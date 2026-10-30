using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VTuberStudioAI.App.Images;

public sealed record PsdLayer(string Name, int Left, int Top, int Width, int Height, bool Visible, byte Opacity, byte[] Png);

public sealed record PsdDocument(int Width, int Height, IReadOnlyList<PsdLayer> Layers, byte[] CompositePng);

/// <summary>
/// Leitor mínimo e defensivo de PSD (versão 1, RGB 8 bits). Lê apenas pixels das camadas;
/// ignora modos de mesclagem, máscaras e efeitos, e nunca executa conteúdo embutido.
/// </summary>
public static class PsdReader
{
    public const int MaximumLayers = 100;
    public const long MaximumTotalLayerPixels = 2_000_000_000;

    public static PsdDocument Read(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        try
        {
            return ReadCore(bytes);
        }
        catch (Exception exception) when (
            exception is EndOfStreamException or OverflowException or InvalidDataException or
                IndexOutOfRangeException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            if (exception is InvalidDataException)
            {
                throw;
            }

            throw new InvalidDataException("O arquivo PSD está corrompido ou usa um recurso não suportado.", exception);
        }
    }

    private static PsdDocument ReadCore(byte[] bytes)
    {
        var reader = new BigEndianReader(bytes);
        if (reader.ReadAscii(4) != "8BPS")
        {
            throw new InvalidDataException("O conteúdo não possui uma assinatura PSD válida.");
        }

        var version = reader.ReadUInt16();
        if (version != 1)
        {
            throw new InvalidDataException("Somente PSD comum é suportado; arquivos grandes (PSB) não são aceitos.");
        }

        reader.Skip(6);
        reader.ReadUInt16(); // canais do composto, não usados
        var height = reader.ReadInt32();
        var width = reader.ReadInt32();
        var depth = reader.ReadUInt16();
        var mode = reader.ReadUInt16();
        ImageImportService.ValidateDimensionsPublic(width, height);
        if (depth != 8 || mode != 3)
        {
            throw new InvalidDataException("Somente PSD RGB de 8 bits por canal é suportado. Converta o arquivo no editor de origem.");
        }

        reader.Skip(reader.ReadLength());
        reader.Skip(reader.ReadLength());

        var layerSectionLength = reader.ReadLength();
        if (layerSectionLength == 0)
        {
            throw new InvalidDataException("O PSD não contém camadas. Exporte a arte como PNG ou salve o PSD com camadas.");
        }

        var layerSectionEnd = reader.Position + layerSectionLength;
        var layerInfoLength = reader.ReadLength();
        if (layerInfoLength == 0)
        {
            throw new InvalidDataException("O PSD não contém camadas. Exporte a arte como PNG ou salve o PSD com camadas.");
        }

        var layerInfoEnd = reader.Position + layerInfoLength;
        if (layerInfoEnd > layerSectionEnd || layerInfoEnd > bytes.Length)
        {
            throw new InvalidDataException("A seção de camadas do PSD é inválida.");
        }

        var layerCount = Math.Abs((int)reader.ReadInt16());
        if (layerCount == 0 || layerCount > MaximumLayers)
        {
            throw new InvalidDataException($"O PSD deve ter entre 1 e {MaximumLayers} camadas.");
        }

        var records = new List<LayerRecord>(layerCount);
        long totalPixels = 0;
        for (var index = 0; index < layerCount; index++)
        {
            var record = ReadRecord(reader, layerInfoEnd);
            totalPixels += (long)record.Width * record.Height;
            if (totalPixels > MaximumTotalLayerPixels)
            {
                throw new InvalidDataException("As camadas do PSD somam pixels demais para importar com segurança.");
            }

            records.Add(record);
        }

        var canvas = new byte[checked(width * height * 4)];
        var layers = new List<PsdLayer>();
        foreach (var record in records)
        {
            var pixels = ReadChannels(reader, record, layerInfoEnd);
            if (record.Width == 0 || record.Height == 0)
            {
                continue; // grupos e camadas vazias
            }

            if (record.IsDivider)
            {
                continue;
            }

            if (record.Visible)
            {
                Composite(canvas, width, height, record, pixels);
            }

            layers.Add(new PsdLayer(
                record.Name, record.Left, record.Top, record.Width, record.Height,
                record.Visible, record.Opacity, EncodePng(pixels, record.Width, record.Height)));
        }

        if (layers.Count == 0)
        {
            throw new InvalidDataException("O PSD não tem camadas com pixels.");
        }

        return new PsdDocument(width, height, layers, EncodePng(canvas, width, height));
    }

    private static LayerRecord ReadRecord(BigEndianReader reader, long limit)
    {
        var top = reader.ReadInt32();
        var left = reader.ReadInt32();
        var bottom = reader.ReadInt32();
        var right = reader.ReadInt32();
        var layerWidth = (long)right - left;
        var layerHeight = (long)bottom - top;
        if (layerWidth < 0 || layerHeight < 0 ||
            layerWidth > ImageImportService.MaximumDimension || layerHeight > ImageImportService.MaximumDimension ||
            layerWidth * layerHeight > ImageImportService.MaximumPixelCount)
        {
            throw new InvalidDataException("Uma camada do PSD tem dimensões fora do limite permitido.");
        }

        var channelCount = reader.ReadUInt16();
        if (channelCount is < 1 or > 6)
        {
            throw new InvalidDataException("Uma camada do PSD tem um número de canais inválido.");
        }

        var channels = new List<(short Id, long Length)>(channelCount);
        for (var index = 0; index < channelCount; index++)
        {
            var id = reader.ReadInt16();
            var length = reader.ReadUInt32();
            if (length > limit)
            {
                throw new InvalidDataException("Um canal do PSD declara tamanho inválido.");
            }

            channels.Add((id, length));
        }

        if (reader.ReadAscii(4) != "8BIM")
        {
            throw new InvalidDataException("Um registro de camada do PSD está corrompido.");
        }

        reader.Skip(4); // modo de mesclagem (tratado como normal)
        var opacity = reader.ReadByte();
        reader.ReadByte(); // recorte
        var flags = reader.ReadByte();
        reader.ReadByte();

        var extraLength = reader.ReadLength();
        var extraEnd = reader.Position + extraLength;
        if (extraEnd > limit)
        {
            throw new InvalidDataException("Os dados extras de uma camada do PSD são inválidos.");
        }

        reader.Skip(reader.ReadLength()); // máscara
        reader.Skip(reader.ReadLength()); // intervalos de mesclagem
        var nameLength = reader.ReadByte();
        var name = Encoding.Latin1.GetString(reader.ReadBytes(nameLength)).Trim();
        reader.Skip((4 - (nameLength + 1) % 4) % 4);

        var isDivider = false;
        while (reader.Position + 12 <= extraEnd)
        {
            var signature = reader.ReadAscii(4);
            var key = reader.ReadAscii(4);
            var blockLength = reader.ReadLength();
            var blockEnd = reader.Position + blockLength;
            if ((signature != "8BIM" && signature != "8B64") || blockEnd > extraEnd)
            {
                break;
            }

            if (key == "luni" && blockLength >= 4)
            {
                var characters = reader.ReadLength();
                if (characters > 0 && characters * 2L <= blockLength - 4)
                {
                    name = Encoding.BigEndianUnicode.GetString(reader.ReadBytes(characters * 2)).TrimEnd('\0').Trim();
                }
            }
            else if (key == "lsct" && blockLength >= 4)
            {
                isDivider = reader.ReadInt32() == 3;
            }

            reader.Position = blockEnd + (blockLength % 2);
        }

        reader.Position = extraEnd;
        name = SanitizeName(name);
        return new LayerRecord(name, left, top, (int)layerWidth, (int)layerHeight, (flags & 0x02) == 0, opacity, channels, isDivider);
    }

    private static byte[] ReadChannels(BigEndianReader reader, LayerRecord record, long limit)
    {
        var pixelCount = checked(record.Width * record.Height);
        var pixels = new byte[checked(pixelCount * 4)];
        var hasAlpha = false;
        var hasColor = new bool[3];
        foreach (var (id, length) in record.Channels)
        {
            var start = reader.Position;
            var end = start + length;
            if (end > limit || end > reader.Length)
            {
                throw new InvalidDataException("Os dados de pixels do PSD terminam antes do esperado.");
            }

            if (length >= 2 && pixelCount > 0 && id is 0 or 1 or 2 or -1)
            {
                var compression = reader.ReadUInt16();
                var plane = DecodePlane(reader, compression, record.Width, record.Height, end);
                var offset = id switch { 0 => 2, 1 => 1, 2 => 0, _ => 3 }; // BGRA
                for (var index = 0; index < pixelCount; index++)
                {
                    pixels[index * 4 + offset] = plane[index];
                }

                if (id == -1)
                {
                    hasAlpha = true;
                }
                else
                {
                    hasColor[id] = true;
                }
            }

            reader.Position = end;
        }

        if (pixelCount > 0 && !hasColor.All(present => present))
        {
            throw new InvalidDataException("Uma camada do PSD não tem canais RGB completos.");
        }

        if (!hasAlpha)
        {
            for (var index = 3; index < pixels.Length; index += 4)
            {
                pixels[index] = 255;
            }
        }

        return pixels;
    }

    private static byte[] DecodePlane(BigEndianReader reader, int compression, int width, int height, long end)
    {
        var plane = new byte[checked(width * height)];
        switch (compression)
        {
            case 0:
                if (reader.Position + plane.Length > end)
                {
                    throw new InvalidDataException("Dados de camada PSD incompletos.");
                }

                reader.ReadBytes(plane.Length).CopyTo(plane, 0);
                break;
            case 1:
                var rowSizes = new int[height];
                for (var row = 0; row < height; row++)
                {
                    rowSizes[row] = reader.ReadUInt16();
                }

                for (var row = 0; row < height; row++)
                {
                    if (reader.Position + rowSizes[row] > end)
                    {
                        throw new InvalidDataException("Dados RLE do PSD incompletos.");
                    }

                    UnpackBits(reader.ReadBytes(rowSizes[row]), plane, row * width, width);
                }

                break;
            case 2:
            case 3:
                var compressed = reader.ReadBytes(checked((int)(end - reader.Position)));
                using (var input = new MemoryStream(compressed))
                using (var zlib = new ZLibStream(input, CompressionMode.Decompress))
                {
                    zlib.ReadExactly(plane);
                }

                if (compression == 3)
                {
                    for (var row = 0; row < height; row++)
                    {
                        for (var column = 1; column < width; column++)
                        {
                            plane[row * width + column] += plane[row * width + column - 1];
                        }
                    }
                }

                break;
            default:
                throw new InvalidDataException("O PSD usa uma compressão de camada não suportada.");
        }

        return plane;
    }

    private static void UnpackBits(byte[] source, byte[] target, int targetOffset, int width)
    {
        var read = 0;
        var written = 0;
        while (written < width)
        {
            if (read >= source.Length)
            {
                throw new InvalidDataException("Dados RLE do PSD incompletos.");
            }

            var header = (sbyte)source[read++];
            if (header >= 0)
            {
                var count = header + 1;
                if (read + count > source.Length || written + count > width)
                {
                    throw new InvalidDataException("Dados RLE do PSD inválidos.");
                }

                Array.Copy(source, read, target, targetOffset + written, count);
                read += count;
                written += count;
            }
            else if (header != -128)
            {
                var count = 1 - header;
                if (read >= source.Length || written + count > width)
                {
                    throw new InvalidDataException("Dados RLE do PSD inválidos.");
                }

                Array.Fill(target, source[read++], targetOffset + written, count);
                written += count;
            }
        }
    }

    private static void Composite(byte[] canvas, int canvasWidth, int canvasHeight, LayerRecord record, byte[] pixels)
    {
        for (var y = 0; y < record.Height; y++)
        {
            var targetY = record.Top + y;
            if (targetY < 0 || targetY >= canvasHeight)
            {
                continue;
            }

            for (var x = 0; x < record.Width; x++)
            {
                var targetX = record.Left + x;
                if (targetX < 0 || targetX >= canvasWidth)
                {
                    continue;
                }

                var source = (y * record.Width + x) * 4;
                var alpha = pixels[source + 3] * record.Opacity / 255.0 / 255.0;
                if (alpha <= 0)
                {
                    continue;
                }

                var target = (targetY * canvasWidth + targetX) * 4;
                var backdrop = canvas[target + 3] / 255.0;
                var outAlpha = alpha + backdrop * (1 - alpha);
                for (var channel = 0; channel < 3; channel++)
                {
                    var value = (pixels[source + channel] * alpha + canvas[target + channel] * backdrop * (1 - alpha)) / outAlpha;
                    canvas[target + channel] = (byte)Math.Clamp(Math.Round(value), 0, 255);
                }

                canvas[target + 3] = (byte)Math.Clamp(Math.Round(outAlpha * 255), 0, 255);
            }
        }
    }

    private static byte[] EncodePng(byte[] bgra, int width, int height)
    {
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bgra, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static string SanitizeName(string name)
    {
        var clean = new string(name.Where(character => !char.IsControl(character)).ToArray()).Trim();
        if (clean.Length == 0)
        {
            return "Camada";
        }

        return clean.Length > 80 ? clean[..80] : clean;
    }

    private sealed record LayerRecord(
        string Name, int Left, int Top, int Width, int Height, bool Visible, byte Opacity,
        List<(short Id, long Length)> Channels, bool IsDivider);

    private sealed class BigEndianReader(byte[] data)
    {
        public long Position { get; set; }

        public long Length => data.Length;

        private void Require(long count)
        {
            if (count < 0 || Position + count > data.Length)
            {
                throw new EndOfStreamException("O arquivo PSD terminou antes do esperado.");
            }
        }

        public void Skip(long count)
        {
            Require(count);
            Position += count;
        }

        public byte ReadByte()
        {
            Require(1);
            return data[Position++];
        }

        public byte[] ReadBytes(int count)
        {
            Require(count);
            var result = new byte[count];
            Array.Copy(data, Position, result, 0, count);
            Position += count;
            return result;
        }

        public ushort ReadUInt16() => (ushort)(ReadByte() << 8 | ReadByte());

        public short ReadInt16() => (short)ReadUInt16();

        public uint ReadUInt32() => (uint)ReadUInt16() << 16 | ReadUInt16();

        public int ReadInt32() => unchecked((int)ReadUInt32());

        public int ReadLength()
        {
            var value = ReadUInt32();
            if (value > data.Length)
            {
                throw new InvalidDataException("O PSD declara uma seção maior que o arquivo.");
            }

            return (int)value;
        }

        public string ReadAscii(int count) => Encoding.ASCII.GetString(ReadBytes(count));
    }
}
