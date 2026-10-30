using System.Text.Json;
using VTuberStudioAI.App.Preview;
using VTuberStudioAI.App.Projects;

namespace VTuberStudioAI.App.Tracking;

/// <summary>Ajustes do rastreamento facial salvos no projeto (chave tracking.settings).</summary>
public sealed record FaceTrackingSettings(
    string ToolId,
    bool Mirror,
    double HeadGain,
    double MouthGain,
    double Smoothing,
    double YawOffset,
    double PitchOffset,
    double RollOffset,
    double EyeOpenImageLeft,
    double EyeOpenImageRight,
    double MouthClosed,
    double IrisGain = 1.0,
    double IrisXOffset = 0,
    double IrisYOffset = 0,
    double BrowGain = 1.0,
    double BrowRestImageLeft = 0,
    double BrowRestImageRight = 0)
{
    public const string ConfigurationKey = "tracking.settings";

    public static FaceTrackingSettings Default { get; } = new(
        FaceTrackerCatalog.MediaPipeId, true, 1.0, 1.0, 0.4, 0, 0, 0, 0.28, 0.28, 0.02);

    public bool IsValid =>
        FaceTrackerCatalog.Available.Any(tool => tool.Id == ToolId) &&
        InRange(HeadGain, 0.2, 3) && InRange(MouthGain, 0.2, 3) && InRange(Smoothing, 0, 1) &&
        InRange(YawOffset, -60, 60) && InRange(PitchOffset, -60, 60) && InRange(RollOffset, -60, 60) &&
        InRange(EyeOpenImageLeft, 0.12, 0.6) && InRange(EyeOpenImageRight, 0.12, 0.6) && InRange(MouthClosed, 0, 0.2) &&
        InRange(IrisGain, 0, 3) && InRange(IrisXOffset, -0.5, 0.5) && InRange(IrisYOffset, -0.5, 0.5) &&
        InRange(BrowGain, 0, 3) && ValidBrowRest(BrowRestImageLeft) && ValidBrowRest(BrowRestImageRight);

    /// <summary>As sobrancelhas só se mexem depois da calibração: sem ela o repouso fica em 0.</summary>
    public bool BrowsCalibrated => BrowRestImageLeft > 0 && BrowRestImageRight > 0;

    private static bool ValidBrowRest(double value) => value == 0 || InRange(value, 0.02, 0.4);

    private static bool InRange(double value, double min, double max) => double.IsFinite(value) && value >= min && value <= max;
}

public static class TrackingSettingsStore
{
    public static FaceTrackingSettings Read(ProjectDocument project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (!project.Configurations.TryGetValue(FaceTrackingSettings.ConfigurationKey, out var element))
        {
            return FaceTrackingSettings.Default;
        }

        try
        {
            var settings = element.Deserialize<FaceTrackingSettings>();
            return settings is { IsValid: true } ? settings : FaceTrackingSettings.Default;
        }
        catch (JsonException)
        {
            return FaceTrackingSettings.Default;
        }
    }

    public static void Save(IProjectRepository repository, Guid projectId, FaceTrackingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.IsValid)
        {
            throw new ArgumentException("Os ajustes de rastreamento têm valores fora do permitido.");
        }

        var project = repository.Load(projectId);
        var configurations = new Dictionary<string, JsonElement>(project.Configurations, StringComparer.Ordinal)
        {
            [FaceTrackingSettings.ConfigurationKey] = JsonSerializer.SerializeToElement(settings)
        };
        repository.Save(project with { Configurations = configurations });
    }
}

/// <summary>Transforma medidas do rosto em pose do avatar, já considerando espelho, ganhos e calibração.</summary>
public static class FaceMotionMapper
{
    private const double YawRange = 30;
    private const double PitchRange = 20;
    private const double MaxRoll = 25;
    private const double MouthRange = 0.15;
    private const double IrisRangeX = 0.10;
    private const double IrisRangeY = 0.07;
    private const double BrowRange = 0.04;

    public static AvatarPose Map(FaceMeasurements m, FaceTrackingSettings s)
    {
        ArgumentNullException.ThrowIfNull(m);
        ArgumentNullException.ThrowIfNull(s);
        var sign = s.Mirror ? -1 : 1;
        var yaw = (m.YawDegrees - s.YawOffset) * sign;
        var pitch = m.PitchDegrees - s.PitchOffset;
        var roll = (m.RollDegrees - s.RollOffset) * sign;

        var screenLeft = s.Mirror
            ? Closed(m.EyeImageRightRatio, s.EyeOpenImageRight)
            : Closed(m.EyeImageLeftRatio, s.EyeOpenImageLeft);
        var screenRight = s.Mirror
            ? Closed(m.EyeImageLeftRatio, s.EyeOpenImageLeft)
            : Closed(m.EyeImageRightRatio, s.EyeOpenImageRight);

        return new AvatarPose(
            Math.Clamp(yaw * s.HeadGain / YawRange, -1, 1),
            Math.Clamp(pitch * s.HeadGain / PitchRange, -1, 1),
            Math.Clamp(roll * s.HeadGain, -MaxRoll, MaxRoll),
            Math.Clamp((m.MouthRatio - s.MouthClosed) / MouthRange * s.MouthGain, 0, 1),
            screenLeft,
            screenRight,
            m.HasIris ? Math.Clamp((m.IrisX - s.IrisXOffset) * sign / IrisRangeX * s.IrisGain, -1, 1) : 0,
            m.HasIris ? Math.Clamp((m.IrisY - s.IrisYOffset) / IrisRangeY * s.IrisGain, -1, 1) : 0,
            s.Mirror ? Brow(m.BrowImageRight, s.BrowRestImageRight, s.BrowGain) : Brow(m.BrowImageLeft, s.BrowRestImageLeft, s.BrowGain),
            s.Mirror ? Brow(m.BrowImageLeft, s.BrowRestImageLeft, s.BrowGain) : Brow(m.BrowImageRight, s.BrowRestImageRight, s.BrowGain));
    }

    private static double Brow(double height, double rest, double gain) =>
        rest > 0 ? Math.Clamp((height - rest) / BrowRange * gain, -1, 1) : 0;

    private static double Closed(double ratio, double open)
    {
        var t = Math.Clamp((ratio - 0.35 * open) / (0.65 * open), 0, 1);
        return 1 - t * t * (3 - 2 * t);
    }
}

/// <summary>Suavização exponencial: cabeça mais lenta (evita tremor), olhos e boca mais rápidos.</summary>
public sealed class PoseSmoother
{
    private AvatarPose? _current;

    public AvatarPose Apply(AvatarPose target, double smoothing)
    {
        ArgumentNullException.ThrowIfNull(target);
        var s = Math.Clamp(smoothing, 0, 1);
        var head = 1 - 0.9 * s;
        var fast = 1 - 0.6 * s;
        if (_current is null)
        {
            _current = target;
            return target;
        }

        var c = _current;
        _current = new AvatarPose(
            Lerp(c.GazeX, target.GazeX, head),
            Lerp(c.GazeY, target.GazeY, head),
            Lerp(c.RollDegrees, target.RollDegrees, head),
            Lerp(c.MouthOpen, target.MouthOpen, fast),
            Lerp(c.ScreenLeftEyeClosed, target.ScreenLeftEyeClosed, fast),
            Lerp(c.ScreenRightEyeClosed, target.ScreenRightEyeClosed, fast),
            Lerp(c.IrisX, target.IrisX, fast),
            Lerp(c.IrisY, target.IrisY, fast),
            Lerp(c.ScreenLeftBrow, target.ScreenLeftBrow, fast),
            Lerp(c.ScreenRightBrow, target.ScreenRightBrow, fast));
        return _current;
    }

    public void Reset() => _current = null;

    private static double Lerp(double from, double to, double alpha) => from + (to - from) * alpha;
}

public sealed record CalibrationResult(FaceTrackingSettings? Settings, string? Error);

public static class FaceCalibrationBuilder
{
    public const int MinimumSamples = 15;

    /// <summary>Usa a mediana de amostras do rosto neutro (olhos abertos, boca fechada, olhando para a tela).</summary>
    public static CalibrationResult Build(IReadOnlyList<FaceMeasurements> samples, FaceTrackingSettings current)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(current);
        if (samples.Count < MinimumSamples)
        {
            return new(null, "Poucas amostras: fique com o rosto visível para a câmera durante a calibração.");
        }

        var eyeLeft = Median(samples.Select(m => m.EyeImageLeftRatio));
        var eyeRight = Median(samples.Select(m => m.EyeImageRightRatio));
        if (eyeLeft < 0.12 || eyeRight < 0.12)
        {
            return new(null, "Seus olhos pareciam fechados. Mantenha-os abertos e tente de novo.");
        }

        var irisSamples = samples.Where(m => m.HasIris).ToList();
        var settings = current with
        {
            YawOffset = Median(samples.Select(m => m.YawDegrees)),
            PitchOffset = Median(samples.Select(m => m.PitchDegrees)),
            RollOffset = Median(samples.Select(m => m.RollDegrees)),
            EyeOpenImageLeft = Math.Min(eyeLeft, 0.6),
            EyeOpenImageRight = Math.Min(eyeRight, 0.6),
            MouthClosed = Math.Clamp(Median(samples.Select(m => m.MouthRatio)), 0, 0.2),
            IrisXOffset = irisSamples.Count >= MinimumSamples ? Math.Clamp(Median(irisSamples.Select(m => m.IrisX)), -0.5, 0.5) : current.IrisXOffset,
            IrisYOffset = irisSamples.Count >= MinimumSamples ? Math.Clamp(Median(irisSamples.Select(m => m.IrisY)), -0.5, 0.5) : current.IrisYOffset,
            BrowRestImageLeft = BrowRest(samples.Select(m => m.BrowImageLeft), current.BrowRestImageLeft),
            BrowRestImageRight = BrowRest(samples.Select(m => m.BrowImageRight), current.BrowRestImageRight)
        };
        return settings.IsValid
            ? new(settings, null)
            : new(null, "As medidas da calibração ficaram fora do esperado. Ajuste a posição e tente de novo.");
    }

    private static double BrowRest(IEnumerable<double> values, double fallback)
    {
        var median = Median(values);
        return median is >= 0.02 and <= 0.4 ? median : fallback;
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }
}
