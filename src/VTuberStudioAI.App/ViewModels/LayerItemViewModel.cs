using VTuberStudioAI.App.Images;
using VTuberStudioAI.App.Preview;

namespace VTuberStudioAI.App.ViewModels;

/// <summary>Linha editável da lista de camadas: o nome muda ao vivo e o papel é recalculado a cada letra.</summary>
public sealed class LayerItemViewModel : ObservableObject
{
    private readonly Action<LayerItemViewModel, string> _renamed;
    private string _name;

    public LayerItemViewModel(PsdLayerInfo layer, Action<LayerItemViewModel, string> renamed)
    {
        ArgumentNullException.ThrowIfNull(layer);
        _renamed = renamed ?? throw new ArgumentNullException(nameof(renamed));
        _name = layer.Name;
        File = layer.File;
        Visible = layer.Visible;
    }

    public string File { get; }

    public bool Visible { get; }

    public string VisibilityLabel => Visible ? string.Empty : "oculta";

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(RoleDescription));
                _renamed(this, _name);
            }
        }
    }

    public string RoleDescription => LayerNaming.Describe(LayerNaming.Parse(_name));
}
