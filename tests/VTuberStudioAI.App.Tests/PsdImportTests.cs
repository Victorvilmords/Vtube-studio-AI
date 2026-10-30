using System.Runtime.ExceptionServices;
using System.Text;
using System.Windows.Media.Imaging;
using VTuberStudioAI.App.Images;
using VTuberStudioAI.App.Preview;
using VTuberStudioAI.App.Projects;
using VTuberStudioAI.App.Tracking;
using VTuberStudioAI.App.ViewModels;
using Xunit;

namespace VTuberStudioAI.App.Tests;

public sealed class PsdImportTests
{
    [Fact]
    public void PlayTurnsTheCameraOnAndKeepsItWhileThePlayWindowIsOpen()
    {
        using var directory = new TemporaryFolder();
        var repository = new ProjectRepository(directory.Path);
        var project = repository.Create("PlayCamera");
        var psdPath = directory.Write("cam.psd", BuildPsd(4, 4, SampleLayers));

        RunOnSta(() =>
        {
            var camera = new PlayFakeCamera();
            var lab = new MotionLabViewModel(
                repository, camera, _ => new PlayFakeTracker(), action => action(), (_, _) => { });
            var viewModel = new MainWindowViewModel(
                repository, _ => false, (_, _) => { }, new ImageImportService(repository), () => psdPath,
                motionLab: lab, openPlayWindow: () => { });
            viewModel.ImportTargetProject = project;
            viewModel.ChooseImageFileCommand.Execute(null);
            viewModel.ConfirmImageImportCommand.Execute(null);

            viewModel.StartPlayCommand.Execute(null);
            for (var i = 0; i < 100 && !lab.IsRunning; i++)
            {
                Thread.Sleep(50);
            }

            Assert.True(viewModel.IsPlaying);
            Assert.True(lab.IsRunning);
            Assert.Equal("cam-1", camera.StartedWith);

            viewModel.NavigateCommand.Execute("library");
            Assert.False(camera.Stopped);

            viewModel.EndPlay();
            for (var i = 0; i < 100 && !camera.Stopped; i++)
            {
                Thread.Sleep(50);
            }

            Assert.False(viewModel.IsPlaying);
            Assert.True(camera.Stopped);
        });
    }

    private sealed class PlayFakeTracker : IFaceTracker
    {
        public FaceMeasurements? Track(BgraFrame frame) => null;

        public void Reset()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class PlayFakeCamera : ICameraSource
    {
        public event Action<BgraFrame>? FrameArrived;

        public string? StartedWith { get; private set; }

        public bool Stopped { get; private set; }

        public Task<IReadOnlyList<CameraDevice>> ListAsync() =>
            Task.FromResult<IReadOnlyList<CameraDevice>>([new CameraDevice("cam-1", "Câmera de teste")]);

        public Task StartAsync(string deviceId)
        {
            StartedWith = deviceId;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            Stopped = true;
            return Task.CompletedTask;
        }

        public void Dispose() => FrameArrived = null;
    }

    private static readonly SyntheticLayer[] SampleLayers =
    [
        new("Fundo", 0, 0, 4, 4, (200, 100, 50), Visible: true, Rle: false),
        new("Olho esquerdo", 1, 1, 2, 1, (10, 20, 30), Visible: true, Rle: true),
        new("Rascunho", 0, 0, 4, 4, (255, 0, 0), Visible: false, Rle: false),
        new("Olho fechado", 0, 2, 2, 1, (5, 5, 5), Visible: true, Rle: false)
    ];

    [Fact]
    public void ReaderComposesVisibleLayersAndKeepsEachLayerAsPng()
    {
        var document = RunOnSta(() => PsdReader.Read(BuildPsd(4, 4, SampleLayers)));

        Assert.Equal(4, document.Width);
        Assert.Equal(4, document.Layers.Count);
        Assert.Contains(document.Layers, layer => layer.Name == "Rascunho" && !layer.Visible);
        var (b, g, r, a) = RunOnSta(() => PixelAt(document.CompositePng, 1, 1));
        Assert.Equal((10, 20, 30, 255), (r, g, b, a));
        var background = RunOnSta(() => PixelAt(document.CompositePng, 3, 3));
        Assert.Equal((200, 100, 50), (background.R, background.G, background.B));
    }

    [Fact]
    public void ImportedPsdPersistsLayersAndEyesAndSkipsClosedEyeLayers()
    {
        using var directory = new TemporaryFolder();
        var repository = new ProjectRepository(directory.Path);
        var project = repository.Create("PSD");
        var psdPath = directory.Write("arte.psd", BuildPsd(4, 4, SampleLayers));
        var service = new ImageImportService(repository);

        string reference = RunOnSta(() => service.Import(project, service.Inspect(psdPath)));

        var reopened = new ProjectRepository(directory.Path).Load(project.Id);
        Assert.Equal([reference], reopened.ResourceReferences);
        var layers = PsdLayerStore.Read(reopened, reference);
        Assert.Equal(4, layers.Count);
        Assert.All(layers, layer => Assert.True(File.Exists(repository.GetResourcePath(project.Id, layer.File))));
        var eyes = EyePreviewService.ReadEyes(reopened, reference);
        var eye = Assert.Single(eyes);
        Assert.Equal(0.25, eye.X, 5);
        Assert.Equal(0.25, eye.Y, 5);
        Assert.True(File.Exists(psdPath));
    }

    [Fact]
    public void ViewModelShowsPsdLayersAfterImport()
    {
        using var directory = new TemporaryFolder();
        var repository = new ProjectRepository(directory.Path);
        var project = repository.Create("PSD VM");
        var psdPath = directory.Write("arte.psd", BuildPsd(4, 4, SampleLayers));

        RunOnSta(() =>
        {
            var viewModel = new MainWindowViewModel(
                repository, _ => false, (_, _) => { }, new ImageImportService(repository), () => psdPath);
            viewModel.ImportTargetProject = project;
            viewModel.ChooseImageFileCommand.Execute(null);
            viewModel.ConfirmImageImportCommand.Execute(null);

            Assert.True(viewModel.HasPsdLayers);
            Assert.Contains("Rascunho (oculta)", viewModel.PsdLayerLabels);
            Assert.True(viewModel.HasEyeRegions);
        });
    }

    [Fact]
    public void PlayStartsOnlyWithAnImageAndRemembersTheScenery()
    {
        using var directory = new TemporaryFolder();
        var repository = new ProjectRepository(directory.Path);
        var project = repository.Create("Play");
        var psdPath = directory.Write("play.psd", BuildPsd(4, 4, SampleLayers));
        var opened = 0;

        RunOnSta(() =>
        {
            var viewModel = new MainWindowViewModel(
                repository, _ => false, (_, _) => { }, new ImageImportService(repository), () => psdPath,
                openPlayWindow: () => opened++);
            viewModel.ImportTargetProject = project;
            Assert.False(viewModel.CanStartPlay);
            viewModel.StartPlayCommand.Execute(null);
            Assert.Equal(0, opened);
            Assert.NotEmpty(viewModel.ErrorMessage);

            viewModel.ChooseImageFileCommand.Execute(null);
            viewModel.ConfirmImageImportCommand.Execute(null);
            Assert.True(viewModel.CanStartPlay);

            viewModel.SceneryColorText = "verde";
            Assert.NotEmpty(viewModel.SceneryColorError);
            Assert.Equal("#00B140", viewModel.Scenery.Color);
            viewModel.StartPlayCommand.Execute(null);
            Assert.Equal(0, opened);

            viewModel.SceneryColorText = "#0047bb";
            Assert.Equal(string.Empty, viewModel.SceneryColorError);
            viewModel.SetSceneryModeCommand.Execute("transparent");
            Assert.True(viewModel.SceneryIsTransparent);
            viewModel.SetSceneryModeCommand.Execute("solid");
            viewModel.StartPlayCommand.Execute(null);
            Assert.Equal(1, opened);
        });

        var saved = SceneryStore.Read(new ProjectRepository(directory.Path).Load(project.Id));
        Assert.Equal(new SceneryOptions(SceneryMode.Solid, "#0047BB"), saved);
    }

    [Fact]
    public void SceneryRejectsInvalidColorsAndFallsBackToDefault()
    {
        using var directory = new TemporaryFolder();
        var repository = new ProjectRepository(directory.Path);
        var project = repository.Create("Cenario");

        Assert.False(SceneryOptions.TryParseColor("#12345", out _));
        Assert.False(SceneryOptions.TryParseColor("red", out _));
        Assert.False(SceneryOptions.TryParseColor("#GG0000", out _));
        Assert.True(SceneryOptions.TryParseColor("#ffaa00", out var color) && color.R == 255 && color.G == 170);
        Assert.Throws<ArgumentException>(() =>
            SceneryStore.Save(repository, project.Id, new SceneryOptions(SceneryMode.Solid, "azul")));
        Assert.Equal(SceneryOptions.Default, SceneryStore.Read(repository.Load(project.Id)));

        SceneryStore.Save(repository, project.Id, new SceneryOptions(SceneryMode.Transparent, "#000000"));
        Assert.Equal(SceneryMode.Transparent, SceneryStore.Read(repository.Load(project.Id)).Mode);
    }

    [Fact]
    public void NamedLayersBuildARigAndRenamingUpdatesItLiveAndPersists()
    {
        using var directory = new TemporaryFolder();
        var repository = new ProjectRepository(directory.Path);
        var project = repository.Create("Rig");
        SyntheticLayer[] layers =
        [
            new("body", 0, 0, 8, 8, (90, 90, 90), Visible: true, Rle: false),
            new("eyewhite.L", 1, 1, 4, 2, (255, 255, 255), Visible: true, Rle: false),
            new("pupil.L", 2, 1, 2, 2, (0, 0, 0), Visible: true, Rle: false),
            new("eyehighlight.L", 2, 1, 1, 1, (255, 255, 255), Visible: true, Rle: false),
            new("F.ear.L", 5, 0, 2, 2, (120, 60, 20), Visible: true, Rle: false)
        ];
        var psdPath = directory.Write("rig.psd", BuildPsd(8, 8, layers));

        RunOnSta(() =>
        {
            var viewModel = new MainWindowViewModel(
                repository, _ => false, (_, _) => { }, new ImageImportService(repository), () => psdPath);
            viewModel.ImportTargetProject = project;
            viewModel.ChooseImageFileCommand.Execute(null);
            viewModel.ConfirmImageImportCommand.Execute(null);

            Assert.NotNull(viewModel.PreviewScene);
            Assert.True(viewModel.HasRiggedEyes);
            Assert.False(viewModel.CanMarkEyes);
            Assert.Single(viewModel.PreviewEyes);
            Assert.Equal(1, viewModel.PreviewScene!.Layers[2].Rig.MaskIndex);
            Assert.Contains("furry", viewModel.RigSummary);
            Assert.Equal(5, viewModel.LayerItems.Count);

            viewModel.LayerItems[2].Name = "Pupila solta";

            Assert.Null(viewModel.PreviewScene!.Layers[2].Rig.MaskIndex);
            Assert.Contains("Sem papel", viewModel.LayerItems[2].RoleDescription);
            Assert.Equal(string.Empty, viewModel.ErrorMessage);

            viewModel.LayerItems[2].Name = " ";

            Assert.Contains("vazio", viewModel.ErrorMessage);
            Assert.Null(viewModel.PreviewScene!.Layers[2].Rig.MaskIndex);
        });

        var reopened = new ProjectRepository(directory.Path).Load(project.Id);
        var reference = Assert.Single(reopened.ResourceReferences);
        Assert.Equal("Pupila solta", PsdLayerStore.Read(reopened, reference)[2].Name);
    }

    [Fact]
    public void PsdWithoutRecognizedNamesKeepsTheCompositePreview()
    {
        using var directory = new TemporaryFolder();
        var repository = new ProjectRepository(directory.Path);
        var project = repository.Create("Sem nomes");
        var psdPath = directory.Write("arte.psd", BuildPsd(4, 4,
        [
            new("Layer 1", 0, 0, 4, 4, (200, 100, 50), Visible: true, Rle: false),
            new("Layer 2", 1, 1, 2, 1, (10, 20, 30), Visible: true, Rle: false)
        ]));

        RunOnSta(() =>
        {
            var viewModel = new MainWindowViewModel(
                repository, _ => false, (_, _) => { }, new ImageImportService(repository), () => psdPath);
            viewModel.ImportTargetProject = project;
            viewModel.ChooseImageFileCommand.Execute(null);
            viewModel.ConfirmImageImportCommand.Execute(null);

            Assert.Null(viewModel.PreviewScene);
            Assert.True(viewModel.CanMarkEyes);
            Assert.Contains("composta", viewModel.RigSummary);
        });
    }

    [Fact]
    public void TruncatedPsdIsRejectedWithoutChangingTheProject()
    {
        using var directory = new TemporaryFolder();
        var repository = new ProjectRepository(directory.Path);
        var project = repository.Create("Corrompido");
        var bytes = BuildPsd(4, 4, SampleLayers);
        var path = directory.Write("quebrado.psd", bytes[..(bytes.Length - 40)]);
        var service = new ImageImportService(repository);

        RunOnSta(() => Assert.Throws<InvalidDataException>(() => service.Inspect(path)));

        Assert.Empty(repository.Load(project.Id).ResourceReferences);
    }

    [Fact]
    public void PsdWithWrongSignatureOrUnsupportedModeIsRejected()
    {
        var notPsd = Encoding.ASCII.GetBytes("isto nao e um psd de verdade");
        Assert.Throws<InvalidDataException>(() => PsdReader.Read(notPsd));

        var cmyk = BuildPsd(4, 4, SampleLayers);
        cmyk[24] = 0;
        cmyk[25] = 4; // modo CMYK
        Assert.Throws<InvalidDataException>(() => PsdReader.Read(cmyk));

        var psb = BuildPsd(4, 4, SampleLayers);
        psb[5] = 2;
        Assert.Throws<InvalidDataException>(() => PsdReader.Read(psb));
    }

    [Fact]
    public void PsdAbove40MegapixelsIsImportedAndReopened()
    {
        using var directory = new TemporaryFolder();
        var repository = new ProjectRepository(directory.Path);
        var project = repository.Create("PSD grande");
        var layers = new SyntheticLayer[]
        {
            new("Fundo", 0, 0, 64, 64, (200, 100, 50), Visible: true, Rle: false),
            new("Olho", 100, 100, 32, 16, (10, 20, 30), Visible: true, Rle: false)
        };
        var psdPath = directory.Write("grande.psd", BuildPsd(7000, 7000, layers));
        var service = new ImageImportService(repository);

        RunOnSta(() =>
        {
            var inspected = service.Inspect(psdPath);
            Assert.Equal((7000, 7000), (inspected.Width, inspected.Height));
            var reference = service.Import(project, inspected);
            var reopened = service.InspectResource(repository.Load(project.Id), reference);
            Assert.Equal((7000, 7000), (reopened.Width, reopened.Height));
        });
    }

    [Fact]
    public void PsdAboveDimensionLimitIsRejected()
    {
        var huge = BuildPsd(4, 4, SampleLayers);
        huge[14] = 0x7F; // altura enorme
        Assert.Throws<InvalidDataException>(() => PsdReader.Read(huge));
    }

    private static (byte B, byte G, byte R, byte A) PixelAt(byte[] png, int x, int y)
    {
        using var stream = new MemoryStream(png);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        var converted = new FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4];
        converted.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return (pixel[0], pixel[1], pixel[2], pixel[3]);
    }

    private sealed record SyntheticLayer(
        string Name, int Left, int Top, int Width, int Height, (byte R, byte G, byte B) Color, bool Visible, bool Rle);

    private static byte[] BuildPsd(int width, int height, SyntheticLayer[] layers)
    {
        var output = new MemoryStream();
        void U16(int value) { output.WriteByte((byte)(value >> 8)); output.WriteByte((byte)value); }
        void U32(int value) { U16(value >> 16); U16(value & 0xFFFF); }
        void Ascii(string text) => output.Write(Encoding.ASCII.GetBytes(text));

        Ascii("8BPS");
        U16(1);
        output.Write(new byte[6]);
        U16(3);
        U32(height);
        U32(width);
        U16(8);
        U16(3);
        U32(0);
        U32(0);

        var info = new MemoryStream();
        var data = new MemoryStream();
        void IU16(int value) { info.WriteByte((byte)(value >> 8)); info.WriteByte((byte)value); }
        void IU32(int value) { IU16(value >> 16); IU16(value & 0xFFFF); }

        IU16(layers.Length);
        foreach (var layer in layers)
        {
            IU32(layer.Top);
            IU32(layer.Left);
            IU32(layer.Top + layer.Height);
            IU32(layer.Left + layer.Width);
            IU16(4);

            var channels = new (int Id, byte Value)[]
            {
                (0, layer.Color.R), (1, layer.Color.G), (2, layer.Color.B), (-1, 255)
            };
            foreach (var (id, value) in channels)
            {
                var plane = EncodePlane(layer, value);
                IU16(id & 0xFFFF);
                IU32(plane.Length);
                data.Write(plane);
            }

            info.Write(Encoding.ASCII.GetBytes("8BIM"));
            info.Write(Encoding.ASCII.GetBytes("norm"));
            info.WriteByte(255);
            info.WriteByte(0);
            info.WriteByte((byte)(layer.Visible ? 0 : 2));
            info.WriteByte(0);

            var nameBytes = Encoding.Latin1.GetBytes(layer.Name);
            var padded = (nameBytes.Length + 1 + 3) / 4 * 4;
            IU32(8 + padded);
            IU32(0);
            IU32(0);
            info.WriteByte((byte)nameBytes.Length);
            info.Write(nameBytes);
            info.Write(new byte[padded - nameBytes.Length - 1]);
        }

        info.Write(data.ToArray());
        if (info.Length % 2 != 0)
        {
            info.WriteByte(0);
        }

        var layerInfo = info.ToArray();
        U32(layerInfo.Length + 4 + 4);
        U32(layerInfo.Length);
        output.Write(layerInfo);
        U32(0);
        return output.ToArray();
    }

    private static byte[] EncodePlane(SyntheticLayer layer, byte value)
    {
        var plane = new MemoryStream();
        if (!layer.Rle)
        {
            plane.WriteByte(0);
            plane.WriteByte(0);
            plane.Write(Enumerable.Repeat(value, layer.Width * layer.Height).ToArray());
            return plane.ToArray();
        }

        plane.WriteByte(0);
        plane.WriteByte(1);
        foreach (var _ in Enumerable.Range(0, layer.Height))
        {
            plane.WriteByte(0);
            plane.WriteByte(2);
        }

        foreach (var _ in Enumerable.Range(0, layer.Height))
        {
            plane.WriteByte((byte)(1 - layer.Width));
            plane.WriteByte(value);
        }

        return plane.ToArray();
    }

    private static T RunOnSta<T>(Func<T> action)
    {
        T? result = default;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception exception)
            {
                error = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }

        return result!;
    }

    private static void RunOnSta(Action action) =>
        RunOnSta(() =>
        {
            action();
            return true;
        });

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"VTuberStudioAI.Psd.{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string Write(string name, byte[] bytes)
        {
            var path = System.IO.Path.Combine(Path, name);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
