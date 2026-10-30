using System.IO;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace VTuberStudioAI.App.Tracking;

/// <summary>
/// Rastreador local: BlazeFace (detecta o rosto) + Face Landmarker (478 pontos), ambos via ONNX Runtime na CPU.
/// Depois do primeiro acerto reaproveita a região dos pontos anteriores e só roda o detector se o rosto se perder.
/// </summary>
public sealed class MediaPipeFaceTracker : IFaceTracker
{
    public const string DetectorFile = "face_detection_short_range.onnx";
    public const string LandmarkerFile = "face_landmarker_Nx3x256x256.onnx";

    private const int DetectorSize = 128;
    private const int LandmarkSize = 256;
    private const int AnchorCount = 896;
    private const float DetectionThreshold = 0.5f;
    private const float PresenceThreshold = 0.5f;

    private readonly InferenceSession _detector;
    private readonly InferenceSession _landmarker;
    private readonly string _detectorInput;
    private readonly string _landmarkerInput;
    private readonly (float X, float Y)[] _anchors = BuildAnchors();
    private Roi? _roi;

    private readonly record struct Roi(double CenterX, double CenterY, double Side, double Angle);

    public MediaPipeFaceTracker() : this(ModelDirectory)
    {
    }

    public MediaPipeFaceTracker(string modelDirectory)
    {
        var detectorPath = Path.Combine(modelDirectory, DetectorFile);
        var landmarkerPath = Path.Combine(modelDirectory, LandmarkerFile);
        if (!File.Exists(detectorPath) || !File.Exists(landmarkerPath))
        {
            throw new FileNotFoundException("Os modelos de rastreamento facial não foram encontrados na pasta Models do aplicativo.");
        }

        using var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        _detector = new InferenceSession(detectorPath, options);
        _landmarker = new InferenceSession(landmarkerPath, options);
        _detectorInput = _detector.InputMetadata.Keys.First();
        _landmarkerInput = _landmarker.InputMetadata.Keys.First();
    }

    public static string ModelDirectory => Path.Combine(AppContext.BaseDirectory, "Models");

    /// <summary>Mensagem de erro se os modelos não estiverem disponíveis; nulo se estiver tudo certo.</summary>
    public static string? CheckModels() =>
        File.Exists(Path.Combine(ModelDirectory, DetectorFile)) && File.Exists(Path.Combine(ModelDirectory, LandmarkerFile))
            ? null
            : "Os modelos de rastreamento facial não foram encontrados na pasta Models ao lado do aplicativo. Reinstale ou republique o app.";

    public void Reset() => _roi = null;

    public FaceMeasurements? Track(BgraFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (!frame.IsValid)
        {
            return null;
        }

        var roi = _roi ?? Detect(frame);
        if (roi is null)
        {
            return null;
        }

        var landmarks = Landmark(frame, roi.Value, out var nextRoi);
        if (landmarks is null)
        {
            _roi = null;
            return null;
        }

        _roi = nextRoi;
        return FaceLandmarkMath.Measure(landmarks);
    }

    public void Dispose()
    {
        _detector.Dispose();
        _landmarker.Dispose();
    }

    private Roi? Detect(BgraFrame frame)
    {
        var scale = (double)DetectorSize / Math.Max(frame.Width, frame.Height);
        var padX = (DetectorSize - frame.Width * scale) / 2;
        var padY = (DetectorSize - frame.Height * scale) / 2;
        var tensor = new DenseTensor<float>([1, 3, DetectorSize, DetectorSize]);
        for (var y = 0; y < DetectorSize; y++)
        {
            for (var x = 0; x < DetectorSize; x++)
            {
                var sx = (x + 0.5 - padX) / scale;
                var sy = (y + 0.5 - padY) / scale;
                var (r, g, b) = Sample(frame, sx, sy);
                tensor[0, 0, y, x] = (r - 127.5f) / 127.5f;
                tensor[0, 1, y, x] = (g - 127.5f) / 127.5f;
                tensor[0, 2, y, x] = (b - 127.5f) / 127.5f;
            }
        }

        float[]? regressors = null;
        float[]? scores = null;
        using (var results = _detector.Run([NamedOnnxValue.CreateFromTensor(_detectorInput, tensor)]))
        {
            foreach (var result in results)
            {
                var values = result.AsTensor<float>().ToArray();
                if (values.Length == AnchorCount * 16)
                {
                    regressors = values;
                }
                else if (values.Length == AnchorCount)
                {
                    scores = values;
                }
            }
        }

        if (regressors is null || scores is null)
        {
            throw new InvalidOperationException("O modelo de detecção facial retornou um formato inesperado.");
        }

        var best = -1;
        var bestScore = DetectionThreshold;
        for (var i = 0; i < AnchorCount; i++)
        {
            var score = Sigmoid(scores[i]);
            if (score > bestScore)
            {
                bestScore = score;
                best = i;
            }
        }

        if (best < 0)
        {
            return null;
        }

        var (ax, ay) = _anchors[best];
        var offset = best * 16;
        double ToImageX(double normalized) => (normalized * DetectorSize - padX) / scale;
        double ToImageY(double normalized) => (normalized * DetectorSize - padY) / scale;

        var width = regressors[offset + 2] / DetectorSize * DetectorSize / scale;
        var height = regressors[offset + 3] / DetectorSize * DetectorSize / scale;
        var centerX = ToImageX(regressors[offset] / DetectorSize + ax);
        var centerY = ToImageY(regressors[offset + 1] / DetectorSize + ay);
        var leftEyeX = ToImageX(regressors[offset + 4] / DetectorSize + ax);
        var leftEyeY = ToImageY(regressors[offset + 5] / DetectorSize + ay);
        var rightEyeX = ToImageX(regressors[offset + 6] / DetectorSize + ax);
        var rightEyeY = ToImageY(regressors[offset + 7] / DetectorSize + ay);
        var angle = Math.Atan2(rightEyeY - leftEyeY, rightEyeX - leftEyeX);
        var side = 1.5 * Math.Max(width, height);
        if (!double.IsFinite(side) || side < 8 || !double.IsFinite(angle))
        {
            return null;
        }

        return new Roi(centerX, centerY, side, angle);
    }

    private float[]? Landmark(BgraFrame frame, Roi roi, out Roi? nextRoi)
    {
        nextRoi = null;
        var tensor = new DenseTensor<float>([1, 3, LandmarkSize, LandmarkSize]);
        var cos = Math.Cos(roi.Angle);
        var sin = Math.Sin(roi.Angle);
        for (var y = 0; y < LandmarkSize; y++)
        {
            for (var x = 0; x < LandmarkSize; x++)
            {
                var lx = ((x + 0.5) / LandmarkSize - 0.5) * roi.Side;
                var ly = ((y + 0.5) / LandmarkSize - 0.5) * roi.Side;
                var sx = roi.CenterX + cos * lx - sin * ly;
                var sy = roi.CenterY + sin * lx + cos * ly;
                var (r, g, b) = Sample(frame, sx, sy);
                tensor[0, 0, y, x] = r / 255f;
                tensor[0, 1, y, x] = g / 255f;
                tensor[0, 2, y, x] = b / 255f;
            }
        }

        float[]? points = null;
        float? presence = null;
        using (var results = _landmarker.Run([NamedOnnxValue.CreateFromTensor(_landmarkerInput, tensor)]))
        {
            foreach (var result in results)
            {
                var values = result.AsTensor<float>().ToArray();
                if (values.Length >= FaceLandmarkMath.MinimumLandmarks * 3)
                {
                    points = values;
                }
                else if (values.Length == 1)
                {
                    presence = values[0];
                }
            }
        }

        if (points is null)
        {
            throw new InvalidOperationException("O modelo de pontos faciais retornou um formato inesperado.");
        }

        if (presence is { } raw && (raw < 0 || raw > 1 ? Sigmoid(raw) : raw) < PresenceThreshold)
        {
            return null;
        }

        var k = roi.Side / LandmarkSize;
        var count = points.Length / 3;
        var mapped = new float[count * 3];
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        for (var i = 0; i < count; i++)
        {
            var lx = (points[i * 3] - LandmarkSize / 2.0) * k;
            var ly = (points[i * 3 + 1] - LandmarkSize / 2.0) * k;
            var px = roi.CenterX + cos * lx - sin * ly;
            var py = roi.CenterY + sin * lx + cos * ly;
            mapped[i * 3] = (float)px;
            mapped[i * 3 + 1] = (float)py;
            mapped[i * 3 + 2] = (float)(points[i * 3 + 2] * k);
            minX = Math.Min(minX, px);
            maxX = Math.Max(maxX, px);
            minY = Math.Min(minY, py);
            maxY = Math.Max(maxY, py);
        }

        var eyeDx = mapped[263 * 3] - mapped[33 * 3];
        var eyeDy = mapped[263 * 3 + 1] - mapped[33 * 3 + 1];
        var nextSide = 1.5 * Math.Max(maxX - minX, maxY - minY);
        if (nextSide >= 8 && double.IsFinite(nextSide))
        {
            nextRoi = new Roi((minX + maxX) / 2, (minY + maxY) / 2, nextSide, Math.Atan2(eyeDy, eyeDx));
        }

        return nextRoi is null ? null : mapped;
    }

    private static (float R, float G, float B) Sample(BgraFrame frame, double x, double y)
    {
        var ix = (int)Math.Floor(x);
        var iy = (int)Math.Floor(y);
        if (ix < 0 || iy < 0 || ix >= frame.Width || iy >= frame.Height)
        {
            return (0, 0, 0);
        }

        var offset = (iy * frame.Width + ix) * 4;
        return (frame.Pixels[offset + 2], frame.Pixels[offset + 1], frame.Pixels[offset]);
    }

    private static float Sigmoid(float value) => 1f / (1f + MathF.Exp(-value));

    private static (float X, float Y)[] BuildAnchors()
    {
        var anchors = new List<(float, float)>(AnchorCount);
        (int Cells, int PerCell)[] layers = [(16, 2), (8, 6)];
        foreach (var (cells, perCell) in layers)
        {
            for (var y = 0; y < cells; y++)
            {
                for (var x = 0; x < cells; x++)
                {
                    for (var n = 0; n < perCell; n++)
                    {
                        anchors.Add(((x + 0.5f) / cells, (y + 0.5f) / cells));
                    }
                }
            }
        }

        return [.. anchors];
    }
}
