using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VTuberStudioAI.App.Images;
using VTuberStudioAI.App.Preview;
using VTuberStudioAI.App.Projects;
using VTuberStudioAI.App.ViewModels;
using Xunit;

namespace VTuberStudioAI.App.Tests;

public sealed class EyePreviewTests
{
    [Fact]
    public void GazeFollowsMouseAndIsClamped()
    {
        var area = new Size(200, 100);

        Assert.Equal(new Vector(0, 0), PreviewMath.GazeFromMouse(new Point(100, 50), area));
        Assert.Equal(new Vector(-1, -1), PreviewMath.GazeFromMouse(new Point(-50, -50), area));
        Assert.Equal(new Vector(1, 1), PreviewMath.GazeFromMouse(new Point(900, 900), area));
        Assert.Equal(new Vector(0, 0), PreviewMath.GazeFromMouse(new Point(10, 10), new Size(0, 0)));
    }

    [Fact]
    public void BlinkClosesAndReopens()
    {
        Assert.Equal(0, PreviewMath.BlinkAmount(0));
        Assert.Equal(1, PreviewMath.BlinkAmount(0.5), 5);
        Assert.Equal(0, PreviewMath.BlinkAmount(1));
        Assert.Equal(0, PreviewMath.BlinkAmount(2));
    }

    [Fact]
    public void HitTestOnlyMatchesInsideTheEye()
    {
        var eye = new EyeRegion(0.2, 0.3, 0.1, 0.1);

        Assert.True(PreviewMath.HitsEye(eye, new Point(0.25, 0.35)));
        Assert.False(PreviewMath.HitsEye(eye, new Point(0.5, 0.35)));
        Assert.False(PreviewMath.HitsEye(eye, new Point(0.25, 0.9)));
    }

    [Fact]
    public void EyelidColorIsSampledFromTheImage()
    {
        var color = RunOnSta(() =>
        {
            var pixels = new byte[4 * 4 * 4];
            for (var i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 0x22;
                pixels[i + 1] = 0x88;
                pixels[i + 2] = 0xCC;
                pixels[i + 3] = 0xFF;
            }

            var bitmap = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, pixels, 16);
            return PreviewMath.SampleEyelidColor(bitmap, new EyeRegion(0.25, 0.5, 0.5, 0.3));
        });

        Assert.Equal(Color.FromRgb(0xCC, 0x88, 0x22), color);
    }

    [Fact]
    public void EyesAreSavedAndReadAfterReopening()
    {
        using var directory = new TemporaryDirectory();
        var repository = new ProjectRepository(directory.Path);
        var project = AddImage(repository, repository.Create("Preview"));
        var service = new EyePreviewService(repository);

        service.AddEye(project.Id, "resources/a.png", new EyeRegion(0.2, 0.3, 0.1, 0.1));
        service.AddEye(project.Id, "resources/a.png", new EyeRegion(0.6, 0.3, 0.1, 0.1));

        var reopened = new ProjectRepository(directory.Path).Load(project.Id);
        Assert.Equal(2, EyePreviewService.ReadEyes(reopened, "resources/a.png").Count);
        Assert.Empty(EyePreviewService.ReadEyes(reopened, "resources/outra.png"));
    }

    [Theory]
    [InlineData(-0.1, 0.2, 0.1, 0.1)]
    [InlineData(0.2, 0.2, 0.005, 0.1)]
    [InlineData(0.95, 0.2, 0.2, 0.1)]
    [InlineData(double.NaN, 0.2, 0.1, 0.1)]
    public void InvalidEyeAreasAreRejectedWithoutChangingTheProject(double x, double y, double width, double height)
    {
        using var directory = new TemporaryDirectory();
        var repository = new ProjectRepository(directory.Path);
        var project = AddImage(repository, repository.Create("Preview"));
        var service = new EyePreviewService(repository);

        Assert.Throws<ArgumentException>(() =>
            service.AddEye(project.Id, "resources/a.png", new EyeRegion(x, y, width, height)));

        Assert.Empty(EyePreviewService.ReadEyes(repository.Load(project.Id), "resources/a.png"));
    }

    [Fact]
    public void ServiceRejectsUnknownImagesAndTooManyEyesAndCanClear()
    {
        using var directory = new TemporaryDirectory();
        var repository = new ProjectRepository(directory.Path);
        var project = AddImage(repository, repository.Create("Preview"));
        var service = new EyePreviewService(repository);

        Assert.Throws<InvalidDataException>(() =>
            service.AddEye(project.Id, "resources/../fora.png", new EyeRegion(0.1, 0.1, 0.1, 0.1)));

        for (var index = 0; index < EyePreviewService.MaxEyes; index++)
        {
            service.AddEye(project.Id, "resources/a.png", new EyeRegion(0.1, 0.1, 0.1, 0.1));
        }

        Assert.Throws<InvalidOperationException>(() =>
            service.AddEye(project.Id, "resources/a.png", new EyeRegion(0.1, 0.1, 0.1, 0.1)));

        service.ClearEyes(project.Id);
        Assert.Empty(EyePreviewService.ReadEyes(repository.Load(project.Id), "resources/a.png"));
    }

    [Fact]
    public void MarkedEyesSurviveReopeningAndKeepTheOriginalImageUntouched()
    {
        using var directory = new TemporaryDirectory();
        var repository = new ProjectRepository(directory.Path);
        var project = repository.Create("Piscar");
        var imageBytes = CreatePngBytes();
        var imagePath = Path.Combine(directory.Path, "origem.png");
        File.WriteAllBytes(imagePath, imageBytes);

        RunOnSta(() =>
        {
            var viewModel = NewViewModel(repository, imagePath);
            viewModel.ImportTargetProject = project;
            viewModel.ChooseImageFileCommand.Execute(null);
            viewModel.ConfirmImageImportCommand.Execute(null);
            Assert.True(viewModel.HasCurrentImage);

            viewModel.MarkEyeCommand.Execute(new EyeRegion(0.2, 0.2, 0.2, 0.2));

            Assert.True(viewModel.HasEyeRegions);
            var saved = repository.Load(project.Id);
            Assert.Single(EyePreviewService.ReadEyes(saved, saved.ResourceReferences[0]));

            var reopened = NewViewModel(repository, imagePath);
            reopened.SelectedProject = reopened.Projects.First(item => item.Id == project.Id);
            reopened.OpenSelectedProjectCommand.Execute(null);
            Assert.Single(reopened.EyeRegions);

            reopened.ClearEyesCommand.Execute(null);
            Assert.False(reopened.HasEyeRegions);
        });

        Assert.Equal(imageBytes, File.ReadAllBytes(imagePath));
    }

    [Fact]
    public void MarkingRequiresAnImageAndInvalidAreasShowAMessage()
    {
        using var directory = new TemporaryDirectory();
        var repository = new ProjectRepository(directory.Path);

        RunOnSta(() =>
        {
            var viewModel = NewViewModel(repository, null);
            viewModel.ToggleMarkEyesCommand.Execute(null);
            Assert.False(viewModel.IsMarkingEyes);

            viewModel.MarkEyeCommand.Execute(new EyeRegion(0.2, 0.2, 0.2, 0.2));
            Assert.False(string.IsNullOrWhiteSpace(viewModel.ErrorMessage));
            Assert.False(viewModel.HasEyeRegions);
        });
    }

    private static MainWindowViewModel NewViewModel(ProjectRepository repository, string? imagePath) =>
        new(repository, _ => false, (_, _) => { }, new ImageImportService(repository), () => imagePath);

    private static ProjectDocument AddImage(ProjectRepository repository, ProjectDocument project)
    {
        var path = repository.GetResourcePath(project.Id, "resources/a.png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, CreatePngBytes());
        return repository.Save(project with { ResourceReferences = ["resources/a.png"] });
    }

    private static byte[] CreatePngBytes()
    {
        var pixels = new byte[16];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 0x22;
            pixels[i + 1] = 0x88;
            pixels[i + 2] = 0xCC;
            pixels[i + 3] = 0xFF;
        }

        var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 8);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
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

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"VTuberStudioAI.Eyes.{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
