using System.IO;
using System.Windows.Media.Imaging;
using VTuberStudioAI.App.Images;

namespace VTuberStudioAI.App.Preview;

public sealed record SceneLayer(PsdLayerInfo Info, LayerRigEntry Rig, BitmapSource? Bitmap);

/// <summary>Camadas já decodificadas e o plano de rig usado pelo preview em camadas.</summary>
public sealed class LayerScene(int width, int height, IReadOnlyList<SceneLayer> layers)
{
    public int Width { get; } = width;

    public int Height { get; } = height;

    public IReadOnlyList<SceneLayer> Layers { get; } = layers;

    /// <summary>Camadas de boca aberta são desenhadas mesmo se ocultas no PSD; só aparecem conforme a boca abre.</summary>
    public static bool IsDrawn(SceneLayer layer) => layer.Info.Visible || layer.Rig.Motion == LayerMotion.MouthOpen;

    public bool HasMouthOpenLayer => Layers.Any(layer => layer.Rig.Motion == LayerMotion.MouthOpen && layer.Bitmap is not null);

    /// <summary>
    /// Lado do olho na tela: -1 esquerda, 1 direita, 0 centralizado/indefinido. Vem da posição do olho em relação
    /// à cabeça, não do sufixo L/R, para não depender de qual lado o artista chamou de "esquerdo".
    /// </summary>
    public int EyeScreenSide(int layerIndex)
    {
        var layer = Layers[layerIndex];
        var anchorIndex = layer.Rig.Motion == LayerMotion.EyeBase ? layerIndex : layer.Rig.MaskIndex ?? layerIndex;
        var info = Layers[anchorIndex].Info;
        var (headX, _, headWidth) = Head;
        var offset = info.X + info.Width / 2.0 - headX;
        return Math.Abs(offset) < headWidth * 0.04 ? 0 : Math.Sign(offset);
    }

    /// <summary>Personagem furry (prefixo F ou focinho): define o desenho da boca fechada.</summary>
    public bool IsFurry => Layers.Any(layer => layer.Rig.Role.IsFurry || layer.Rig.Role.Part == LayerPart.Snout);

    /// <summary>Ponto onde a cabeça se prende ao corpo (base da cabeça), em pixels do documento.</summary>
    public (double X, double Y) NeckPivot
    {
        get
        {
            var head = Layers.FirstOrDefault(layer =>
                layer.Rig.Role.Part == LayerPart.Head && layer.Info.Visible && layer.Info.Width > 0 && layer.Info.Height > 0);
            if (head is not null)
            {
                return (head.Info.X + head.Info.Width / 2.0, head.Info.Y + head.Info.Height * 0.92);
            }

            var (x, y, width) = Head;
            return (x, y + width * 0.4);
        }
    }

    private (double X, double Y, double Width)? _head;

    /// <summary>Centro e largura estimados da cabeça (pivô do giro), em pixels do documento.</summary>
    public (double X, double Y, double Width) Head => _head ??= EstimateHead();

    private (double X, double Y, double Width) EstimateHead()
    {
        var visible = Layers.Where(layer => layer.Info.Visible && layer.Info.Width > 0 && layer.Info.Height > 0).ToList();
        var head = visible.FirstOrDefault(layer => layer.Rig.Role.Part == LayerPart.Head);
        if (head is not null)
        {
            return (head.Info.X + head.Info.Width / 2.0, head.Info.Y + head.Info.Height / 2.0, head.Info.Width);
        }

        var face = visible.Where(layer => layer.Rig.Role.Part is LayerPart.EyeWhite or LayerPart.Nose or
            LayerPart.Mouth or LayerPart.Snout).ToList();
        if (face.Count > 0)
        {
            var left = face.Min(layer => layer.Info.X);
            var right = face.Max(layer => layer.Info.X + layer.Info.Width);
            var top = face.Min(layer => layer.Info.Y);
            var bottom = face.Max(layer => layer.Info.Y + layer.Info.Height);
            return ((left + right) / 2.0, (top + bottom) / 2.0, Math.Max((right - left) * 1.8, Width * 0.2));
        }

        return (Width / 2.0, Height / 2.0, Width * 0.4);
    }

    /// <summary>Áreas clicáveis dos olhos (camadas eyewhite visíveis), normalizadas pelo tamanho do documento.</summary>
    public IReadOnlyList<EyeRegion> EyeRegions =>
        Layers
            .Where(layer => layer.Rig.Motion == LayerMotion.EyeBase && layer.Info.Visible)
            .Select(layer => new EyeRegion(
                (double)Math.Max(0, layer.Info.X) / Width,
                (double)Math.Max(0, layer.Info.Y) / Height,
                (double)(Math.Min(Width, layer.Info.X + layer.Info.Width) - Math.Max(0, layer.Info.X)) / Width,
                (double)(Math.Min(Height, layer.Info.Y + layer.Info.Height) - Math.Max(0, layer.Info.Y)) / Height))
            .Where(EyePreviewService.IsValid)
            .ToList();
}

public static class LayerSceneLoader
{
    private const int MaxDecodeDimension = 2048;
    private const long MaxDecodedPixels = 150_000_000;

    private static bool NeedsBitmap(PsdLayerInfo layer) =>
        layer.Visible || LayerNaming.Parse(layer.Name).Part == LayerPart.MouthOpen;

    /// <summary>
    /// Decodifica as camadas visíveis em resolução reduzida. Retorna nulo se o total de pixels passar
    /// do orçamento de memória do preview; falhas de arquivo viram exceção com mensagem clara.
    /// </summary>
    public static Dictionary<string, BitmapSource>? LoadBitmaps(
        IReadOnlyList<PsdLayerInfo> layers, Func<string, string> resolvePath, int documentWidth, int documentHeight)
    {
        var scale = Math.Min(1.0, (double)MaxDecodeDimension / Math.Max(documentWidth, documentHeight));
        var wanted = layers.Where(NeedsBitmap).ToList();
        var total = wanted
            .Sum(layer => (long)Math.Max(1, layer.Width * scale) * Math.Max(1, layer.Height * scale));
        if (total > MaxDecodedPixels)
        {
            return null;
        }

        var bitmaps = new Dictionary<string, BitmapSource>(StringComparer.Ordinal);
        foreach (var layer in wanted.Where(layer => layer.Width > 0 && layer.Height > 0))
        {
            var path = resolvePath(layer.File);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"O arquivo da camada “{layer.Name}” não foi encontrado no projeto.");
            }

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bitmap.DecodePixelWidth = Math.Max(1, (int)Math.Round(layer.Width * scale));
                bitmap.EndInit();
                bitmap.Freeze();
                bitmaps[layer.File] = bitmap;
            }
            catch (Exception exception) when (exception is NotSupportedException or FormatException or IOException)
            {
                throw new InvalidDataException($"A camada “{layer.Name}” não pôde ser lida: {exception.Message}", exception);
            }
        }

        return bitmaps;
    }
}
