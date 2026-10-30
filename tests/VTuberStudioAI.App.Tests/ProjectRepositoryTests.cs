using System.Text.Json;
using System.Text.Json.Nodes;
using VTuberStudioAI.App.Projects;
using VTuberStudioAI.App.ViewModels;
using Xunit;

namespace VTuberStudioAI.App.Tests;

public sealed class ProjectRepositoryTests
{
    [Fact]
    public void CreatedProjectCanBeSavedClosedAndReopened()
    {
        using var directory = new TemporaryDirectory();
        var repository = new ProjectRepository(directory.Path);
        var project = repository.Create("Meu personagem");
        var saved = repository.Save(project with
        {
            Configurations = new Dictionary<string, JsonElement>
            {
                ["canvas.zoom"] = JsonDocument.Parse("1.5").RootElement.Clone()
            }
        });

        var reopened = new ProjectRepository(directory.Path).Load(project.Id);

        Assert.Equal(project.Id, reopened.Id);
        Assert.Equal("Meu personagem", reopened.Name);
        Assert.Equal(ProjectDocument.CurrentFormatVersion, reopened.FormatVersion);
        Assert.Equal("2D", reopened.ContentType);
        Assert.Equal(project.CreatedAt, reopened.CreatedAt);
        Assert.Equal(saved.ModifiedAt, reopened.ModifiedAt);
        Assert.Equal(JsonValueKind.Number, reopened.Configurations["canvas.zoom"].ValueKind);
        Assert.Equal(1.5, reopened.Configurations["canvas.zoom"].GetDouble());
    }

    [Fact]
    public void RenameAndDuplicatePreserveProjectDataAndCopyReferencedFiles()
    {
        using var directory = new TemporaryDirectory();
        var repository = new ProjectRepository(directory.Path);
        var project = repository.Create("Raposa");
        var projectDirectory = Path.Combine(directory.Path, project.Id.ToString("D"));
        var assetPath = Path.Combine(projectDirectory, "assets", "portrait.png");
        Directory.CreateDirectory(Path.GetDirectoryName(assetPath)!);
        File.WriteAllText(assetPath, "local test asset");
        project = repository.Save(project with
        {
            ResourceReferences = ["assets/portrait.png"],
            Configurations = new Dictionary<string, JsonElement>
            {
                ["fur.color"] = JsonDocument.Parse("\"amber\"").RootElement.Clone()
            }
        });

        var renamed = repository.Rename(project.Id, "Raposa do norte");
        var duplicate = repository.Duplicate(renamed.Id);

        Assert.Equal("Raposa do norte", repository.Load(project.Id).Name);
        Assert.Equal("Raposa do norte (cópia)", duplicate.Name);
        Assert.NotEqual(project.Id, duplicate.Id);
        Assert.Equal("amber", duplicate.Configurations["fur.color"].GetString());
        Assert.Equal("local test asset", File.ReadAllText(Path.Combine(
            directory.Path,
            duplicate.Id.ToString("D"),
            "assets",
            "portrait.png")));
        Assert.Equal(2, repository.List().Count);
    }

    [Fact]
    public void DeleteRemovesOnlyTheSelectedProject()
    {
        using var directory = new TemporaryDirectory();
        var repository = new ProjectRepository(directory.Path);
        var deleted = repository.Create("Descartar");
        var retained = repository.Create("Manter");

        repository.Delete(deleted.Id);

        Assert.Throws<FileNotFoundException>(() => repository.Load(deleted.Id));
        Assert.Equal(retained.Id, Assert.Single(repository.List()).Id);
    }

    [Fact]
    public void ViewModelCancelingDeletePreservesProject()
    {
        using var directory = new TemporaryDirectory();
        var repository = new ProjectRepository(directory.Path);
        var project = repository.Create("Preservado");
        var viewModel = new MainWindowViewModel(repository, _ => false, (_, _) => { })
        {
            SelectedProject = project
        };

        viewModel.DeleteSelectedProjectCommand.Execute(null);

        Assert.Equal(project.Id, repository.Load(project.Id).Id);
        Assert.Contains(viewModel.Projects, item => item.Id == project.Id);
        Assert.Contains("cancelada", viewModel.OperationMessage);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("../fora")]
    [InlineData("nome:inválido")]
    [InlineData("CON")]
    [InlineData("nome.")]
    public void InvalidProjectNamesAreRejected(string name)
    {
        using var directory = new TemporaryDirectory();
        var repository = new ProjectRepository(directory.Path);

        Assert.Throws<ArgumentException>(() => repository.Create(name));
        Assert.Empty(repository.List());
    }

    [Fact]
    public void MissingMetadataFileIsReported()
    {
        using var directory = new TemporaryDirectory();
        var repository = new ProjectRepository(directory.Path);
        var project = repository.Create("Sem arquivo");
        File.Delete(Path.Combine(directory.Path, project.Id.ToString("D"), "project.json"));

        Assert.Throws<FileNotFoundException>(() => repository.Load(project.Id));
        Assert.Throws<FileNotFoundException>(() => repository.List());
    }

    [Fact]
    public void CorruptJsonIsReportedWithoutOverwritingIt()
    {
        using var directory = new TemporaryDirectory();
        var repository = new ProjectRepository(directory.Path);
        var project = repository.Create("Corrompido");
        var metadataPath = Path.Combine(directory.Path, project.Id.ToString("D"), "project.json");
        File.WriteAllText(metadataPath, "{ inválido");

        Assert.Throws<InvalidDataException>(() => repository.Load(project.Id));
        Assert.Throws<InvalidDataException>(() => repository.Save(project));
        Assert.Equal("{ inválido", File.ReadAllText(metadataPath));
    }

    [Fact]
    public void UnsupportedFormatVersionIsRejected()
    {
        using var directory = new TemporaryDirectory();
        var repository = new ProjectRepository(directory.Path);
        var project = repository.Create("Versão futura");
        var metadataPath = Path.Combine(directory.Path, project.Id.ToString("D"), "project.json");
        var json = File.ReadAllText(metadataPath).Replace(
            $"\"formatVersion\": {ProjectDocument.CurrentFormatVersion}",
            "\"formatVersion\": 99",
            StringComparison.Ordinal);
        File.WriteAllText(metadataPath, json);

        Assert.Throws<InvalidDataException>(() => repository.Load(project.Id));
    }

    [Fact]
    public void MissingRequiredMetadataFieldIsRejected()
    {
        using var directory = new TemporaryDirectory();
        var repository = new ProjectRepository(directory.Path);
        var project = repository.Create("Metadado incompleto");
        var metadataPath = Path.Combine(directory.Path, project.Id.ToString("D"), "project.json");
        var json = JsonNode.Parse(File.ReadAllText(metadataPath))!.AsObject();
        json.Remove("formatVersion");
        File.WriteAllText(metadataPath, json.ToJsonString());

        Assert.Throws<InvalidDataException>(() => repository.Load(project.Id));
    }

    [Fact]
    public void WriteFailureIsReportedWhenLibraryPathIsAFile()
    {
        using var directory = new TemporaryDirectory();
        var libraryFile = Path.Combine(directory.Path, "not-a-folder");
        File.WriteAllText(libraryFile, "preserve");
        var repository = new ProjectRepository(libraryFile);

        Assert.Throws<IOException>(() => repository.Create("Falha de escrita"));
        Assert.Throws<IOException>(() => repository.List());
        Assert.Equal("preserve", File.ReadAllText(libraryFile));
    }

    [Fact]
    public void ViewModelRefreshesLibraryAndOpensFurryStudioForANewCharacter()
    {
        using var directory = new TemporaryDirectory();
        var repository = new ProjectRepository(directory.Path);
        var viewModel = new MainWindowViewModel(repository, _ => false, (_, _) => { })
        {
            NewProjectName = "Projeto de teste"
        };

        viewModel.CreateProjectCommand.Execute(null);

        var project = Assert.Single(viewModel.Projects);
        Assert.Equal(project.Id, viewModel.ActiveProject?.Id);
        Assert.Equal("furry", viewModel.CurrentPage.Key);
        Assert.Contains(project, viewModel.RecentProjects);

        viewModel.NavigateToLibraryCommand.Execute(null);
        viewModel.SelectedProject = project;
        viewModel.SaveActiveProjectCommand.Execute(null);

        Assert.Equal(project.Id, Assert.Single(repository.List()).Id);
        Assert.Contains("salvo", viewModel.OperationMessage);
    }

    [Fact]
    public void CreatingFromScratchMakesANewEmptyCharacterWithoutChangingAnExistingProject()
    {
        using var directory = new TemporaryDirectory();
        var repository = new ProjectRepository(directory.Path);
        var existing = repository.Create("Avatar existente");
        var viewModel = new MainWindowViewModel(repository, _ => false, (_, _) => { })
        {
            NewProjectName = "Meu avatar novo",
            ImportTargetProject = existing
        };

        viewModel.NavigateToCreateCommand.Execute(null);
        viewModel.CreateProjectCommand.Execute(null);

        var created = Assert.Single(viewModel.Projects, project => project.Name == "Meu avatar novo");
        Assert.NotEqual(existing.Id, created.Id);
        Assert.Equal(created.Id, viewModel.ActiveProject?.Id);
        Assert.Equal("furry", viewModel.CurrentPage.Key);
        Assert.Empty(created.ResourceReferences);
        Assert.Equal(existing.Id, repository.Load(existing.Id).Id);
        Assert.Contains("criado do zero", viewModel.OperationMessage);
    }

    [Fact]
    public void ViewModelRenameDuplicateOpenSaveAndConfirmedDeleteRefreshTheLibrary()
    {
        using var directory = new TemporaryDirectory();
        var repository = new ProjectRepository(directory.Path);
        var viewModel = new MainWindowViewModel(repository, _ => true, (_, _) => { })
        {
            NewProjectName = "Original"
        };
        viewModel.CreateProjectCommand.Execute(null);
        var original = Assert.Single(viewModel.Projects);

        viewModel.NavigateToLibraryCommand.Execute(null);
        viewModel.SelectedProject = original;
        viewModel.RenameProjectName = "Original renomeado";
        viewModel.RenameSelectedProjectCommand.Execute(null);
        Assert.Equal("Original renomeado", Assert.Single(viewModel.Projects).Name);
        Assert.Equal("Original renomeado", viewModel.ActiveProject?.Name);

        viewModel.DuplicateSelectedProjectCommand.Execute(null);
        var duplicate = Assert.Single(viewModel.Projects, project => project.Id != original.Id);
        Assert.Equal("Original renomeado (cópia)", duplicate.Name);

        viewModel.OpenSelectedProjectCommand.Execute(null);
        Assert.Equal(duplicate.Id, viewModel.ActiveProject?.Id);
        Assert.Equal("studio", viewModel.CurrentPage.Key);
        viewModel.SaveActiveProjectCommand.Execute(null);
        Assert.Equal(duplicate.Id, repository.Load(duplicate.Id).Id);

        viewModel.NavigateToLibraryCommand.Execute(null);
        viewModel.SelectedProject = duplicate;
        viewModel.DeleteSelectedProjectCommand.Execute(null);

        Assert.Single(viewModel.Projects);
        Assert.Equal(original.Id, Assert.Single(viewModel.Projects).Id);
        Assert.Null(viewModel.ActiveProject);
        Assert.Throws<FileNotFoundException>(() => repository.Load(duplicate.Id));
    }

    [Fact]
    public void ViewModelShowsInvalidNameErrorInsteadOfCreatingAProject()
    {
        using var directory = new TemporaryDirectory();
        var repository = new ProjectRepository(directory.Path);
        var viewModel = new MainWindowViewModel(repository, _ => false, (_, _) => { })
        {
            NewProjectName = "../outside"
        };

        viewModel.CreateProjectCommand.Execute(null);

        Assert.Empty(repository.List());
        Assert.Contains("caracteres não permitidos", viewModel.ErrorMessage);
    }

    [Fact]
    public void ResourceReferencesCannotEscapeProjectDirectory()
    {
        using var directory = new TemporaryDirectory();
        var repository = new ProjectRepository(directory.Path);
        var project = repository.Create("Caminho seguro");

        Assert.Throws<InvalidDataException>(() =>
            repository.Save(project with { ResourceReferences = ["../outside.png"] }));
        Assert.Empty(repository.Load(project.Id).ResourceReferences);
    }

    [Fact]
    public void ResourcePathMustStayInsideProjectAndRejectReparsePoints()
    {
        using var directory = new TemporaryDirectory();
        var repository = new ProjectRepository(directory.Path);
        var project = repository.Create("Caminho do recurso");

        Assert.Throws<InvalidDataException>(() => repository.GetResourcePath(project.Id, "..\\outside.png"));
        Assert.Throws<InvalidDataException>(() => repository.GetResourcePath(project.Id, "C:/outside.png"));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"VTuberStudioAI.Tests.{Guid.NewGuid():N}");
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
