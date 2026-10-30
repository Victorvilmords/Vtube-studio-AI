using VTuberStudioAI.App.Projects;
using VTuberStudioAI.App.ViewModels;
using Xunit;

namespace VTuberStudioAI.App.Tests;

public sealed class NavigationTests
{
    [Fact]
    public void HomeEnablesProjectActionsAndExplainsThatImportIsUnavailable()
    {
        var viewModel = CreateViewModel();

        Assert.Equal("home", viewModel.CurrentPage.Key);
        Assert.Contains("Bem-vindo", viewModel.CurrentPage.Description);
        Assert.Contains("personagens virtuais", viewModel.CurrentPage.Description);
        Assert.True(viewModel.CanManageProjects);
        Assert.True(viewModel.CanImportCharacter);
        Assert.Contains("PSD", viewModel.ProjectActionsUnavailableReason);
    }

    [Theory]
    [InlineData("studio", "Meu estúdio")]
    [InlineData("create", "Criar VTuber")]
    [InlineData("furry", "Furry Studio")]
    [InlineData("preview", "Preview")]
    public void HomeShortcutNavigatesToItsDestinationAndUpdatesSelection(string key, string title)
    {
        var viewModel = CreateViewModel();

        viewModel.NavigateCommand.Execute(key);

        Assert.Equal(key, viewModel.CurrentPage.Key);
        Assert.Equal(title, viewModel.CurrentPage.Title);
        Assert.Equal(key, Assert.Single(viewModel.NavigationItems, item => item.IsSelected).Key);
    }

    [Theory]
    [InlineData("home", "Início")]
    [InlineData("studio", "Meu estúdio")]
    [InlineData("create", "Criar VTuber")]
    [InlineData("furry", "Furry Studio")]
    [InlineData("preview", "Preview")]
    [InlineData("motion", "Laboratório de movimento")]
    [InlineData("library", "Biblioteca de projetos")]
    [InlineData("integrations", "Integrações")]
    [InlineData("settings", "Configurações")]
    public void NavigatingToAnItemOpensItsPageAndSelectsOnlyThatItem(string key, string title)
    {
        var viewModel = CreateViewModel();
        var item = Assert.Single(viewModel.NavigationItems, navigationItem => navigationItem.Key == key);

        viewModel.NavigateCommand.Execute(item);

        Assert.Equal(key, viewModel.CurrentPage.Key);
        Assert.Equal(title, viewModel.CurrentPage.Title);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.CurrentPage.Badge));
        Assert.False(string.IsNullOrWhiteSpace(viewModel.CurrentPage.PanelTitle));
        Assert.False(string.IsNullOrWhiteSpace(viewModel.CurrentPage.PanelDescription));
        Assert.True(item.IsSelected);
        Assert.Single(viewModel.NavigationItems, navigationItem => navigationItem.IsSelected);
    }

    [Fact]
    public void StudioShortcutKeepsNavigationSelectionSynchronized()
    {
        var viewModel = CreateViewModel();

        viewModel.NavigateToStudioCommand.Execute(null);

        Assert.Equal("studio", viewModel.CurrentPage.Key);
        Assert.Equal("studio", Assert.Single(viewModel.NavigationItems, item => item.IsSelected).Key);
    }

    [Fact]
    public void PreviewTabSitsBelowFurryStudioAndFollowMouseTogglesHint()
    {
        var viewModel = CreateViewModel();
        var keys = viewModel.NavigationItems.Select(item => item.Key).ToList();

        Assert.Equal(keys.IndexOf("furry") + 1, keys.IndexOf("preview"));
        Assert.False(viewModel.FollowMouse);

        viewModel.ToggleFollowMouseCommand.Execute(null);

        Assert.True(viewModel.FollowMouse);
        Assert.Contains("Parar", viewModel.FollowMouseButtonLabel);
        viewModel.ToggleFollowMouseCommand.Execute(null);
        Assert.False(viewModel.FollowMouse);
    }

    [Fact]
    public void NavigationCommandIgnoresParametersThatAreNotNavigationItems()
    {
        var viewModel = CreateViewModel();

        viewModel.NavigateCommand.Execute(new object());

        Assert.Equal("home", viewModel.CurrentPage.Key);
        Assert.Equal("home", Assert.Single(viewModel.NavigationItems, item => item.IsSelected).Key);
    }

    private static MainWindowViewModel CreateViewModel() =>
        new(
            new ProjectRepository(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))),
            _ => false,
            (_, _) => { });
}
