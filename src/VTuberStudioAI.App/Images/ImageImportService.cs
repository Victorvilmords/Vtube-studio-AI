using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Media.Imaging;
using VTuberStudioAI.App.Preview;
using VTuberStudioAI.App.Projects;

namespace VTuberStudioAI.App.Images;

public sealed record ValidatedImage(string FileName, string Format, int Width, int Height, byte[] Bytes, PsdDocument? Psd = null);

public interface IImageImportService
{
    ValidatedImage Inspect(string path);

    string Import(ProjectDocument project, ValidatedImage image);

    ValidatedImage InspectResource(ProjectDocument project, string relativeReference);

    void RemoveReference(ProjectDocument project, string relativeReference);
}

public sealed class ImageImportService(IProjectRepository projectRepository) : IImageImportService
{
    public const long MaximumFileSizeBytes = 25 * 1024 * 1024;
    public const int MaximumDimension = 12_000;
    public const long MaximumPixelCount = (long)MaximumDimension * MaximumDimension;

    public const long MaximumPsdFileSizeBytes = 500L * 1024 * 1024;

    // Limite para recursos já guardados no projeto, como a composição gerada de um PSD grande.
    private const long MaximumStoredFileSizeBytes = 400L * 1024 * 1024;

    private readonly IProjectRepository _projectRepository =
        projectRepository ?? throw new ArgumentNullException(nameof(projectRepository));

    internal static void ValidateDimensionsPublic(int width, int height) => ValidateDimensions(width, height);

    private static ValidatedImage InspectPsd(string path)
    {
        byte[] bytes;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (stream.Length <= 0 || stream.Length > MaximumPsdFileSizeBytes)
            {
                throw new InvalidDataException($"O PSD deve ter entre 1 byte e {MaximumPsdFileSizeBytes / (1024 * 1024)} MiB.");
            }

            bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
        }

        var psd = PsdReader.Read(bytes);
        return new ValidatedImage(
            Path.ChangeExtension(Path.GetFileName(path), ".png"),
            "PNG",
            psd.Width,
            psd.Height,
            psd.CompositePng,
            psd);
    }

    public ValidatedImage Inspect(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (Path.GetExtension(path).Equals(".psd", StringComparison.OrdinalIgnoreCase))
        {
            return InspectPsd(path);
        }

        return InspectFile(path, MaximumFileSizeBytes);
    }

    private static ValidatedImage InspectFile(string path, long maximumBytes)
    {
        var extension = Path.GetExtension(path);
        if (!extension.Equals(".png", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Formato não aceito. Selecione PNG, JPG/JPEG ou PSD com camadas. WebP não é suportado nesta versão.");
        }

        byte[] bytes;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (stream.Length <= 0 || stream.Length > maximumBytes)
            {
                throw new InvalidDataException($"A imagem deve ter entre 1 byte e {maximumBytes / (1024 * 1024)} MiB.");
            }

            bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
        }

        var (headerFormat, headerWidth, headerHeight) = ReadHeader(bytes);
        if (headerFormat == "PNG" && !extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
            headerFormat == "JPEG" &&
            !extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("A extensão do arquivo não corresponde ao formato real da imagem.");
        }

        ValidateDimensions(headerWidth, headerHeight);
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0 ||
                (headerFormat == "PNG" && decoder is not PngBitmapDecoder) ||
                (headerFormat == "JPEG" && decoder is not JpegBitmapDecoder))
            {
                throw new InvalidDataException("O conteúdo não é uma imagem PNG ou JPEG válida.");
            }

            var frame = decoder.Frames[0];
            ValidateDimensions(frame.PixelWidth, frame.PixelHeight);
            if (frame.PixelWidth != headerWidth || frame.PixelHeight != headerHeight)
            {
                throw new InvalidDataException("As dimensões do cabeçalho não correspondem à imagem decodificada.");
            }

            return new ValidatedImage(
                Path.GetFileName(path),
                headerFormat,
                frame.PixelWidth,
                frame.PixelHeight,
                bytes);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is FileFormatException or
                NotSupportedException or
                InvalidOperationException or
                COMException or
                IOException)
        {
            throw new InvalidDataException("O arquivo está corrompido ou não pode ser decodificado como PNG ou JPEG.", exception);
        }
    }

    public string Import(ProjectDocument project, ValidatedImage image)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(image);

        var currentProject = _projectRepository.Load(project.Id);
        var extension = image.Format switch
        {
            "PNG" => ".png",
            "JPEG" => ".jpg",
            _ => throw new InvalidDataException("Formato decodificado não suportado.")
        };
        var storedFileName = $"{Guid.NewGuid():N}-{SanitizeFileName(image.FileName, extension)}";
        var relativeReference = $"resources/{storedFileName}";
        var targetPath = _projectRepository.GetResourcePath(currentProject.Id, relativeReference);
        var targetDirectory = Path.GetDirectoryName(targetPath)!;
        Directory.CreateDirectory(targetDirectory);
        var temporaryPath = Path.Combine(targetDirectory, $".{Guid.NewGuid():N}.tmp{extension}");
        var committed = false;
        var writtenLayerFiles = new List<string>();

        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       81920,
                       FileOptions.WriteThrough))
            {
                stream.Write(image.Bytes);
                stream.Flush(flushToDisk: true);
            }

            var verifiedCopy = Inspect(temporaryPath, image.Format);
            if (verifiedCopy.Width != image.Width || verifiedCopy.Height != image.Height)
            {
                throw new InvalidDataException("A imagem copiada não corresponde à prévia validada.");
            }

            File.Move(temporaryPath, targetPath);
            committed = true;
            var configurations = new Dictionary<string, JsonElement>(currentProject.Configurations, StringComparer.Ordinal);
            if (image.Psd is not null)
            {
                var layerDirectory = $"resources/layers/{Guid.NewGuid():N}";
                var layers = new List<PsdLayerInfo>();
                for (var index = 0; index < image.Psd.Layers.Count; index++)
                {
                    var layer = image.Psd.Layers[index];
                    var reference = $"{layerDirectory}/{index + 1:D3}.png";
                    var layerPath = _projectRepository.GetResourcePath(currentProject.Id, reference);
                    Directory.CreateDirectory(Path.GetDirectoryName(layerPath)!);
                    writtenLayerFiles.Add(layerPath);
                    File.WriteAllBytes(layerPath, layer.Png);
                    layers.Add(new PsdLayerInfo(layer.Name, layer.Left, layer.Top, layer.Width, layer.Height, layer.Visible, reference, layer.Opacity));
                }

                configurations[PsdLayerStore.ConfigurationKey] = JsonSerializer.SerializeToElement(
                    new PsdLayerSet(relativeReference, image.FileName, layers));
                var eyes = PsdLayerStore.DetectEyes(image.Psd);
                if (eyes.Count > 0)
                {
                    configurations[EyePreviewService.ConfigurationKey] =
                        JsonSerializer.SerializeToElement(new EyeLayout(relativeReference, eyes));
                }
            }

            _projectRepository.Save(currentProject with
            {
                ResourceReferences = [.. currentProject.ResourceReferences, relativeReference],
                Configurations = configurations
            });
            return relativeReference;
        }
        catch
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            if (committed && File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            foreach (var layerFile in writtenLayerFiles.Where(File.Exists))
            {
                File.Delete(layerFile);
            }

            throw;
        }
    }

    public void RemoveReference(ProjectDocument project, string relativeReference)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeReference);
        var currentProject = _projectRepository.Load(project.Id);
        if (!currentProject.ResourceReferences.Contains(relativeReference, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A referência de imagem não pertence a este projeto.");
        }

        _projectRepository.Save(currentProject with
        {
            ResourceReferences = currentProject.ResourceReferences
                .Where(reference => !reference.Equals(relativeReference, StringComparison.OrdinalIgnoreCase))
                .ToList()
        });
    }

    public ValidatedImage InspectResource(ProjectDocument project, string relativeReference)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeReference);
        var currentProject = _projectRepository.Load(project.Id);
        if (!currentProject.ResourceReferences.Contains(relativeReference, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A referência de imagem não pertence a este projeto.");
        }

        var image = InspectFile(_projectRepository.GetResourcePath(project.Id, relativeReference), MaximumStoredFileSizeBytes);
        var separator = Path.GetFileName(relativeReference).IndexOf('-', StringComparison.Ordinal);
        return separator >= 0
            ? image with { FileName = Path.GetFileName(relativeReference)[(separator + 1)..] }
            : image;
    }

    private ValidatedImage Inspect(string path, string expectedFormat)
    {
        var image = InspectFile(path, MaximumStoredFileSizeBytes);
        if (!image.Format.Equals(expectedFormat, StringComparison.Ordinal))
        {
            throw new InvalidDataException("O formato da imagem mudou depois da seleção.");
        }

        return image;
    }

    private static void ValidateDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0 ||
            width > MaximumDimension || height > MaximumDimension ||
            (long)width * height > MaximumPixelCount)
        {
            throw new InvalidDataException(
                $"Dimensões não permitidas. O limite é {MaximumDimension} × {MaximumDimension} px.");
        }

    }

    private static string SanitizeFileName(string fileName, string extension)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var safeName = new string(name.Select(character =>
                char.IsControl(character) || invalidCharacters.Contains(character) ? '_' : character)
            .ToArray()).Trim(' ', '.');
        if (safeName.Length == 0)
        {
            safeName = "imagem";
        }

        if (safeName.Length > 100)
        {
            safeName = safeName[..100];
        }

        return $"{safeName}{extension}";
    }

    private static (string Format, int Width, int Height) ReadHeader(byte[] bytes)
    {
        ReadOnlySpan<byte> data = bytes;
        ReadOnlySpan<byte> pngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (data.Length >= 24 && data[..8].SequenceEqual(pngSignature) &&
            data.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            var rawWidth = ReadBigEndianUInt32(data.Slice(16, 4));
            var rawHeight = ReadBigEndianUInt32(data.Slice(20, 4));
            if (rawWidth > int.MaxValue || rawHeight > int.MaxValue)
            {
                throw new InvalidDataException("As dimensões da imagem estão fora do limite permitido.");
            }

            var width = (int)rawWidth;
            var height = (int)rawHeight;
            return ("PNG", width, height);
        }

        if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8)
        {
            var (width, height) = ReadJpegDimensions(data);
            return ("JPEG", width, height);
        }

        throw new InvalidDataException("O conteúdo não possui uma assinatura PNG ou JPEG válida.");
    }

    private static (int Width, int Height) ReadJpegDimensions(ReadOnlySpan<byte> data)
    {
        var offset = 2;
        while (offset < data.Length)
        {
            while (offset < data.Length && data[offset] != 0xFF)
            {
                offset++;
            }

            while (offset < data.Length && data[offset] == 0xFF)
            {
                offset++;
            }

            if (offset >= data.Length)
            {
                break;
            }

            var marker = data[offset++];
            if (marker is 0xD8 or 0xD9 or 0x01 || marker is >= 0xD0 and <= 0xD7)
            {
                continue;
            }

            if (offset + 2 > data.Length)
            {
                break;
            }

            var segmentLength = data[offset] << 8 | data[offset + 1];
            if (segmentLength < 2 || offset + segmentLength > data.Length)
            {
                break;
            }

            if (marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF)
            {
                if (segmentLength < 7)
                {
                    break;
                }

                var height = data[offset + 3] << 8 | data[offset + 4];
                var width = data[offset + 5] << 8 | data[offset + 6];
                return (width, height);
            }

            offset += segmentLength;
        }

        throw new InvalidDataException("Não foi possível ler as dimensões do arquivo JPEG.");
    }

    private static uint ReadBigEndianUInt32(ReadOnlySpan<byte> bytes) =>
        (uint)bytes[0] << 24 | (uint)bytes[1] << 16 | (uint)bytes[2] << 8 | bytes[3];
}
