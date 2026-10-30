using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VTuberStudioAI.App.Images;

namespace VTuberStudioAI.App.Preview;

/// <summary>
/// Preview 2D simples: o quadro da imagem acompanha o mouse e as áreas de olho marcadas
/// recebem uma pálpebra ao clicar. Não há rigging nem deformação da imagem.
/// </summary>
public sealed class InteractivePreview : FrameworkElement
{
    private const double MaxShiftRatio = 0.015;
    private const double MaxTiltDegrees = 1.5;
    private const double SceneHeadShiftRatio = 0.008;
    private const double BodyShiftRatio = 0.02;
    private const double BodyTiltDegrees = 1.2;
    private const double BodyRollShare = 0.3;
    private const double BlinkMilliseconds = 240;
    private const double EarTiltDegrees = 4;
    private const double EyeShiftRatio = 0.22;
    private const double BrowTravelRatio = 0.05;

    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(BitmapSource), typeof(InteractivePreview),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnContentChanged));

    public static readonly DependencyProperty EyesProperty = DependencyProperty.Register(
        nameof(Eyes), typeof(IReadOnlyList<EyeRegion>), typeof(InteractivePreview),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnContentChanged));

    public static readonly DependencyProperty IsMarkingProperty = DependencyProperty.Register(
        nameof(IsMarking), typeof(bool), typeof(InteractivePreview),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MarkCommandProperty = DependencyProperty.Register(
        nameof(MarkCommand), typeof(ICommand), typeof(InteractivePreview));

    public static readonly DependencyProperty IsFollowingProperty = DependencyProperty.Register(
        nameof(IsFollowing), typeof(bool), typeof(InteractivePreview),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnFollowingChanged));

    public static readonly DependencyProperty SceneProperty = DependencyProperty.Register(
        nameof(Scene), typeof(LayerScene), typeof(InteractivePreview),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RigProperty = DependencyProperty.Register(
        nameof(Rig), typeof(RigSettings), typeof(InteractivePreview),
        new FrameworkPropertyMetadata(RigSettings.Default, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsPoseTestingProperty = DependencyProperty.Register(
        nameof(IsPoseTesting), typeof(bool), typeof(InteractivePreview),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TestYawProperty = DependencyProperty.Register(
        nameof(TestYaw), typeof(double), typeof(InteractivePreview),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TestPitchProperty = DependencyProperty.Register(
        nameof(TestPitch), typeof(double), typeof(InteractivePreview),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackingProperty = DependencyProperty.Register(
        nameof(Tracking), typeof(AvatarPose), typeof(InteractivePreview),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly Stopwatch _blinkClock = new();
    private readonly Dictionary<EyeRegion, Color> _eyelidColors = [];
    private Vector _gaze;
    private Vector _gazeTarget;
    private bool _blinking;
    private bool _animating;
    private Point? _dragStart;
    private Point _dragCurrent;

    public InteractivePreview()
    {
        SnapsToDevicePixels = true;
        Focusable = false;
    }

    public BitmapSource? Source
    {
        get => (BitmapSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public IReadOnlyList<EyeRegion>? Eyes
    {
        get => (IReadOnlyList<EyeRegion>?)GetValue(EyesProperty);
        set => SetValue(EyesProperty, value);
    }

    public bool IsMarking
    {
        get => (bool)GetValue(IsMarkingProperty);
        set => SetValue(IsMarkingProperty, value);
    }

    public ICommand? MarkCommand
    {
        get => (ICommand?)GetValue(MarkCommandProperty);
        set => SetValue(MarkCommandProperty, value);
    }

    /// <summary>Quando definido, desenha as camadas separadas seguindo o plano de rig por nomenclatura.</summary>
    public LayerScene? Scene
    {
        get => (LayerScene?)GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    /// <summary>Na tela Play o duplo clique é reservado ao botão de fechar.</summary>
    public bool DoubleClickTogglesFollow { get; set; } = true;

    /// <summary>Calibração da deformação pseudo-3D das camadas.</summary>
    public RigSettings Rig
    {
        get => (RigSettings)GetValue(RigProperty);
        set => SetValue(RigProperty, value ?? RigSettings.Default);
    }

    /// <summary>Quando ligado, o giro vem dos controles de teste (calibração) e não do mouse.</summary>
    public bool IsPoseTesting
    {
        get => (bool)GetValue(IsPoseTestingProperty);
        set => SetValue(IsPoseTestingProperty, value);
    }

    public double TestYaw
    {
        get => (double)GetValue(TestYawProperty);
        set => SetValue(TestYawProperty, value);
    }

    public double TestPitch
    {
        get => (double)GetValue(TestPitchProperty);
        set => SetValue(TestPitchProperty, value);
    }

    /// <summary>Pose vinda da câmera; quando definida, substitui o mouse e os controles de teste.</summary>
    public AvatarPose? Tracking
    {
        get => (AvatarPose?)GetValue(TrackingProperty);
        set => SetValue(TrackingProperty, value);
    }

    private Vector EffectiveGaze() =>
        Tracking is { } tracking
            ? new Vector(tracking.GazeX, tracking.GazeY)
            : IsPoseTesting
            ? new Vector(Math.Clamp(TestYaw, -1, 1), Math.Clamp(TestPitch, -1, 1))
            : _gaze;

    /// <summary>Quando ligado, a cabeça (quadro da imagem) e os olhos acompanham o mouse.</summary>
    public bool IsFollowing
    {
        get => (bool)GetValue(IsFollowingProperty);
        set => SetValue(IsFollowingProperty, value);
    }

    private static void OnFollowingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var preview = (InteractivePreview)d;
        if (!(bool)e.NewValue)
        {
            preview._gazeTarget = new Vector(0, 0);
            preview.StartAnimation();
        }
    }

    private static void OnContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((InteractivePreview)d)._eyelidColors.Clear();

    private Rect ImageRect()
    {
        if (Source is null || Source.PixelWidth == 0 || Source.PixelHeight == 0)
        {
            return Rect.Empty;
        }

        var available = new Size(Math.Max(1, ActualWidth - 24), Math.Max(1, ActualHeight - 24));
        var scale = Math.Min(available.Width / Source.PixelWidth, available.Height / Source.PixelHeight);
        var width = Source.PixelWidth * scale;
        var height = Source.PixelHeight * scale;
        return new Rect((ActualWidth - width) / 2, (ActualHeight - height) / 2, width, height);
    }

    private Matrix FollowMatrix(Rect image)
    {
        if (Scene is { } scene && !IsMarking)
        {
            return HeadMatrix(image, scene);
        }

        var matrix = Matrix.Identity;
        var center = new Point(image.X + image.Width / 2, image.Y + image.Height / 2);
        var gaze = EffectiveGaze();
        matrix.RotateAt(gaze.X * MaxTiltDegrees + (Tracking?.RollDegrees ?? 0), center.X, center.Y);
        matrix.Translate(gaze.X * image.Width * MaxShiftRatio, gaze.Y * image.Height * MaxShiftRatio);
        return matrix;
    }

    /// <summary>O corpo inclina e desliza um pouco com a cabeça; gira em torno da base da imagem.</summary>
    private Matrix BodyMatrix(Rect image)
    {
        var rig = Rig;
        var strength = rig.Enabled ? rig.Overall * rig.Body : 0;
        var gaze = EffectiveGaze();
        var matrix = Matrix.Identity;
        matrix.RotateAt(
            (gaze.X * BodyTiltDegrees + (Tracking?.RollDegrees ?? 0) * BodyRollShare) * strength,
            image.X + image.Width / 2, image.Bottom);
        matrix.Translate(
            gaze.X * image.Width * BodyShiftRatio * strength,
            gaze.Y * image.Height * BodyShiftRatio * 0.6 * strength);
        return matrix;
    }

    /// <summary>A cabeça gira em torno do pescoço e leva junto o movimento do corpo, então nunca se solta dele.</summary>
    private Matrix HeadMatrix(Rect image, LayerScene scene)
    {
        var (neckX, neckY) = scene.NeckPivot;
        var scale = image.Width / scene.Width;
        var neck = new Point(image.X + neckX * scale, image.Y + neckY * scale);
        var gaze = EffectiveGaze();
        var local = Matrix.Identity;
        local.RotateAt(gaze.X * MaxTiltDegrees + (Tracking?.RollDegrees ?? 0), neck.X, neck.Y);
        local.Translate(gaze.X * image.Width * SceneHeadShiftRatio, gaze.Y * image.Height * SceneHeadShiftRatio);
        return Matrix.Multiply(local, BodyMatrix(image));
    }

    private static bool FollowsBody(SceneLayer layer) =>
        layer.Rig.Motion == LayerMotion.Static &&
        layer.Rig.Role.Part is LayerPart.Body or LayerPart.Neck or LayerPart.Tail;

    private const int MouthStrips = 40;
    private const double MouthFlatten = 0.7;
    private const double MouthCornerDrop = 0.32;

    /// <summary>
    /// Deforma a própria arte da boca em faixas verticais. Furry: "V" invertido (Λ) preso à linha do focinho,
    /// com os cantos descendo. Humano: curva deitada (C), centro mais baixo que os cantos.
    /// <paramref name="closed"/> vai de 0 (forma original) a 1 (totalmente fechada).
    /// </summary>
    private static void DrawClosedMouth(DrawingContext drawingContext, BitmapSource bitmap, Rect rect, bool furry, double closed)
    {
        var squash = 1 - MouthFlatten * closed;
        var pivotY = furry ? rect.Y : rect.Y + rect.Height / 2;
        var stripWidth = rect.Width / MouthStrips;
        for (var i = 0; i < MouthStrips; i++)
        {
            var u = (i + 0.5) / MouthStrips * 2 - 1;
            var dy = (furry ? Math.Abs(u) : 1 - u * u) * MouthCornerDrop * rect.Height * closed;
            var strip = new Rect(rect.X + i * stripWidth - 0.3, rect.Y - rect.Height, stripWidth + 0.6, rect.Height * 3);
            drawingContext.PushTransform(new TranslateTransform(0, dy));
            drawingContext.PushTransform(new ScaleTransform(1, squash, 0, pivotY));
            drawingContext.PushClip(new RectangleGeometry(strip));
            drawingContext.DrawImage(bitmap, rect);
            drawingContext.Pop();
            drawingContext.Pop();
            drawingContext.Pop();
        }
    }

    private static Rect EyeToRect(Rect image, EyeRegion eye) =>
        new(image.X + eye.X * image.Width, image.Y + eye.Y * image.Height,
            eye.Width * image.Width, eye.Height * image.Height);

    private void RenderScene(DrawingContext drawingContext, Rect image, LayerScene scene)
    {
        var scale = image.Width / scene.Width;
        var head = new MatrixTransform(HeadMatrix(image, scene));
        var body = new MatrixTransform(BodyMatrix(image));
        var amount = _blinking ? PreviewMath.BlinkAmount(_blinkClock.ElapsedMilliseconds / BlinkMilliseconds) : 0;
        var gaze = EffectiveGaze();
        var rig = Rig;
        Rect RectOf(PsdLayerInfo info) =>
            new(image.X + info.X * scale, image.Y + info.Y * scale, info.Width * scale, info.Height * scale);

        for (var layerIndex = 0; layerIndex < scene.Layers.Count; layerIndex++)
        {
            var layer = scene.Layers[layerIndex];
            if (!LayerScene.IsDrawn(layer) || layer.Bitmap is null)
            {
                continue;
            }

            var mouthOpen = Tracking?.MouthOpen ?? 0;
            if (layer.Rig.Motion == LayerMotion.MouthOpen && mouthOpen < 0.01)
            {
                continue;
            }

            var eyeAmount = amount;
            if (Tracking is { } tracked && layer.Rig.Motion is LayerMotion.EyeBase or LayerMotion.EyeContent or LayerMotion.UpperLash or LayerMotion.LowerLash)
            {
                var side = scene.EyeScreenSide(layerIndex);
                var closed = side < 0
                    ? tracked.ScreenLeftEyeClosed
                    : side > 0 ? tracked.ScreenRightEyeClosed : (tracked.ScreenLeftEyeClosed + tracked.ScreenRightEyeClosed) / 2;
                eyeAmount = Math.Max(amount, closed);
            }

            var rect = RectOf(layer.Info);
            var pushed = 0;
            void Push(Action push)
            {
                push();
                pushed++;
            }

            if (layer.Rig.Motion != LayerMotion.Static)
            {
                Push(() => drawingContext.PushTransform(head));
                var pose = LayerDeform.Pose(scene, layerIndex, gaze.X, gaze.Y, rig);
                if (!pose.IsIdentity)
                {
                    var pivot = new Point(image.X + pose.PivotX * scale, image.Y + pose.PivotY * scale);
                    Push(() => drawingContext.PushTransform(new TranslateTransform(pose.Dx * scale, pose.Dy * scale)));
                    Push(() => drawingContext.PushTransform(new ScaleTransform(pose.ScaleX, pose.ScaleY, pivot.X, pivot.Y)));
                }
            }
            else if (FollowsBody(layer))
            {
                Push(() => drawingContext.PushTransform(body));
            }

            var opacity = layer.Info.Opacity / 255.0;
            var closedMouthAmount = 0.0;
            if (layer.Rig.Motion == LayerMotion.MouthOpen)
            {
                opacity *= mouthOpen;
            }
            else if (layer.Rig.Role.Part == LayerPart.Mouth && scene.HasMouthOpenLayer)
            {
                opacity *= 1 - mouthOpen;
            }
            else if (layer.Rig.Role.Part == LayerPart.Mouth && rig.CloseMouth)
            {
                // A arte da boca é a boca aberta: fechar = deformar a própria arte; abrir volta à forma original.
                closedMouthAmount = 1 - mouthOpen;
            }
            else if (layer.Rig.Role.Part == LayerPart.Mouth && mouthOpen > 0)
            {
                Push(() => drawingContext.PushTransform(
                    new ScaleTransform(1, 1 + 0.7 * mouthOpen, 0, rect.Y + rect.Height / 2)));
            }

            Push(() => drawingContext.PushOpacity(opacity));
            var target = rect;
            switch (layer.Rig.Motion)
            {
                case LayerMotion.Ear:
                    Push(() => drawingContext.PushTransform(
                        new RotateTransform(gaze.X * EarTiltDegrees * rig.Ears, rect.X + rect.Width / 2, rect.Bottom)));
                    break;
                case LayerMotion.Eyebrow:
                    if (Tracking is { } browPose)
                    {
                        var browSide = scene.EyeScreenSide(layerIndex);
                        var raise = browSide < 0
                            ? browPose.ScreenLeftBrow
                            : browSide > 0 ? browPose.ScreenRightBrow : (browPose.ScreenLeftBrow + browPose.ScreenRightBrow) / 2;
                        var browTravel = scene.Head.Width * scale * BrowTravelRatio * (raise > 0 ? raise : raise * 0.6);
                        Push(() => drawingContext.PushTransform(new TranslateTransform(0, -browTravel)));
                    }

                    break;
                case LayerMotion.UpperLash:
                case LayerMotion.LowerLash:
                    if (layer.Rig.MaskIndex is { } eyeIndex)
                    {
                        var eyeRect = RectOf(scene.Layers[eyeIndex].Info);
                        var travel = (1 - Math.Max(0.06, 1 - eyeAmount)) * eyeRect.Height / 2;
                        var dy = layer.Rig.Motion == LayerMotion.UpperLash ? travel : -travel;
                        Push(() => drawingContext.PushTransform(new TranslateTransform(0, dy)));
                    }

                    break;
                case LayerMotion.EyeBase:
                case LayerMotion.EyeContent:
                    var maskLayer = layer.Rig.MaskIndex is { } maskIndex ? scene.Layers[maskIndex] : null;
                    var baseRect = maskLayer is null ? rect : RectOf(maskLayer.Info);
                    var closing = Math.Max(0.06, 1 - eyeAmount);
                    Push(() => drawingContext.PushTransform(
                        new ScaleTransform(1, closing, 0, baseRect.Y + baseRect.Height / 2)));
                    if (layer.Rig.Motion == LayerMotion.EyeContent)
                    {
                        if (maskLayer?.Bitmap is { } maskBitmap)
                        {
                            Push(() => drawingContext.PushOpacityMask(new ImageBrush(maskBitmap)
                            {
                                Viewport = RectOf(maskLayer.Info),
                                ViewportUnits = BrushMappingMode.Absolute,
                                Stretch = Stretch.Fill,
                                TileMode = TileMode.None
                            }));
                        }

                        var eyeGaze = Tracking is { } irisPose
                            ? new Vector(Math.Clamp(gaze.X + irisPose.IrisX, -1.3, 1.3), Math.Clamp(gaze.Y + irisPose.IrisY, -1.3, 1.3))
                            : gaze;
                        target = new Rect(
                            rect.X + eyeGaze.X * baseRect.Width * EyeShiftRatio * rig.EyeLook,
                            rect.Y + eyeGaze.Y * baseRect.Height * EyeShiftRatio * rig.EyeLook,
                            rect.Width,
                            rect.Height);
                    }

                    break;
            }

            if (closedMouthAmount > 0.01)
            {
                DrawClosedMouth(drawingContext, layer.Bitmap, rect, scene.IsFurry, closedMouthAmount);
            }
            else
            {
                drawingContext.DrawImage(layer.Bitmap, target);
            }

            for (var i = 0; i < pushed; i++)
            {
                drawingContext.Pop();
            }
        }
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        var image = ImageRect();
        if (Source is null || image.IsEmpty)
        {
            return;
        }

        if (Scene is { } scene && !IsMarking)
        {
            RenderScene(drawingContext, image, scene);
            return;
        }

        var follow = IsMarking ? Matrix.Identity : FollowMatrix(image);
        drawingContext.PushTransform(new MatrixTransform(follow));
        drawingContext.DrawImage(Source, image);

        var amount = _blinking ? PreviewMath.BlinkAmount(_blinkClock.ElapsedMilliseconds / BlinkMilliseconds) : 0;
        foreach (var eye in Eyes ?? [])
        {
            var rect = EyeToRect(image, eye);
            if (!IsMarking && _gaze.Length > 0.01)
            {
                // Desloca o conteúdo da área do olho para sugerir o olhar; não há camada de íris separada.
                drawingContext.PushClip(new RectangleGeometry(rect));
                drawingContext.DrawImage(Source, new Rect(
                    image.X + _gaze.X * rect.Width * 0.12,
                    image.Y + _gaze.Y * rect.Height * 0.12,
                    image.Width,
                    image.Height));
                drawingContext.Pop();
            }
            if (amount > 0)
            {
                if (!_eyelidColors.TryGetValue(eye, out var color))
                {
                    color = PreviewMath.SampleEyelidColor(Source, eye);
                    _eyelidColors[eye] = color;
                }

                var lid = new Rect(rect.X, rect.Y, rect.Width, rect.Height * amount);
                drawingContext.DrawRoundedRectangle(new SolidColorBrush(color), null, lid, 3, 3);
            }

            if (IsMarking)
            {
                var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x38, 0xC6, 0xE8)), 1.5) { DashStyle = DashStyles.Dash };
                drawingContext.DrawRectangle(null, pen, rect);
            }
        }

        drawingContext.Pop();

        if (_dragStart is { } start)
        {
            var pen = new Pen(Brushes.White, 1.5) { DashStyle = DashStyles.Dash };
            drawingContext.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, 0x38, 0xC6, 0xE8)), pen, new Rect(start, _dragCurrent));
        }
    }

    private Point ToNormalized(Point position, Rect image, bool applyFollow)
    {
        if (applyFollow && !IsMarking)
        {
            var inverse = FollowMatrix(image);
            inverse.Invert();
            position = inverse.Transform(position);
        }

        return new Point((position.X - image.X) / image.Width, (position.Y - image.Y) / image.Height);
    }

    private bool IsOverEye(Point position)
    {
        var image = ImageRect();
        if (image.IsEmpty)
        {
            return false;
        }

        if (Scene is { } scene && !IsMarking)
        {
            return IsOverSceneEye(position, image, scene);
        }

        if (Eyes is null)
        {
            return false;
        }

        var normalized = ToNormalized(position, image, applyFollow: true);
        return Eyes.Any(eye => PreviewMath.HitsEye(eye, normalized));
    }

    // Os olhos se movem com a deformação; o clique precisa acertar o olho onde ele é desenhado.
    private bool IsOverSceneEye(Point position, Rect image, LayerScene scene)
    {
        var inverse = FollowMatrix(image);
        inverse.Invert();
        var point = inverse.Transform(position);
        var scale = image.Width / scene.Width;
        var gaze = EffectiveGaze();
        for (var index = 0; index < scene.Layers.Count; index++)
        {
            var layer = scene.Layers[index];
            if (layer.Rig.Motion != LayerMotion.EyeBase || !layer.Info.Visible)
            {
                continue;
            }

            var pose = LayerDeform.Pose(scene, index, gaze.X, gaze.Y, Rig);
            var width = layer.Info.Width * scale * pose.ScaleX;
            var height = layer.Info.Height * scale * pose.ScaleY;
            var centerX = image.X + (layer.Info.X + layer.Info.Width / 2.0 + pose.Dx) * scale;
            var centerY = image.Y + (layer.Info.Y + layer.Info.Height / 2.0 + pose.Dy) * scale;
            if (new Rect(centerX - width / 2, centerY - height / 2, width, height).Contains(point))
            {
                return true;
            }
        }

        return false;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var position = e.GetPosition(this);
        if (IsMarking)
        {
            Cursor = Cursors.Cross;
            if (_dragStart is not null)
            {
                _dragCurrent = ClampToImage(position);
                InvalidateVisual();
            }

            return;
        }

        Cursor = IsOverEye(position) ? Cursors.Hand : Cursors.Arrow;
        _gazeTarget = IsFollowing
            ? PreviewMath.GazeFromMouse(position, new Size(ActualWidth, ActualHeight))
            : new Vector(0, 0);
        StartAnimation();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _gazeTarget = new Vector(0, 0);
        StartAnimation();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var position = e.GetPosition(this);
        var image = ImageRect();
        if (image.IsEmpty)
        {
            return;
        }

        if (IsMarking)
        {
            if (image.Contains(position))
            {
                _dragStart = position;
                _dragCurrent = position;
                CaptureMouse();
            }

            return;
        }

        if (e.ClickCount == 2)
        {
            if (DoubleClickTogglesFollow)
            {
                IsFollowing = !IsFollowing;
            }

            return;
        }

        if (IsOverEye(position))
        {
            _blinking = true;
            _blinkClock.Restart();
            StartAnimation();
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragStart is not { } start)
        {
            return;
        }

        ReleaseMouseCapture();
        var image = ImageRect();
        var end = ClampToImage(e.GetPosition(this));
        _dragStart = null;
        InvalidateVisual();
        var area = new Rect(start, end);
        var region = new EyeRegion(
            (area.X - image.X) / image.Width,
            (area.Y - image.Y) / image.Height,
            area.Width / image.Width,
            area.Height / image.Height);
        MarkCommand?.Execute(region);
    }

    private Point ClampToImage(Point point)
    {
        var image = ImageRect();
        return image.IsEmpty
            ? point
            : new Point(Math.Clamp(point.X, image.Left, image.Right), Math.Clamp(point.Y, image.Top, image.Bottom));
    }

    private void StartAnimation()
    {
        if (_animating)
        {
            return;
        }

        _animating = true;
        CompositionTarget.Rendering += OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        _gaze += (_gazeTarget - _gaze) * 0.18;
        var gazeSettled = (_gazeTarget - _gaze).Length < 0.002;
        if (gazeSettled)
        {
            _gaze = _gazeTarget;
        }

        if (_blinking && _blinkClock.ElapsedMilliseconds >= BlinkMilliseconds)
        {
            _blinking = false;
        }

        InvalidateVisual();
        if (gazeSettled && !_blinking)
        {
            CompositionTarget.Rendering -= OnFrame;
            _animating = false;
        }
    }
}
