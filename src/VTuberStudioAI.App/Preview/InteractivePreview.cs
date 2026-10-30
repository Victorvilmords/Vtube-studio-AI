using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VTuberStudioAI.App.Preview;

/// <summary>
/// Preview 2D simples: o quadro da imagem acompanha o mouse e as áreas de olho marcadas
/// recebem uma pálpebra ao clicar. Não há rigging nem deformação da imagem.
/// </summary>
public sealed class InteractivePreview : FrameworkElement
{
    private const double MaxShiftRatio = 0.015;
    private const double MaxTiltDegrees = 1.5;
    private const double BlinkMilliseconds = 240;

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
        var matrix = Matrix.Identity;
        var center = new Point(image.X + image.Width / 2, image.Y + image.Height / 2);
        matrix.RotateAt(_gaze.X * MaxTiltDegrees, center.X, center.Y);
        matrix.Translate(_gaze.X * image.Width * MaxShiftRatio, _gaze.Y * image.Height * MaxShiftRatio);
        return matrix;
    }

    private static Rect EyeToRect(Rect image, EyeRegion eye) =>
        new(image.X + eye.X * image.Width, image.Y + eye.Y * image.Height,
            eye.Width * image.Width, eye.Height * image.Height);

    protected override void OnRender(DrawingContext drawingContext)
    {
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        var image = ImageRect();
        if (Source is null || image.IsEmpty)
        {
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
        if (image.IsEmpty || Eyes is null)
        {
            return false;
        }

        var normalized = ToNormalized(position, image, applyFollow: true);
        return Eyes.Any(eye => PreviewMath.HitsEye(eye, normalized));
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
            IsFollowing = !IsFollowing;
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
