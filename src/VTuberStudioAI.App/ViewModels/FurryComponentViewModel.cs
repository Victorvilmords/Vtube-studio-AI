using System.ComponentModel;
using System.Runtime.CompilerServices;
using VTuberStudioAI.App.Furry;

namespace VTuberStudioAI.App.ViewModels;

public sealed class FurryComponentViewModel : INotifyPropertyChanged
{
    private readonly Action<FurryComponentViewModel, bool> _setEnabled;
    private bool _isEnabled;

    public FurryComponentViewModel(
        FurryComponent component,
        Action<FurryComponentViewModel, bool> setEnabled)
    {
        Component = component;
        _setEnabled = setEnabled;
        _isEnabled = component.IsEnabled;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public FurryComponent Component { get; private set; }

    public Guid Id => Component.Id;

    public string TypeLabel => FurryComponentCatalog.GetTypeLabel(Component.Type);

    public string Name => Component.Name;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _setEnabled(this, value);
            }
        }
    }

    public void Update(FurryComponent component)
    {
        Component = component;
        SetProperty(ref _isEnabled, component.IsEnabled, nameof(IsEnabled));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(TypeLabel));
    }

    private void SetProperty(ref bool field, bool value, string propertyName)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class FurryParameterViewModel : INotifyPropertyChanged
{
    private readonly Action<FurryParameterViewModel, double>? _setNumber;
    private readonly Action<FurryParameterViewModel, bool>? _setBoolean;
    private double _numberValue;
    private bool _booleanValue;

    public FurryParameterViewModel(
        FurryParameterDefinition definition,
        double numberValue,
        Action<FurryParameterViewModel, double> setNumber)
    {
        Definition = definition;
        _numberValue = numberValue;
        _setNumber = setNumber;
    }

    public FurryParameterViewModel(
        FurryParameterDefinition definition,
        bool booleanValue,
        Action<FurryParameterViewModel, bool> setBoolean)
    {
        Definition = definition;
        _booleanValue = booleanValue;
        _setBoolean = setBoolean;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public FurryParameterDefinition Definition { get; }

    public string Label => Definition.Label;

    public string RangeLabel => Definition.Kind == FurryParameterKind.Number
        ? $"{Definition.Minimum:0.##} – {Definition.Maximum:0.##}"
        : "Ativo / inativo";

    public bool IsBoolean => Definition.Kind == FurryParameterKind.Boolean;

    public double Minimum => Definition.Minimum ?? 0;

    public double Maximum => Definition.Maximum ?? 1;

    public double NumberValue
    {
        get => _numberValue;
        set
        {
            if (Math.Abs(_numberValue - value) > 0.000001)
            {
                _setNumber?.Invoke(this, value);
            }
        }
    }

    public bool BooleanValue
    {
        get => _booleanValue;
        set
        {
            if (_booleanValue != value)
            {
                _setBoolean?.Invoke(this, value);
            }
        }
    }

    public void SetNumberValue(double value)
    {
        _numberValue = value;
        OnPropertyChanged(nameof(NumberValue));
    }

    public void SetBooleanValue(bool value)
    {
        _booleanValue = value;
        OnPropertyChanged(nameof(BooleanValue));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
