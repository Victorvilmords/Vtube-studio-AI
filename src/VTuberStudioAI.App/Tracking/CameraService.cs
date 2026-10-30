using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Enumeration;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;

namespace VTuberStudioAI.App.Tracking;

public sealed record CameraDevice(string Id, string Name);

public sealed class CameraException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Fonte de quadros de câmera. Os quadros chegam em thread de fundo.</summary>
public interface ICameraSource : IDisposable
{
    event Action<BgraFrame>? FrameArrived;

    Task<IReadOnlyList<CameraDevice>> ListAsync();

    /// <exception cref="CameraException">Câmera negada, ocupada ou indisponível.</exception>
    Task StartAsync(string deviceId);

    Task StopAsync();
}

/// <summary>Câmera via Windows (MediaCapture), somente leitura e compartilhada com outros aplicativos.</summary>
public sealed class CameraService : ICameraSource
{
    private MediaCapture? _capture;
    private MediaFrameReader? _reader;

    public event Action<BgraFrame>? FrameArrived;

    public async Task<IReadOnlyList<CameraDevice>> ListAsync()
    {
        try
        {
            var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            return [.. devices.Select(device => new CameraDevice(device.Id, device.Name))];
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new CameraException("Não foi possível listar as câmeras do computador.", ex);
        }
    }

    public async Task StartAsync(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            throw new CameraException("Escolha uma câmera para começar.");
        }

        await StopAsync();
        try
        {
            var capture = new MediaCapture();
            _capture = capture;
            await capture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                VideoDeviceId = deviceId,
                StreamingCaptureMode = StreamingCaptureMode.Video,
                SharingMode = MediaCaptureSharingMode.SharedReadOnly,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu
            });

            var source = capture.FrameSources.Values.FirstOrDefault(s => s.Info.SourceKind == MediaFrameSourceKind.Color)
                ?? throw new CameraException("Esta câmera não oferece imagem colorida.");
            var reader = await capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8);
            reader.FrameArrived += OnFrameArrived;
            _reader = reader;
            var status = await reader.StartAsync();
            if (status != MediaFrameReaderStartStatus.Success)
            {
                throw new CameraException($"A câmera não iniciou ({status}). Ela pode estar em uso por outro aplicativo.");
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            await StopAsync();
            throw new CameraException(
                "O Windows bloqueou a câmera. Abra Configurações > Privacidade e segurança > Câmera e permita " +
                "o acesso para aplicativos da área de trabalho.", ex);
        }
        catch (CameraException)
        {
            await StopAsync();
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            await StopAsync();
            throw new CameraException("Não foi possível abrir a câmera. Verifique se ela está conectada e livre.", ex);
        }
    }

    public async Task StopAsync()
    {
        var reader = _reader;
        var capture = _capture;
        _reader = null;
        _capture = null;
        if (reader is not null)
        {
            reader.FrameArrived -= OnFrameArrived;
            try
            {
                await reader.StopAsync();
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
                // A câmera pode já ter sido desconectada; o objetivo aqui é só liberar o dispositivo.
            }

            reader.Dispose();
        }

        capture?.Dispose();
    }

    public void Dispose() => Task.Run(StopAsync).Wait(TimeSpan.FromSeconds(2));

    private void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        using var reference = sender.TryAcquireLatestFrame();
        var bitmap = reference?.VideoMediaFrame?.SoftwareBitmap;
        if (bitmap is null)
        {
            return;
        }

        var converted = bitmap.BitmapPixelFormat == BitmapPixelFormat.Bgra8
            ? bitmap
            : SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        try
        {
            var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
            converted.CopyToBuffer(pixels.AsBuffer());
            FrameArrived?.Invoke(new BgraFrame(pixels, converted.PixelWidth, converted.PixelHeight));
        }
        finally
        {
            if (!ReferenceEquals(converted, bitmap))
            {
                converted.Dispose();
            }
        }
    }
}
