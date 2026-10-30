using System.Windows;
using VTuberStudioAI.App.Themes;

namespace VTuberStudioAI.App.ViewModels;

public sealed class ThemeOptionViewModel : ObservableObject
{
    private bool _isCurrent;

    public ThemeOptionViewModel(AppTheme theme)
    {
        Id = theme.Id;
        Name = theme.Name;
        Description = theme.Description;
        Swatches = [theme.Colors["AppBackgroundBrush"], theme.Colors["SurfaceBrush"], theme.Colors["AccentBrush"], theme.Colors["TextPrimaryBrush"]];
    }

    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public IReadOnlyList<string> Swatches { get; }

    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            if (SetProperty(ref _isCurrent, value))
            {
                OnPropertyChanged(nameof(InUseVisibility));
            }
        }
    }

    public Visibility InUseVisibility => _isCurrent ? Visibility.Visible : Visibility.Collapsed;
}
