using System.Runtime.ExceptionServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VTuberStudioAI.App.Images;
using VTuberStudioAI.App.Projects;
using VTuberStudioAI.App.ViewModels;
using Xunit;

namespace VTuberStudioAI.App.Tests;

public sealed class ImageImportTests
{
    [Fact]
    public void ValidPngIsDecodedAndReportsItsActualDimensions()
    {
        using var directory = new TemporaryDirectory();
        var imagePath = directory.WritePng("portrait.png");
        var service = new ImageImportService(new ProjectRepository(directory.Path));

        var result = RunOnSta(() => service.Inspect(imagePath));

        Assert.Equal("PNG", result.Format);
        Assert.Equal("portrait.png", result.FileName);
        Assert.Equal(2, result.Width);
        Assert.Equal(2, result.Height);
    }

    [Fact]
    public void ValidJpegIsDecodedAndReportsItsActualDimensions()
    {
        using var directory = new TemporaryDirectory();
        var imagePath = directory.WriteBytes("portrait.jpg", directory.CreateJpegBytes());
        var service = new ImageImportService(new ProjectRepository(directory.Path));

        var result = RunOnSta(() => service.Inspect(imagePath));

        Assert.Equal("JPEG", result.Format);
        Assert.Equal(2, result.Width);
        Assert.Equal(2, result.Height);
    }

    [Fact]
    public void MismatchedExtensionAndCorruptedImageAreRejected()
    {
        using var directory = new TemporaryDirectory();
        var bytes = directory.CreatePngBytes();
        var mismatched = directory.WriteBytes("portrait.jpg", bytes);
        var corrupted = directory.WriteBytes("corrupted.png", [1, 2, 3, 4, 5]);
        var truncated = directory.WriteBytes("truncated.png", bytes[..24]);
        var service = new ImageImportService(new ProjectRepository(directory.Path));

        Assert.Throws<InvalidDataException>(() => service.Inspect(mismatched));
        Assert.Throws<InvalidDataException>(() => service.Inspect(corrupted));
        Assert.Throws<InvalidDataException>(() => RunOnSta(() => service.Inspect(truncated)));
    }

    [Fact]
    public void ImageLargerThanTheFileSizeLimitIsRejectedBeforeDecoding()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "too-large.png");
        using (var stream = new FileStream(path, FileMode.CreateNew))
        {
            stream.SetLength(ImageImportService.MaximumFileSizeBytes + 1);
        }

        var service = new ImageImportService(new ProjectRepository(directory.Path));

        var error = Assert.Throws<InvalidDataException>(() => service.Inspect(path));
        Assert.Contains("25 MiB", error.Message);
    }

    [Fact]
    public void ImageDimensionsAboveTheLimitAreRejectedFromHeader()
    {
        using var directory = new TemporaryDirectory();
        var bytes = directory.CreatePngBytes();
        bytes[16] = 0;
        bytes[17] = 0;
        bytes[18] = 0x2E;
        bytes[19] = 0xE1;
        var path = directory.WriteBytes("too-wide.png", bytes);
        var service = new ImageImportService(new ProjectRepository(directory.Path));

        var error = Assert.Throws<InvalidDataException>(() => service.Inspect(path));
        Assert.Contains("dimensões", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LargeButAllowedHeaderDimensionsAreNotRejectedByTheSizeLimit()
    {
        using var directory = new TemporaryDirectory();
        var bytes = directory.CreatePngBytes();
        bytes[16] = 0;
        bytes[17] = 0;
        bytes[18] = 0x1F;
        bytes[19] = 0x40; // 8000
        bytes[20] = 0;
        bytes[21] = 0;
        bytes[22] = 0x17;
        bytes[23] = 0x70; // 6000 (48 MP, antes recusado)
        var path = directory.WriteBytes("grande.png", bytes);
        var service = new ImageImportService(new ProjectRepository(directory.Path));

        var error = Record.Exception(() => service.Inspect(path));

        Assert.False(error is InvalidDataException { Message: var message } &&
                     message.Contains("Dimensões não permitidas", StringComparison.Ordinal));
    }

    [Fact]
    public void WebpExtensionIsExplicitlyRejected()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.WriteBytes("portrait.webp", [1, 2, 3]);
        var service = new ImageImportService(new ProjectRepository(directory.Path));

        var error = Assert.Throws<InvalidDataException>(() => service.Inspect(path));
        Assert.Contains("WebP não é suportado", error.Message);
    }

    [Fact]
    public void CancelingPreviewDoesNotChangeTheProject()
    {
        using var directory = new TemporaryDirectory();
        var repository = new ProjectRepository(directory.Path);
        var project = repository.Create("Sem alteração");
        var imagePath = directory.WritePng("portrait.png");
        var viewModel = new MainWindowViewModel(
            repository,
            _ => false,
            (_, _) => { },
            new ImageImportService(repository),
            () => imagePath);

        RunOnSta(() =>
        {
            viewModel.ImportTargetProject = project;
            viewModel.ChooseImageFileCommand.Execute(null);

            Assert.True(viewModel.HasPendingImage);
            Assert.True(viewModel.CanConfirmImageImport);
            Assert.Empty(repository.Load(project.Id).ResourceReferences);
            viewModel.ZoomInCommand.Execute(null);
            Assert.Equal(1.2, viewModel.ImageZoom, 5);
            viewModel.FitImageCommand.Execute(null);
            Assert.Equal(1, viewModel.ImageZoom);
            viewModel.ZoomOutCommand.Execute(null);
            Assert.Equal(1 / 1.2, viewModel.ImageZoom, 5);
            for (var index = 0; index < 30; index++)
            {
                viewModel.ZoomInCommand.Execute(null);
            }

            Assert.Equal(5, viewModel.ImageZoom);
            viewModel.RestoreImageFramingCommand.Execute(null);
            Assert.Equal(1, viewModel.ImageZoom);

            viewModel.CancelImagePreviewCommand.Execute(null);

            Assert.False(viewModel.HasPendingImage);
            Assert.False(viewModel.HasDisplayImage);
            Assert.Empty(repository.Load(project.Id).ResourceReferences);
        });
    }

    [Fact]
    public void ConfirmedImportPersistsAfterReopeningAndDoesNotDeleteOriginalWhenReferenceIsRemoved()
    {
        using var directory = new TemporaryDirectory();
        var root = Path.Combine(directory.Path, "library");
        var repository = new ProjectRepository(root);
        var project = repository.Create("Personagem");
        var originalPath = directory.WritePng("avatar.png");
        var originalBytes = File.ReadAllBytes(originalPath);
        var service = new ImageImportService(repository);

        var image = RunOnSta(() => service.Inspect(originalPath));
        var relativeReference = service.Import(project, image);

        var reopenedRepository = new ProjectRepository(root);
        var reopened = reopenedRepository.Load(project.Id);
        var reopenedService = new ImageImportService(reopenedRepository);
        var storedImage = RunOnSta(() => reopenedService.InspectResource(reopened, relativeReference));
        var copiedPath = reopenedRepository.GetResourcePath(project.Id, relativeReference);

        Assert.Contains(relativeReference, reopened.ResourceReferences);
        Assert.Equal("avatar.png", storedImage.FileName);
        Assert.Equal(2, storedImage.Width);
        Assert.Equal(originalBytes, File.ReadAllBytes(originalPath));
        Assert.True(File.Exists(copiedPath));

        reopenedService.RemoveReference(reopened, relativeReference);

        Assert.Empty(reopenedRepository.Load(project.Id).ResourceReferences);
        Assert.True(File.Exists(originalPath));
        Assert.True(File.Exists(copiedPath));
    }

    [Fact]
    public void ImportRequiresAProjectAndCreatingOneEnablesConfirmation()
    {
        using var directory = new TemporaryDirectory();
        var repository = new ProjectRepository(Path.Combine(directory.Path, "library"));
        var imagePath = directory.WritePng("portrait.png");
        var viewModel = new MainWindowViewModel(
            repository,
            _ => false,
            (_, _) => { },
            new ImageImportService(repository),
            () => imagePath)
        {
            NewProjectName = "Novo personagem"
        };

        RunOnSta(() =>
        {
            viewModel.ChooseImageFileCommand.Execute(null);
            Assert.False(viewModel.CanConfirmImageImport);
            viewModel.ConfirmImageImportCommand.Execute(null);
            Assert.Contains("Selecione um projeto ou crie um novo", viewModel.ErrorMessage);
            Assert.Empty(repository.List());

            viewModel.CreateProjectCommand.Execute(null);
            Assert.True(viewModel.CanConfirmImageImport);
            viewModel.ConfirmImageImportCommand.Execute(null);
            Assert.Single(repository.Load(viewModel.ActiveProject!.Id).ResourceReferences);
        });
    }

    [Fact]
    public void FailedProjectCopyLeavesTheOriginalAndProjectMetadataUntouched()
    {
        using var directory = new TemporaryDirectory();
        var repository = new ProjectRepository(directory.Path);
        var project = repository.Create("Falha ao copiar");
        var resourcePath = Path.Combine(directory.Path, project.Id.ToString("D"), "resources");
        File.WriteAllText(resourcePath, "preserve");
        var originalPath = directory.WritePng("original.png");
        var originalBytes = File.ReadAllBytes(originalPath);
        var service = new ImageImportService(repository);
        var image = RunOnSta(() => service.Inspect(originalPath));

        Assert.Throws<IOException>(() => service.Import(project, image));
        Assert.Empty(repository.Load(project.Id).ResourceReferences);
        Assert.Equal(originalBytes, File.ReadAllBytes(originalPath));
        Assert.Equal("preserve", File.ReadAllText(resourcePath));
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
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"VTuberStudioAI.Images.{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public byte[] CreatePngBytes()
        {
            var pixels = new byte[]
            {
                0x22, 0x88, 0xCC, 0xFF, 0x22, 0x88, 0xCC, 0xFF,
                0x22, 0x88, 0xCC, 0xFF, 0x22, 0x88, 0xCC, 0xFF
            };
            var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 8);
            bitmap.Freeze();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }

        public string WritePng(string name) => WriteBytes(name, CreatePngBytes());

        public byte[] CreateJpegBytes()
        {
            var pixels = new byte[]
            {
                0x22, 0x88, 0xCC, 0xFF, 0x22, 0x88, 0xCC, 0xFF,
                0x22, 0x88, 0xCC, 0xFF, 0x22, 0x88, 0xCC, 0xFF
            };
            var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 8);
            bitmap.Freeze();
            var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }

        public string WriteBytes(string name, byte[] bytes)
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
