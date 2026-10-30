using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VTuberStudioAI.App.Images;
using VTuberStudioAI.App.Preview;
using Xunit;

namespace VTuberStudioAI.App.Tests;

public sealed class LayeredPreviewRenderTests
{
    [Fact]
    public void PupilIsClippedByTheEyeWhiteAndNothingLeaksOutside()
    {
        var (inside, outside, empty) = RunOnSta(() =>
        {
            var layers = new List<PsdLayerInfo>
            {
                new("eyewhite.L", 10, 5, 20, 10, true, "a"),
                new("pupil.L", 20, 5, 20, 10, true, "b")
            };
            var plan = LayerRigger.Plan(layers);
            var scene = new LayerScene(40, 20,
            [
                new SceneLayer(layers[0], plan[0], Solid(20, 10, 255, 255, 255)),
                new SceneLayer(layers[1], plan[1], Solid(20, 10, 0, 0, 0))
            ]);
            var preview = new InteractivePreview
            {
                Source = Solid(40, 20, 10, 10, 10),
                Scene = scene,
                Eyes = scene.EyeRegions,
                Width = 400,
                Height = 200
            };
            preview.Measure(new Size(400, 200));
            preview.Arrange(new Rect(0, 0, 400, 200));
            var target = new RenderTargetBitmap(400, 200, 96, 96, PixelFormats.Pbgra32);
            target.Render(preview);

            // Escala 8,8 com origem (24, 12): x do documento 27 está dentro do olho, 35 fora, 15 só tem o branco.
            return (Pixel(target, 262, 100), Pixel(target, 332, 100), Pixel(target, 156, 100));
        });

        Assert.Equal((255, 0), (inside.A, inside.B));
        Assert.Equal(0, outside.A);
        Assert.Equal((255, 255), (empty.A, empty.B));
    }

    private static (byte B, byte G, byte R, byte A) Pixel(BitmapSource bitmap, int x, int y)
    {
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return (pixel[0], pixel[1], pixel[2], pixel[3]);
    }

    private static BitmapSource Solid(int width, int height, byte r, byte g, byte b)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
            pixels[i + 3] = 255;
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static T RunOnSta<T>(Func<T> action)
    {
        T? result = default;
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
        return result!;
    }
}
