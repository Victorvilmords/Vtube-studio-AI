namespace VTuberStudioAI.App.Tracking;

/// <summary>Quadro de câmera em pixels BGRA (4 bytes por pixel, sem preenchimento entre linhas).</summary>
public sealed record BgraFrame(byte[] Pixels, int Width, int Height)
{
    public bool IsValid => Width > 0 && Height > 0 && Pixels.Length >= (long)Width * Height * 4;

    /// <summary>Reduz por amostragem simples para caber em <paramref name="maxWidth"/>; devolve o próprio quadro se já couber.</summary>
    public BgraFrame Downscale(int maxWidth)
    {
        if (!IsValid || Width <= maxWidth)
        {
            return this;
        }

        var factor = (double)Width / maxWidth;
        var newWidth = maxWidth;
        var newHeight = Math.Max(1, (int)(Height / factor));
        var output = new byte[newWidth * newHeight * 4];
        for (var y = 0; y < newHeight; y++)
        {
            var sourceY = Math.Min(Height - 1, (int)(y * factor));
            for (var x = 0; x < newWidth; x++)
            {
                var sourceX = Math.Min(Width - 1, (int)(x * factor));
                Buffer.BlockCopy(Pixels, (sourceY * Width + sourceX) * 4, output, (y * newWidth + x) * 4, 4);
            }
        }

        return new BgraFrame(output, newWidth, newHeight);
    }
}

/// <summary>
/// Medidas brutas do rosto na imagem da câmera (sem espelhar). "Esquerda/direita" aqui são lados da IMAGEM:
/// o olho da esquerda da imagem é o olho direito da pessoa.
/// </summary>
public sealed record FaceMeasurements(
    double YawDegrees,
    double PitchDegrees,
    double RollDegrees,
    double MouthRatio,
    double EyeImageLeftRatio,
    double EyeImageRightRatio,
    double IrisX = 0,
    double IrisY = 0,
    bool HasIris = false,
    double BrowImageLeft = 0,
    double BrowImageRight = 0);

/// <summary>Ferramenta de detecção facial. Cada implementação roda 100% local.</summary>
public interface IFaceTracker : IDisposable
{
    /// <summary>Devolve as medidas do rosto principal ou nulo quando não há rosto no quadro.</summary>
    FaceMeasurements? Track(BgraFrame frame);

    /// <summary>Esquece o rosto anterior (usar ao trocar de câmera).</summary>
    void Reset();
}

public sealed record FaceTrackerInfo(string Id, string Name, string Description, Func<IFaceTracker> Create, Func<string?> CheckAvailability);

public static class FaceTrackerCatalog
{
    public const string MediaPipeId = "mediapipe-onnx";

    public static IReadOnlyList<FaceTrackerInfo> Available { get; } =
    [
        new(
            MediaPipeId,
            "MediaPipe Face Mesh (local)",
            "Detecta o rosto e 478 pontos (olhos, boca e cabeça) no seu computador, com modelos ONNX. Nenhuma imagem sai do PC.",
            () => new MediaPipeFaceTracker(),
            MediaPipeFaceTracker.CheckModels)
    ];
}
