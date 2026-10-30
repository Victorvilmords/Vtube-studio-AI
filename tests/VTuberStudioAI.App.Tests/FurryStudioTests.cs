using System.Text.Json.Nodes;
using VTuberStudioAI.App.Furry;
using VTuberStudioAI.App.Projects;
using VTuberStudioAI.App.ViewModels;
using Xunit;

namespace VTuberStudioAI.App.Tests;

public sealed class FurryStudioTests
{
    [Fact]
    public void ComponentTypesHaveDistinctValidatedDefaults()
    {
        Assert.Equal(7, FurryComponentCatalog.Types.Count);

        foreach (var type in Enum.GetValues<FurryComponentType>())
        {
            var component = FurryComponentCatalog.Create(type, FurryComponentCatalog.GetTypeLabel(type));

            FurryComponentCatalog.Validate(component);
            Assert.NotEmpty(component.Parameters);
        }

        Assert.NotEqual(
            string.Join(',', FurryComponentCatalog.GetParameters(FurryComponentType.Ears).Select(parameter => parameter.Key)),
            string.Join(',', FurryComponentCatalog.GetParameters(FurryComponentType.Horns).Select(parameter => parameter.Key)));
    }

    [Fact]
    public void ComponentsCanBeAddedRenamedConfiguredDisabledAndReopened()
    {
        using var directory = new TemporaryDirectory();
        var repository = new ProjectRepository(directory.Path);
        var service = new FurryStudioService(repository);
        var project = repository.Create("Raposa");
        var ears = service.Add(project, FurryComponentType.Ears, "Orelhas");

        ears = service.Rename(project, ears.Id, "Orelhas grandes");
        ears = service.SetNumberParameter(project, ears.Id, "scale", 1.75);
        ears = service.SetBooleanParameter(project, ears.Id, "tufts", true);
        ears = service.SetEnabled(project, ears.Id, false);

        var reopened = new ProjectRepository(directory.Path).Load(project.Id);
        var savedComponent = Assert.Single(FurryStudioService.ReadComponents(reopened));
        Assert.Equal(ears.Id, savedComponent.Id);
        Assert.Equal("Orelhas grandes", savedComponent.Name);
        Assert.False(savedComponent.IsEnabled);
        Assert.Equal(1.75, savedComponent.Parameters["scale"].GetDouble());
        Assert.True(savedComponent.Parameters["tufts"].GetBoolean());

        service.Remove(reopened, savedComponent.Id);
        Assert.Empty(FurryStudioService.ReadComponents(repository.Load(project.Id)));
    }

    [Fact]
    public void InvalidAndUnsupportedParametersAreRejectedWithoutChangingSavedValues()
    {
        using var directory = new TemporaryDirectory();
        var repository = new ProjectRepository(directory.Path);
        var service = new FurryStudioService(repository);
        var project = repository.Create("Raposa");
        var ears = service.Add(project, FurryComponentType.Ears, "Orelhas");

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            service.SetNumberParameter(project, ears.Id, "scale", 3.01));
        Assert.Throws<ArgumentException>(() =>
            service.SetNumberParameter(project, ears.Id, "unknown", 0.5));
        Assert.Throws<ArgumentException>(() =>
            service.SetBooleanParameter(project, ears.Id, "angle", true));
        Assert.Equal(1, FurryStudioService.ReadComponents(repository.Load(project.Id))
            .Single().Parameters["scale"].GetDouble());
    }

    [Fact]
    public void PresetsAreSavedAsConfigurationsAndDoNotModifyImageFiles()
    {
        using var directory = new TemporaryDirectory();
        var repository = new ProjectRepository(directory.Path);
        var project = repository.Create("Raposa");
        var imagePath = Path.Combine(directory.Path, project.Id.ToString("D"), "resources", "portrait.png");
        Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
        var originalBytes = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3 };
        File.WriteAllBytes(imagePath, originalBytes);
        project = repository.Save(project with { ResourceReferences = ["resources/portrait.png"] });
        var service = new FurryStudioService(repository);

        var preset = service.AddPreset(project, "ears-alert");
        service.Add(project, FurryComponentType.Custom, "Marca pessoal");

        var reopened = new ProjectRepository(directory.Path).Load(project.Id);
        var components = FurryStudioService.ReadComponents(reopened);
        Assert.Equal(2, components.Count);
        Assert.Contains(components, component => component.Id == preset.Id &&
            component.Name.Contains("configuração", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(originalBytes, File.ReadAllBytes(imagePath));
        Assert.Contains("resources/portrait.png", reopened.ResourceReferences);
    }

    [Fact]
    public void FurryStudioViewModelCreatesAndEditsProjectComponents()
    {
        using var directory = new TemporaryDirectory();
        var repository = new ProjectRepository(directory.Path);
        var viewModel = new MainWindowViewModel(repository, _ => false, (_, _) => { })
        {
            NewProjectName = "Projeto furry"
        };
        viewModel.NavigateCommand.Execute("furry");
        viewModel.CreateProjectCommand.Execute(null);

        viewModel.SelectedFurryComponentType = viewModel.FurryComponentTypes
            .Single(option => option.Type == FurryComponentType.Tail);
        viewModel.NewFurryComponentName = "Cauda";
        viewModel.AddFurryComponentCommand.Execute(null);
        var component = Assert.Single(viewModel.FurryComponents);
        viewModel.SelectedFurryComponent = component;
        var length = viewModel.FurryParameters.Single(parameter => parameter.Definition.Key == "length");
        length.NumberValue = 1.6;

        var saved = FurryStudioService.ReadComponents(repository.Load(viewModel.ActiveProject!.Id));
        Assert.Equal(1.6, Assert.Single(saved).Parameters["length"].GetDouble());
        Assert.Equal("furry", viewModel.CurrentPage.Key);
        Assert.Contains("deformação", viewModel.FurryScopeNotice);
    }

    [Fact]
    public void CancelingComponentRemovalKeepsSavedConfiguration()
    {
        using var directory = new TemporaryDirectory();
        var repository = new ProjectRepository(directory.Path);
        var project = repository.Create("Projeto furry");
        var service = new FurryStudioService(repository);
        var component = service.Add(project, FurryComponentType.Ears, "Orelhas");
        var viewModel = new MainWindowViewModel(
            repository,
            _ => false,
            (_, _) => { },
            confirmRemoveFurryComponent: _ => false);
        viewModel.ImportTargetProject = project;
        viewModel.NavigateCommand.Execute("furry");
        viewModel.SelectedFurryComponent = viewModel.FurryComponents.Single();

        viewModel.RemoveFurryComponentCommand.Execute(null);

        Assert.Equal(component.Id, Assert.Single(viewModel.FurryComponents).Id);
        Assert.Equal(component.Id, Assert.Single(FurryStudioService.ReadComponents(repository.Load(project.Id))).Id);
        Assert.Contains("cancelada", viewModel.OperationMessage);
    }

    [Fact]
    public void ConfirmingComponentRemovalDeletesOnlyItsConfiguration()
    {
        using var directory = new TemporaryDirectory();
        var repository = new ProjectRepository(directory.Path);
        var project = repository.Create("Projeto furry");
        var service = new FurryStudioService(repository);
        var component = service.Add(project, FurryComponentType.Ears, "Orelhas");
        var viewModel = new MainWindowViewModel(
            repository,
            _ => false,
            (_, _) => { },
            confirmRemoveFurryComponent: selected => selected.Id == component.Id);
        viewModel.ImportTargetProject = project;
        viewModel.NavigateCommand.Execute("furry");
        viewModel.SelectedFurryComponent = viewModel.FurryComponents.Single();

        viewModel.RemoveFurryComponentCommand.Execute(null);

        Assert.Empty(viewModel.FurryComponents);
        Assert.Empty(FurryStudioService.ReadComponents(repository.Load(project.Id)));
        Assert.Contains("removido", viewModel.OperationMessage);
    }

    [Fact]
    public void CorruptedFurryConfigurationIsRejectedWhenProjectLoads()
    {
        using var directory = new TemporaryDirectory();
        var repository = new ProjectRepository(directory.Path);
        var project = repository.Create("Corrompido");
        var metadataPath = Path.Combine(directory.Path, project.Id.ToString("D"), "project.json");
        var json = JsonNode.Parse(File.ReadAllText(metadataPath))!.AsObject();
        json["configurations"]!.AsObject()["furry.components"] = new JsonObject();
        File.WriteAllText(metadataPath, json.ToJsonString());

        Assert.Throws<InvalidDataException>(() => repository.Load(project.Id));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"VTuberStudioAI.FurryTests.{Guid.NewGuid():N}");
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
