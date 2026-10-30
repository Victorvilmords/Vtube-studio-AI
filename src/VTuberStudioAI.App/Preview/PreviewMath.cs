using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VTuberStudioAI.App.Preview;

public static class PreviewMath
{
    /// <summary>Converte a posição do mouse em um deslocamento de -1 a 1 em relação ao centro da área.</summary>
    public static Vector GazeFromMouse(Point mouse, Size area)
    {
        if (area.Width <= 0 || area.Height <= 0)
        {
            return new Vector(0, 0);
        }

        var x = Math.Clamp((mouse.X / area.Width - 0.5) * 2, -1, 1);
        var y = Math.Clamp((mouse.Y / area.Height - 0.5) * 2, -1, 1);
        return new Vector(x, y);
    }

    public static bool HitsEye(EyeRegion eye, Point normalized) =>
        normalized.X >= eye.X && normalized.X <= eye.X + eye.Width &&
        normalized.Y >= eye.Y && normalized.Y <= eye.Y + eye.Height;

    /// <summary>Fechamento da pálpebra de 0 (aberto) a 1 (fechado) ao longo do piscar.</summary>
    public static double BlinkAmount(double progress) =>
        progress is <= 0 or >= 1 ? 0 : Math.Sin(Math.PI * progress);

    /// <summary>Cor média da faixa logo acima do olho, usada para a pálpebra do preview.</summary>
    public static Color SampleEyelidColor(BitmapSource source, EyeRegion eye)
    {
        BitmapSource converted = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        var left = Math.Clamp((int)(eye.X * width), 0, width - 1);
        var right = Math.Clamp((int)((eye.X + eye.Width) * width), left + 1, width);
        var bottom = Math.Clamp((int)(eye.Y * height), 1, height);
        var top = Math.Clamp(bottom - Math.Max(1, (int)(eye.Height * height * 0.4)), 0, bottom - 1);

        var rect = new Int32Rect(left, top, right - left, bottom - top);
        var stride = rect.Width * 4;
        var pixels = new byte[stride * rect.Height];
        converted.CopyPixels(rect, pixels, stride, 0);

        double b = 0, g = 0, r = 0, weight = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var alpha = pixels[i + 3] / 255.0;
            b += pixels[i] * alpha;
            g += pixels[i + 1] * alpha;
            r += pixels[i + 2] * alpha;
            weight += alpha;
        }

        return weight <= 0
            ? Color.FromRgb(60, 60, 70)
            : Color.FromRgb((byte)(r / weight), (byte)(g / weight), (byte)(b / weight));
    }
}
