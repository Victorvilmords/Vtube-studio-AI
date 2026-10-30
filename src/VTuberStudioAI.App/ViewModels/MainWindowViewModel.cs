using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using System.IO;
using System.Windows.Media.Imaging;
using VTuberStudioAI.App.Projects;
using VTuberStudioAI.App.Images;
using VTuberStudioAI.App.Furry;
using VTuberStudioAI.App.Preview;
using VTuberStudioAI.App.Themes;

namespace VTuberStudioAI.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly IProjectRepository _projectRepository;
    private readonly Func<ProjectDocument, bool> _confirmDelete;
    private readonly Action<Exception, string> _logException;
    private readonly IImageImportService _imageImportService;
    private readonly Func<string?> _selectImageFile;
    private readonly FurryStudioService _furryStudioService;
    private readonly EyePreviewService _eyePreviewService;
    private IReadOnlyList<EyeRegion> _eyeRegions = [];
    private bool _isMarkingEyes;
    private bool _followMouse;
    private IReadOnlyList<PsdLayerInfo> _psdLayers = [];
    private Dictionary<string, BitmapSource>? _layerBitmaps;
    private LayerScene? _previewScene;
    private string _rigSummary = string.Empty;
    private IReadOnlyList<EyeRegion> _sceneEyes = [];
    private RigSettings _previewRig = RigSettings.Default;
    private readonly Action? _openPlayWindow;
    private readonly ThemeService? _themeService;
    private readonly Action? _requestRestart;
    private readonly string _startupThemeId;
    private string _themeMessage = string.Empty;
    private SceneryOptions _scenery = SceneryOptions.Default;
    private string _sceneryColorText = SceneryOptions.Default.Color;
    private string _sceneryColorError = string.Empty;
    private bool _isRigDirty;
    private bool _loadingRig;
    private bool _isPoseTesting;
    private double _testYaw;
    private double _testPitch;
    private readonly Func<FurryComponent, bool> _confirmRemoveFurryComponent;
    private NavigationPageViewModel _currentPage;
    private ProjectDocument? _selectedProject;
    private ProjectDocument? _activeProject;
    private string _newProjectName = string.Empty;
    private string _renameProjectName = string.Empty;
    private string _errorMessage = string.Empty;
    private string _operationMessage = string.Empty;
    private ProjectDocument? _importTargetProject;
    private ValidatedImage? _pendingImage;
    private BitmapSource? _previewImage;
    private BitmapSource? _currentImage;
    private string? _currentImageReference;
    private string _currentImageName = string.Empty;
    private int _currentImageWidth;
    private int _currentImageHeight;
    private double _imageZoom = 1;
    private FurryComponentViewModel? _selectedFurryComponent;
    private FurryComponentTypeOption? _selectedFurryComponentType;
    private string _newFurryComponentName = string.Empty;
    private string _selectedFurryComponentName = string.Empty;

    public MainWindowViewModel()
        : this(new ProjectRepository(), _ => false)
    {
    }

    public MainWindowViewModel(
        IProjectRepository projectRepository,
        Func<ProjectDocument, bool> confirmDelete,
        Action<Exception, string>? logException = null,
        IImageImportService? imageImportService = null,
        Func<string?>? selectImageFile = null,
        FurryStudioService? furryStudioService = null,
        Func<FurryComponent, bool>? confirmRemoveFurryComponent = null,
        Action? openPlayWindow = null,
        MotionLabViewModel? motionLab = null,
        ThemeService? themeService = null,
        Action? requestRestart = null)
    {
        _themeService = themeService;
        _requestRestart = requestRestart;
        _startupThemeId = themeService?.Current.Id ?? AppThemes.Default.Id;
        ThemeOptions = new ObservableCollection<ThemeOptionViewModel>(AppThemes.All.Select(theme => new ThemeOptionViewModel(theme)));
        RefreshThemeSelection();
        SelectThemeCommand = new RelayCommand(parameter =>
        {
            if (parameter is not string themeId || _themeService is null)
            {
                return;
            }

            var error = _themeService.Select(themeId);
            RefreshThemeSelection();
            if (error is not null)
            {
                ThemeMessage = error;
            }
            else if (_themeService.Current.Id == _startupThemeId)
            {
                ThemeMessage = string.Empty;
            }
            else
            {
                ThemeMessage = "Tema salvo. Ele é aplicado quando o aplicativo for reiniciado.";
                _requestRestart?.Invoke();
            }
        });
        _openPlayWindow = openPlayWindow;
        MotionLab = motionLab ?? new MotionLabViewModel(projectRepository);
        _projectRepository = projectRepository ?? throw new ArgumentNullException(nameof(projectRepository));
        _confirmDelete = confirmDelete ?? throw new ArgumentNullException(nameof(confirmDelete));
        _logException = logException ?? AppLogger.Log;
        _imageImportService = imageImportService ?? new ImageImportService(_projectRepository);
        _selectImageFile = selectImageFile ?? (() => null);
        _furryStudioService = furryStudioService ?? new FurryStudioService(_projectRepository);
        _confirmRemoveFurryComponent = confirmRemoveFurryComponent ?? (_ => false);
        _eyePreviewService = new EyePreviewService(_projectRepository);
        ToggleMarkEyesCommand = new RelayCommand(_ => IsMarkingEyes = !IsMarkingEyes);
        ToggleFollowMouseCommand = new RelayCommand(_ => FollowMouse = !FollowMouse);
        SaveRigCommand = new RelayCommand(_ => RunProjectOperation(SaveRig));
        ResetRigCommand = new RelayCommand(_ => ResetRig());
        SetSceneryColorCommand = new RelayCommand(parameter =>
        {
            if (parameter is string hex)
            {
                SceneryColorText = hex;
            }
        });
        StartPlayCommand = new RelayCommand(_ => StartPlay());
        SetSceneryModeCommand = new RelayCommand(parameter =>
        {
            SceneryIsTransparent = parameter as string == "transparent";
            SceneryIsSolid = parameter as string == "solid";
        });
        MarkEyeCommand = new RelayCommand(parameter =>
        {
            if (parameter is EyeRegion eye)
            {
                RunProjectOperation(() => MarkEye(eye));
            }
        });
        ClearEyesCommand = new RelayCommand(_ => RunProjectOperation(ClearEyes));
        NavigationItems =
        [
            new NavigationItemViewModel("home", "Início", "\uE80F"),
            new NavigationItemViewModel("studio", "Meu estúdio", "\uE8A5"),
            new NavigationItemViewModel("create", "Criar VTuber", "\uE710"),
            new NavigationItemViewModel("furry", "Furry Studio", "\uE8D2"),
            new NavigationItemViewModel("preview", "Preview", "\uE7B3"),
            new NavigationItemViewModel("motion", "Laboratório de movimento", "\uE722"),
            new NavigationItemViewModel("play", "Play", "\uE768"),
            new NavigationItemViewModel("library", "Biblioteca de projetos", "\uE8F1"),
            new NavigationItemViewModel("integrations", "Integrações", "\uE8D7", "EM BREVE"),
            new NavigationItemViewModel("themes", "Temas", "\uE790"),
            new NavigationItemViewModel("settings", "Configurações", "\uE713")
        ];

        NavigateCommand = new RelayCommand(parameter => Navigate(parameter switch
        {
            NavigationItemViewModel item => item.Key,
            string key => key,
            _ => null
        }));
        NavigateToStudioCommand = new RelayCommand(_ => Navigate("studio"));
        NavigateToLibraryCommand = new RelayCommand(_ => Navigate("library"));
        NavigateToCreateCommand = new RelayCommand(_ => Navigate("create"));
        OpenRecentProjectCommand = new RelayCommand(parameter =>
        {
            if (parameter is ProjectDocument project)
            {
                SelectedProject = project;
                RunProjectOperation(OpenSelectedProject);
            }
        });
        CreateProjectCommand = new RelayCommand(_ => RunProjectOperation(CreateProject));
        OpenSelectedProjectCommand = new RelayCommand(_ => RunProjectOperation(OpenSelectedProject));
        RenameSelectedProjectCommand = new RelayCommand(_ => RunProjectOperation(RenameSelectedProject));
        DuplicateSelectedProjectCommand = new RelayCommand(_ => RunProjectOperation(DuplicateSelectedProject));
        SaveActiveProjectCommand = new RelayCommand(_ => RunProjectOperation(SaveActiveProject));
        DeleteSelectedProjectCommand = new RelayCommand(_ => RunProjectOperation(DeleteSelectedProject));
        ChooseImageFileCommand = new RelayCommand(_ => ChooseImageFile());
        ConfirmImageImportCommand = new RelayCommand(_ => RunProjectOperation(ConfirmImageImport));
        CancelImagePreviewCommand = new RelayCommand(_ => CancelImagePreview());
        ZoomInCommand = new RelayCommand(_ => ChangeZoom(1.2));
        ZoomOutCommand = new RelayCommand(_ => ChangeZoom(1 / 1.2));
        FitImageCommand = new RelayCommand(_ => ImageZoom = 1);
        RestoreImageFramingCommand = new RelayCommand(_ => ImageZoom = 1);
        RemoveImageReferenceCommand = new RelayCommand(_ => RunProjectOperation(RemoveImageReference));
        AddFurryComponentCommand = new RelayCommand(_ => RunProjectOperation(AddFurryComponent));
        RenameFurryComponentCommand = new RelayCommand(_ => RunProjectOperation(RenameFurryComponent));
        RemoveFurryComponentCommand = new RelayCommand(_ => RunProjectOperation(RemoveFurryComponent));
        AddFurryPresetCommand = new RelayCommand(parameter =>
        {
            if (parameter is string presetKey)
            {
                RunProjectOperation(() => AddFurryPreset(presetKey));
            }
        });

        _currentPage = CreatePage("home");
        SelectNavigationItem("home");
        Projects = new ObservableCollection<ProjectDocument>();
        RecentProjects = new ObservableCollection<ProjectDocument>();
        FurryComponents = new ObservableCollection<FurryComponentViewModel>();
        FurryParameters = new ObservableCollection<FurryParameterViewModel>();
        FurryComponentTypes = new ObservableCollection<FurryComponentTypeOption>(FurryComponentCatalog.Types);
        SelectedFurryComponentType = FurryComponentTypes.FirstOrDefault();
        RefreshProjectsAtStartup();
    }

    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }
    public ObservableCollection<ThemeOptionViewModel> ThemeOptions { get; }
    public ICommand SelectThemeCommand { get; }

    public string ThemeMessage
    {
        get => _themeMessage;
        private set => SetProperty(ref _themeMessage, value);
    }

    public string VersionText => $"Early access · v{AppInfo.Version}";

    private void RefreshThemeSelection()
    {
        var currentId = _themeService?.Current.Id ?? AppThemes.Default.Id;
        foreach (var option in ThemeOptions)
        {
            option.IsCurrent = option.Id == currentId;
        }
    }

    public ICommand ToggleMarkEyesCommand { get; }

    public ICommand ToggleFollowMouseCommand { get; }

    public ICommand MarkEyeCommand { get; }

    public ICommand ClearEyesCommand { get; }

    public IReadOnlyList<EyeRegion> EyeRegions
    {
        get => _eyeRegions;
        private set
        {
            if (SetProperty(ref _eyeRegions, value))
            {
                OnPropertyChanged(nameof(HasEyeRegions));
                OnPropertyChanged(nameof(PreviewEyes));
                OnPropertyChanged(nameof(PreviewHint));
            }
        }
    }

    public bool HasEyeRegions => EyeRegions.Count > 0;

    public MotionLabViewModel MotionLab { get; }

    public ObservableCollection<LayerItemViewModel> LayerItems { get; } = [];

    public LayerScene? PreviewScene
    {
        get => _previewScene;
        private set
        {
            _sceneEyes = value?.EyeRegions ?? [];
            if (SetProperty(ref _previewScene, value))
            {
                OnPropertyChanged(nameof(HasRiggedEyes));
                OnPropertyChanged(nameof(HasLayerScene));
                OnPropertyChanged(nameof(CanMarkEyes));
                OnPropertyChanged(nameof(PreviewEyes));
                OnPropertyChanged(nameof(PreviewHint));
            }
        }
    }

    public string RigSummary
    {
        get => _rigSummary;
        private set => SetProperty(ref _rigSummary, value);
    }

    public bool HasLayerScene => PreviewScene is not null;

    public RigSettings PreviewRig
    {
        get => _previewRig;
        private set => SetProperty(ref _previewRig, value);
    }

    public bool RigEnabled
    {
        get => _previewRig.Enabled;
        set => UpdateRig(_previewRig with { Enabled = value }, nameof(RigEnabled));
    }

    public double RigOverall
    {
        get => _previewRig.Overall;
        set => UpdateRig(_previewRig with { Overall = ClampIntensity(value) }, nameof(RigOverall));
    }

    public double RigFace
    {
        get => _previewRig.Face;
        set => UpdateRig(_previewRig with { Face = ClampIntensity(value) }, nameof(RigFace));
    }

    public double RigEars
    {
        get => _previewRig.Ears;
        set => UpdateRig(_previewRig with { Ears = ClampIntensity(value) }, nameof(RigEars));
    }

    public double RigHair
    {
        get => _previewRig.Hair;
        set => UpdateRig(_previewRig with { Hair = ClampIntensity(value) }, nameof(RigHair));
    }

    public double RigHead
    {
        get => _previewRig.Head;
        set => UpdateRig(_previewRig with { Head = ClampIntensity(value) }, nameof(RigHead));
    }

    public double RigEyeLook
    {
        get => _previewRig.EyeLook;
        set => UpdateRig(_previewRig with { EyeLook = ClampIntensity(value) }, nameof(RigEyeLook));
    }

    public double RigBody
    {
        get => _previewRig.Body;
        set => UpdateRig(_previewRig with { Body = ClampIntensity(value) }, nameof(RigBody));
    }

    public bool RigCloseMouth
    {
        get => _previewRig.CloseMouth;
        set => UpdateRig(_previewRig with { CloseMouth = value }, nameof(RigCloseMouth));
    }

    public bool IsRigDirty
    {
        get => _isRigDirty;
        private set
        {
            if (SetProperty(ref _isRigDirty, value))
            {
                OnPropertyChanged(nameof(RigStatus));
            }
        }
    }

    public string RigStatus => IsRigDirty
        ? "Calibração com alterações não salvas."
        : "Calibração salva no projeto (ou padrão, se nunca alterada).";

    public bool IsPoseTesting
    {
        get => _isPoseTesting;
        set => SetProperty(ref _isPoseTesting, value && HasLayerScene);
    }

    public double TestYaw
    {
        get => _testYaw;
        set => SetProperty(ref _testYaw, Math.Clamp(double.IsFinite(value) ? value : 0, -1, 1));
    }

    public double TestPitch
    {
        get => _testPitch;
        set => SetProperty(ref _testPitch, Math.Clamp(double.IsFinite(value) ? value : 0, -1, 1));
    }

    public ICommand SaveRigCommand { get; private set; } = null!;

    public ICommand SetSceneryColorCommand { get; private set; } = null!;

    public ICommand StartPlayCommand { get; private set; } = null!;

    public ICommand SetSceneryModeCommand { get; private set; } = null!;

    public IReadOnlyList<SceneryPreset> SceneryPresets => SceneryOptions.Presets;

    public SceneryOptions Scenery => _scenery;

    public Brush PlayBackground => _scenery.ToBrush();

    public bool SceneryIsTransparent
    {
        get => _scenery.Mode == SceneryMode.Transparent;
        set
        {
            if (value)
            {
                SetScenery(_scenery with { Mode = SceneryMode.Transparent });
            }
        }
    }

    public bool SceneryIsSolid
    {
        get => _scenery.Mode == SceneryMode.Solid;
        set
        {
            if (value)
            {
                SetScenery(_scenery with { Mode = SceneryMode.Solid });
            }
        }
    }

    public string SceneryColorText
    {
        get => _sceneryColorText;
        set
        {
            var text = (value ?? string.Empty).Trim();
            if (SetProperty(ref _sceneryColorText, text))
            {
                if (SceneryOptions.TryParseColor(text, out _))
                {
                    SceneryColorError = string.Empty;
                    SetScenery(_scenery with { Color = text.ToUpperInvariant() });
                }
                else
                {
                    SceneryColorError = "Use o formato #RRGGBB, por exemplo #00B140.";
                }
            }
        }
    }

    public string SceneryColorError
    {
        get => _sceneryColorError;
        private set => SetProperty(ref _sceneryColorError, value);
    }

    public bool CanStartPlay => HasCurrentImage && _openPlayWindow is not null;

    public string PlayStatus => !HasCurrentImage
        ? "Escolha um VTuber com imagem importada para começar."
        : "Abre uma janela só com o seu VTuber. Clique com o botão direito e arraste para mover; duplo clique mostra ou esconde o X de fechar.";

    private void SetScenery(SceneryOptions options)
    {
        if (options == _scenery)
        {
            return;
        }

        _scenery = options;
        OnPropertyChanged(nameof(Scenery));
        OnPropertyChanged(nameof(PlayBackground));
        OnPropertyChanged(nameof(SceneryIsTransparent));
        OnPropertyChanged(nameof(SceneryIsSolid));
        OnPropertyChanged(nameof(SceneryNote));
    }

    public string SceneryNote => _scenery.Mode == SceneryMode.Transparent
        ? "Transparente: só o VTuber aparece, sem fundo. Cliques em áreas totalmente vazias do desenho podem passar para a janela de trás."
        : "Cor sólida: ideal para chroma key (verde ou azul) no OBS.";

    private void ApplyScenery(SceneryOptions options)
    {
        _scenery = options;
        _sceneryColorText = options.Color;
        SceneryColorError = string.Empty;
        foreach (var name in new[]
        {
            nameof(SceneryColorText), nameof(Scenery), nameof(PlayBackground),
            nameof(SceneryIsTransparent), nameof(SceneryIsSolid), nameof(SceneryNote)
        })
        {
            OnPropertyChanged(name);
        }
    }

    private void StartPlay()
    {
        if (!HasCurrentImage || _openPlayWindow is null)
        {
            ErrorMessage = "Escolha um VTuber com imagem importada antes de começar.";
            return;
        }

        if (!string.IsNullOrEmpty(SceneryColorError))
        {
            ErrorMessage = "Corrija a cor do cenário antes de começar.";
            return;
        }

        if (ActiveProject is not null)
        {
            RunProjectOperation(() =>
            {
                SceneryStore.Save(_projectRepository, ActiveProject.Id, _scenery);
                ActiveProject = _projectRepository.Load(ActiveProject.Id);
            });
        }

        ErrorMessage = string.Empty;
        _openPlayWindow();
        IsPlaying = true;
        _ = StartPlayCameraAsync();
    }

    /// <summary>Enquanto o Play está aberto a câmera não é desligada ao trocar de aba.</summary>
    public bool IsPlaying { get; private set; }

    public void EndPlay()
    {
        IsPlaying = false;
        if (CurrentPage?.Key != "motion")
        {
            MotionLab.Stop();
        }
    }

    private async Task StartPlayCameraAsync()
    {
        try
        {
            if (MotionLab.IsRunning)
            {
                return;
            }

            if (MotionLab.Cameras.Count == 0)
            {
                await MotionLab.RefreshCamerasAsync();
            }

            await MotionLab.StartAsync();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ReportError("ligar a câmera do Play", exception);
        }
    }

    public ICommand ResetRigCommand { get; private set; } = null!;

    private static double ClampIntensity(double value) =>
        Math.Clamp(double.IsFinite(value) ? value : 1, 0, RigSettings.MaxIntensity);

    private void UpdateRig(RigSettings settings, string changed)
    {
        if (settings == _previewRig)
        {
            return;
        }

        PreviewRig = settings;
        OnPropertyChanged(changed);
        IsRigDirty = !_loadingRig;
    }

    private void ApplyRig(RigSettings settings)
    {
        _loadingRig = true;
        try
        {
            UpdateRig(settings, nameof(RigEnabled));
            foreach (var name in new[] { nameof(RigOverall), nameof(RigFace), nameof(RigEars), nameof(RigHair), nameof(RigHead), nameof(RigEyeLook), nameof(RigBody), nameof(RigCloseMouth) })
            {
                OnPropertyChanged(name);
            }

            RaiseLayerModifierChanged();
        }
        finally
        {
            _loadingRig = false;
        }

        IsRigDirty = false;
    }

    private void SaveRig()
    {
        if (ActiveProject is null || !HasLayerScene)
        {
            throw new InvalidOperationException("Abra um projeto com camadas reconhecidas para salvar a calibração.");
        }

        RigSettingsStore.Save(_projectRepository, ActiveProject.Id, _previewRig);
        ActiveProject = _projectRepository.Load(ActiveProject.Id);
        IsRigDirty = false;
        ErrorMessage = string.Empty;
        OperationMessage = "Calibração do rig salva no projeto.";
    }

    private void ResetRig()
    {
        UpdateRig(RigSettings.Default, nameof(RigEnabled));
        foreach (var name in new[] { nameof(RigOverall), nameof(RigFace), nameof(RigEars), nameof(RigHair), nameof(RigHead), nameof(RigEyeLook), nameof(RigBody), nameof(RigCloseMouth) })
        {
            OnPropertyChanged(name);
        }

        IsRigDirty = true;
        RaiseLayerModifierChanged();
        OperationMessage = "Valores padrão restaurados. Salve para gravar no projeto.";
        ErrorMessage = string.Empty;
    }

    /// <summary>Quando há camadas eyewhite, elas definem os olhos e a marcação manual fica desnecessária.</summary>
    public bool HasRiggedEyes => _sceneEyes.Count > 0;

    public bool CanMarkEyes => HasCurrentImage && !HasRiggedEyes;

    public IReadOnlyList<EyeRegion> PreviewEyes => HasRiggedEyes ? _sceneEyes : EyeRegions;

    public IReadOnlyList<PsdLayerInfo> PsdLayers
    {
        get => _psdLayers;
        private set
        {
            if (SetProperty(ref _psdLayers, value))
            {
                OnPropertyChanged(nameof(HasPsdLayers));
                OnPropertyChanged(nameof(PsdLayerLabels));
            }
        }
    }

    public bool HasPsdLayers => PsdLayers.Count > 0;

    public IReadOnlyList<string> PsdLayerLabels =>
        PsdLayers.Select(layer => layer.Visible ? layer.Name : $"{layer.Name} (oculta)").ToList();

    public bool IsMarkingEyes
    {
        get => _isMarkingEyes;
        set
        {
            if (SetProperty(ref _isMarkingEyes, value && CanMarkEyes))
            {
                OnPropertyChanged(nameof(MarkEyesButtonLabel));
                OnPropertyChanged(nameof(PreviewHint));
            }
        }
    }

    public string MarkEyesButtonLabel => IsMarkingEyes ? "Concluir marcação" : "Marcar olhos";

    public bool FollowMouse
    {
        get => _followMouse;
        set
        {
            if (SetProperty(ref _followMouse, value))
            {
                OnPropertyChanged(nameof(FollowMouseButtonLabel));
                OnPropertyChanged(nameof(PreviewHint));
            }
        }
    }

    public string FollowMouseButtonLabel => FollowMouse ? "Parar de seguir o mouse" : "Seguir o mouse";

    public string PreviewHint => !HasCurrentImage
        ? "Escolha um VTuber que tenha uma imagem importada em Criar VTuber."
        : IsMarkingEyes
            ? $"Arraste sobre cada olho para marcá-lo ({EyeRegions.Count}/{EyePreviewService.MaxEyes}). Depois clique em Concluir marcação."
            : HasEyeRegions || HasRiggedEyes
                ? FollowMouse
                    ? "Seguindo o mouse. Clique num olho para piscar; dê dois cliques para parar."
                    : "Clique num olho para piscar. Dê dois cliques (ou use o botão) para a cabeça e os olhos seguirem o mouse."
                : "Marque os olhos para habilitar o piscar. Dois cliques ligam o seguir o mouse.";

    public ICommand NavigateCommand { get; }

    public ICommand NavigateToStudioCommand { get; }

    public ICommand NavigateToLibraryCommand { get; }

    public ICommand NavigateToCreateCommand { get; }

    public ICommand OpenRecentProjectCommand { get; }

    public ICommand CreateProjectCommand { get; }

    public ICommand OpenSelectedProjectCommand { get; }

    public ICommand RenameSelectedProjectCommand { get; }

    public ICommand DuplicateSelectedProjectCommand { get; }

    public ICommand SaveActiveProjectCommand { get; }

    public ICommand DeleteSelectedProjectCommand { get; }

    public ICommand ChooseImageFileCommand { get; }

    public ICommand ConfirmImageImportCommand { get; }

    public ICommand CancelImagePreviewCommand { get; }

    public ICommand ZoomInCommand { get; }

    public ICommand ZoomOutCommand { get; }

    public ICommand FitImageCommand { get; }

    public ICommand RestoreImageFramingCommand { get; }

    public ICommand RemoveImageReferenceCommand { get; }

    public ICommand AddFurryComponentCommand { get; }

    public ICommand RenameFurryComponentCommand { get; }

    public ICommand RemoveFurryComponentCommand { get; }

    public ICommand AddFurryPresetCommand { get; }

    public NavigationPageViewModel CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    public ObservableCollection<ProjectDocument> Projects { get; }

    public ObservableCollection<ProjectDocument> RecentProjects { get; }

    public ObservableCollection<FurryComponentViewModel> FurryComponents { get; }

    public ObservableCollection<FurryParameterViewModel> FurryParameters { get; }

    public ObservableCollection<FurryComponentTypeOption> FurryComponentTypes { get; }

    public bool HasProjects => Projects.Count > 0;

    public bool HasSelectedProject => SelectedProject is not null;

    public bool HasActiveProject => ActiveProject is not null;

    public bool HasImportTarget => ImportTargetProject is not null;

    public bool HasPendingImage => _pendingImage is not null;

    public bool HasCurrentImage => CurrentImage is not null;

    public bool HasDisplayImage => DisplayImage is not null;

    public bool HasFurryComponents => FurryComponents.Count > 0;

    public ObservableCollection<DetectedPart> DetectedParts { get; } = [];

    public bool HasDetectedParts => DetectedParts.Count > 0;

    private DetectedPart? _selectedDetectedPart;

    public DetectedPart? SelectedDetectedPart
    {
        get => _selectedDetectedPart;
        set
        {
            if (SetProperty(ref _selectedDetectedPart, value))
            {
                OnPropertyChanged(nameof(HasSelectedDetectedPart));
                RaiseLayerModifierChanged();
            }
        }
    }

    public bool HasSelectedDetectedPart => SelectedDetectedPart is not null;

    private LayerModifier SelectedLayerModifier =>
        SelectedDetectedPart is null ? LayerModifier.Default : _previewRig.ModifierFor(SelectedDetectedPart.LayerName);

    public double LayerScale
    {
        get => SelectedLayerModifier.Scale;
        set => UpdateLayerModifier(SelectedLayerModifier with
        {
            Scale = Math.Clamp(double.IsFinite(value) ? value : 1, LayerModifier.MinScale, LayerModifier.MaxScale)
        });
    }

    public double LayerOffsetX
    {
        get => SelectedLayerModifier.OffsetX;
        set => UpdateLayerModifier(SelectedLayerModifier with { OffsetX = ClampOffset(value) });
    }

    public double LayerOffsetY
    {
        get => SelectedLayerModifier.OffsetY;
        set => UpdateLayerModifier(SelectedLayerModifier with { OffsetY = ClampOffset(value) });
    }

    public double LayerIntensity
    {
        get => SelectedLayerModifier.Intensity;
        set => UpdateLayerModifier(SelectedLayerModifier with { Intensity = ClampIntensity(value) });
    }

    private static double ClampOffset(double value) =>
        Math.Clamp(double.IsFinite(value) ? value : 0, -LayerModifier.MaxOffset, LayerModifier.MaxOffset);

    private void UpdateLayerModifier(LayerModifier modifier)
    {
        if (SelectedDetectedPart is null)
        {
            return;
        }

        var modifiers = new Dictionary<string, LayerModifier>(
            _previewRig.LayerModifiers ?? [], StringComparer.Ordinal);
        if (modifier.IsDefault)
        {
            modifiers.Remove(SelectedDetectedPart.LayerName);
        }
        else
        {
            modifiers[SelectedDetectedPart.LayerName] = modifier;
        }

        UpdateRig(_previewRig with { LayerModifiers = modifiers.Count == 0 ? null : modifiers }, nameof(RigEnabled));
        RaiseLayerModifierChanged();
    }

    private void RaiseLayerModifierChanged()
    {
        OnPropertyChanged(nameof(LayerScale));
        OnPropertyChanged(nameof(LayerOffsetX));
        OnPropertyChanged(nameof(LayerOffsetY));
        OnPropertyChanged(nameof(LayerIntensity));
    }

    private string _characterDetectionSummary = string.Empty;

    public string CharacterDetectionSummary
    {
        get => _characterDetectionSummary;
        private set => SetProperty(ref _characterDetectionSummary, value);
    }

    public bool HasSelectedFurryComponent => SelectedFurryComponent is not null;

    public bool CanConfirmImageImport => HasPendingImage && HasImportTarget;
    public string StatusMessage => "Pronto · Base local";

    public string ProjectStatus => ActiveProject is null ? "Nenhum projeto aberto" : ActiveProject.Name;

    public bool CanManageProjects => true;

    public bool CanImportCharacter => true;

    public string ProjectActionsUnavailableReason =>
        "Importação local: PNG ou JPEG/JPG (até 25 MiB) ou PSD RGB de 8 bits com camadas (até 500 MiB, 100 camadas), com no máximo 12.000 × 12.000 px. WebP não é suportado.";

    public string ImageImportAvailabilityMessage => ProjectActionsUnavailableReason;

    public string RecentProjectsStatus => HasProjects
        ? $"{Projects.Count} projeto(s) na biblioteca local."
        : "Nenhum projeto criado ainda. Crie um projeto para começar.";

    public string ImportTargetStatus => Projects.Count == 0
        ? "Nenhum personagem ainda. Comece do zero ao lado; depois você poderá importar imagens para ele."
        : ImportTargetProject is null
            ? "Selecione um projeto existente antes de confirmar a importação."
            : $"A imagem será associada a “{ImportTargetProject.Name}”.";

    public string NewProjectName
    {
        get => _newProjectName;
        set => SetProperty(ref _newProjectName, value);
    }

    public string RenameProjectName
    {
        get => _renameProjectName;
        set => SetProperty(ref _renameProjectName, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public string OperationMessage
    {
        get => _operationMessage;
        private set => SetProperty(ref _operationMessage, value);
    }

    public ProjectDocument? SelectedProject
    {
        get => _selectedProject;
        set
        {
            if (SetProperty(ref _selectedProject, value))
            {
                RenameProjectName = value?.Name ?? string.Empty;
                OnPropertyChanged(nameof(HasSelectedProject));
            }
        }
    }

    public ProjectDocument? ActiveProject
    {
        get => _activeProject;
        private set
        {
            var projectChanged = _activeProject?.Id != value?.Id;
            if (SetProperty(ref _activeProject, value))
            {
                OnPropertyChanged(nameof(ProjectStatus));
                OnPropertyChanged(nameof(HasActiveProject));
                OnPropertyChanged(nameof(HasImportTarget));
                OnPropertyChanged(nameof(CanConfirmImageImport));
                OnPropertyChanged(nameof(FurryProjectStatus));
                if (value is not null && ImportTargetProject is null)
                {
                    ImportTargetProject = Projects.FirstOrDefault(project => project.Id == value.Id);
                }

                if (projectChanged)
                {
                    RefreshFurryComponents();
                }
            }
        }
    }

    public ProjectDocument? ImportTargetProject
    {
        get => _importTargetProject;
        set
        {
            if (SetProperty(ref _importTargetProject, value))
            {
                OnPropertyChanged(nameof(HasImportTarget));
                OnPropertyChanged(nameof(CanConfirmImageImport));
                OnPropertyChanged(nameof(ImportTargetStatus));
                if (value is not null && ActiveProject?.Id != value.Id)
                {
                    try
                    {
                        ActiveProject = _projectRepository.Load(value.Id);
                        LoadCurrentImage(ActiveProject);
                        ErrorMessage = string.Empty;
                    }
                    catch (Exception exception) when (IsProjectOperationException(exception))
                    {
                        ReportError("abrir o projeto selecionado", exception);
                    }
                }
            }
        }
    }

    public BitmapSource? PreviewImage
    {
        get => _previewImage;
        private set
        {
            if (SetProperty(ref _previewImage, value))
            {
                OnPropertyChanged(nameof(DisplayImage));
                OnPropertyChanged(nameof(HasPendingImage));
                OnPropertyChanged(nameof(HasDisplayImage));
                OnPropertyChanged(nameof(CanConfirmImageImport));
            }
        }
    }

    public BitmapSource? CurrentImage
    {
        get => _currentImage;
        private set
        {
            if (SetProperty(ref _currentImage, value))
            {
                OnPropertyChanged(nameof(DisplayImage));
                OnPropertyChanged(nameof(HasCurrentImage));
                OnPropertyChanged(nameof(CanStartPlay));
                OnPropertyChanged(nameof(PlayStatus));
                OnPropertyChanged(nameof(CanMarkEyes));
                OnPropertyChanged(nameof(PreviewHint));
                OnPropertyChanged(nameof(HasDisplayImage));
            }
        }
    }

    public BitmapSource? DisplayImage => PreviewImage ?? CurrentImage;

    public string CurrentImageName
    {
        get => _pendingImage?.FileName ?? _currentImageName;
        private set => SetProperty(ref _currentImageName, value);
    }

    public string CurrentImageDimensions => _pendingImage is not null
        ? $"{_pendingImage.Width} × {_pendingImage.Height} px"
        : _currentImageWidth > 0
            ? $"{_currentImageWidth} × {_currentImageHeight} px"
            : string.Empty;

    public double ImageZoom
    {
        get => _imageZoom;
        private set
        {
            if (SetProperty(ref _imageZoom, Math.Clamp(value, 0.1, 5)))
            {
                OnPropertyChanged(nameof(ImageZoomLabel));
            }
        }
    }

    public string ImageZoomLabel => $"{ImageZoom:P0}";

    public string FurryProjectStatus => ActiveProject is null
        ? "Nenhum personagem aberto. Comece do zero para criar um avatar sem alterar outros projetos."
        : $"Editando “{ActiveProject.Name}”.";

    public string FurryScopeNotice =>
        "Estas são configurações persistentes do personagem. Não aplicam deformação, rigging, física ou animação à imagem.";

    public string CreateCharacterStatus =>
        "Comece com um personagem vazio ou escolha um projeto para importar uma imagem. Criar do zero não altera nenhum avatar existente.";

    public string NewFurryComponentName
    {
        get => _newFurryComponentName;
        set => SetProperty(ref _newFurryComponentName, value);
    }

    public string SelectedFurryComponentName
    {
        get => _selectedFurryComponentName;
        set => SetProperty(ref _selectedFurryComponentName, value);
    }

    public FurryComponentViewModel? SelectedFurryComponent
    {
        get => _selectedFurryComponent;
        set
        {
            if (SetProperty(ref _selectedFurryComponent, value))
            {
                OnPropertyChanged(nameof(HasSelectedFurryComponent));
                SelectedFurryComponentName = value?.Component.Name ?? string.Empty;
                RefreshFurryParameters(value?.Component);
            }
        }
    }

    public FurryComponentTypeOption? SelectedFurryComponentType
    {
        get => _selectedFurryComponentType;
        set => SetProperty(ref _selectedFurryComponentType, value);
    }

    private void Navigate(string? key)
    {
        if (key is null || !NavigationItems.Any(item => item.Key == key))
        {
            return;
        }

        if (key == "library")
        {
            var selectedId = SelectedProject?.Id;
            RunProjectOperation(() => RefreshProjects(selectedId));
        }
        else if (key is "create" or "play" or "motion")
        {
            try
            {
                RefreshProjects(ImportTargetProject?.Id ?? ActiveProject?.Id);
                ImportTargetProject = ActiveProject is null
                    ? ImportTargetProject
                    : Projects.FirstOrDefault(project => project.Id == ActiveProject.Id);
            }
            catch (Exception exception) when (IsProjectOperationException(exception))
            {
                ReportError("carregar os projetos para importação", exception);
            }
        }
        else if (key == "furry")
        {
            try
            {
                RefreshProjects(ActiveProject?.Id);
                if (ActiveProject is not null)
                {
                    ImportTargetProject = Projects.FirstOrDefault(project => project.Id == ActiveProject.Id);
                }

                RefreshFurryComponents();
            }
            catch (Exception exception) when (IsProjectOperationException(exception))
            {
                ReportError("carregar os componentes furry", exception);
            }
        }

        CurrentPage = CreatePage(key);
        SelectNavigationItem(key);
        if (key == "motion")
        {
            _ = MotionLab.RefreshCamerasAsync();
        }
        else if (!IsPlaying)
        {
            MotionLab.Stop();
        }
    }

    private void SelectNavigationItem(string key)
    {
        foreach (var item in NavigationItems)
        {
            item.IsSelected = item.Key == key;
        }
    }

    private void RefreshProjectsAtStartup()
    {
        try
        {
            RefreshProjects();
            ErrorMessage = string.Empty;
        }
        catch (Exception exception) when (IsProjectOperationException(exception))
        {
            ReportError("carregar a biblioteca", exception);
        }
    }

    private void RefreshProjects(Guid? selectId = null)
    {
        var projects = _projectRepository.List();
        Projects.Clear();
        foreach (var project in projects)
        {
            Projects.Add(project);
        }

        RecentProjects.Clear();
        foreach (var project in projects.Take(3))
        {
            RecentProjects.Add(project);
        }

        OnPropertyChanged(nameof(HasProjects));
        OnPropertyChanged(nameof(RecentProjectsStatus));
        OnPropertyChanged(nameof(CanConfirmImageImport));
        OnPropertyChanged(nameof(ImportTargetStatus));
        OnPropertyChanged(nameof(HasFurryComponents));
        SelectedProject = selectId is null
            ? null
            : Projects.FirstOrDefault(project => project.Id == selectId.Value);
        if (ImportTargetProject is not null)
        {
            ImportTargetProject = Projects.FirstOrDefault(project => project.Id == ImportTargetProject.Id);
        }
    }

    private void CreateProject()
    {
        var project = _projectRepository.Create(NewProjectName);
        NewProjectName = string.Empty;
        RefreshProjects(project.Id);
        ActiveProject = project;
        ImportTargetProject = project;
        LoadCurrentImage(project);
        RefreshFurryComponents();
        OperationMessage = $"Personagem “{project.Name}” criado do zero. Agora você pode configurar os componentes no Furry Studio.";
        ErrorMessage = string.Empty;
        Navigate("furry");
    }

    private void OpenSelectedProject()
    {
        if (SelectedProject is null)
        {
            return;
        }

        ActiveProject = _projectRepository.Load(SelectedProject.Id);
        ImportTargetProject = Projects.FirstOrDefault(project => project.Id == ActiveProject.Id) ?? ActiveProject;
        LoadCurrentImage(ActiveProject);
        RefreshFurryComponents();
        RefreshProjects(ActiveProject.Id);
        OperationMessage = $"Projeto “{ActiveProject.Name}” aberto.";
        ErrorMessage = string.Empty;
        Navigate("studio");
    }

    private void RenameSelectedProject()
    {
        if (SelectedProject is null)
        {
            return;
        }

        var project = _projectRepository.Rename(SelectedProject.Id, RenameProjectName);
        RefreshProjects(project.Id);
        if (ActiveProject?.Id == project.Id)
        {
            ActiveProject = project;
            ImportTargetProject = project;
            LoadCurrentImage(project);
            RefreshFurryComponents();
        }

        OperationMessage = $"Projeto renomeado para “{project.Name}”.";
        ErrorMessage = string.Empty;
    }

    private void DuplicateSelectedProject()
    {
        if (SelectedProject is null)
        {
            return;
        }

        var project = _projectRepository.Duplicate(SelectedProject.Id);
        RefreshProjects(project.Id);
        OperationMessage = $"Projeto duplicado como “{project.Name}”.";
        ErrorMessage = string.Empty;
    }

    private void SaveActiveProject()
    {
        if (ActiveProject is null)
        {
            OperationMessage = "Abra um projeto antes de salvar.";
            return;
        }

        ActiveProject = _projectRepository.Save(ActiveProject);
        RefreshProjects(ActiveProject.Id);
        OperationMessage = $"Projeto “{ActiveProject.Name}” salvo.";
        ErrorMessage = string.Empty;
    }

    private void DeleteSelectedProject()
    {
        if (SelectedProject is null)
        {
            return;
        }

        var project = SelectedProject;
        if (!_confirmDelete(project))
        {
            OperationMessage = $"Exclusão de “{project.Name}” cancelada; o projeto foi preservado.";
            return;
        }

        _projectRepository.Delete(project.Id);
        if (ActiveProject?.Id == project.Id)
        {
            ActiveProject = null;
            ImportTargetProject = null;
            ClearCurrentImage();
            RefreshFurryComponents();
        }

        RefreshProjects();
        OperationMessage = $"Projeto “{project.Name}” excluído.";
        ErrorMessage = string.Empty;
    }

    private void RunProjectOperation(Action operation)
    {
        try
        {
            operation();
        }
        catch (Exception exception) when (IsProjectOperationException(exception))
        {
            ReportError("concluir a operação", exception);
        }
    }

    private void ReportError(string operation, Exception exception)
    {
        _logException(exception, $"Falha ao {operation}.");
        ErrorMessage = $"Não foi possível {operation}: {exception.Message}";
        OperationMessage = string.Empty;
    }

    private static bool IsProjectOperationException(Exception exception) =>
        exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            ArgumentException or
        AggregateException or
        InvalidOperationException or
        System.Security.SecurityException;

    private void ChooseImageFile()
    {
        try
        {
        var path = _selectImageFile();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        _pendingImage = null;
        PreviewImage = null;
        OnPropertyChanged(nameof(CurrentImageName));
        OnPropertyChanged(nameof(CurrentImageDimensions));
        var image = _imageImportService.Inspect(path);
        _pendingImage = image;
        PreviewImage = DecodeImage(image);
        ErrorMessage = string.Empty;
        OperationMessage = $"Prévia carregada: {image.FileName} · {image.Width} × {image.Height} px. Confirme para copiar para o projeto.";
        OnPropertyChanged(nameof(CurrentImageName));
        OnPropertyChanged(nameof(CurrentImageDimensions));
        OnPropertyChanged(nameof(CanConfirmImageImport));
        OnPropertyChanged(nameof(CanConfirmImageImport));
        }
        catch (Exception exception) when (IsProjectOperationException(exception))
        {
        ReportError("validar a imagem selecionada", exception);
        }
    }

    private void ConfirmImageImport()
    {
        if (_pendingImage is null)
        {
        throw new InvalidOperationException("Selecione uma imagem e confira a prévia antes de importar.");
        }

        var target = ImportTargetProject ?? ActiveProject;
        if (target is null)
        {
        throw new InvalidOperationException("Selecione um projeto ou crie um novo antes de confirmar a importação.");
        }

        var current = _projectRepository.Load(target.Id);
        var relativeReference = _imageImportService.Import(current, _pendingImage);
        ActiveProject = _projectRepository.Load(current.Id);
        ImportTargetProject = ActiveProject;
        RefreshProjects(ActiveProject.Id);
        LoadCurrentImage(ActiveProject, relativeReference);
        _pendingImage = null;
        PreviewImage = null;
        OnPropertyChanged(nameof(CurrentImageName));
        OnPropertyChanged(nameof(CurrentImageDimensions));
        OperationMessage = $"Imagem “{CurrentImageName}” copiada para o projeto. O arquivo original foi mantido.";
        ErrorMessage = string.Empty;
    }

    private void CancelImagePreview()
    {
        _pendingImage = null;
        PreviewImage = null;
        OnPropertyChanged(nameof(CurrentImageName));
        OnPropertyChanged(nameof(CurrentImageDimensions));
        OperationMessage = "Prévia cancelada. O projeto não foi alterado.";
        ErrorMessage = string.Empty;
    }

    private void AddFurryComponent()
    {
        EnsureActiveProject();
        if (SelectedFurryComponentType is null)
        {
            throw new InvalidOperationException("Selecione um tipo de componente.");
        }

        var componentName = string.IsNullOrWhiteSpace(NewFurryComponentName)
            ? SelectedFurryComponentType.Type == FurryComponentType.Custom
                ? throw new ArgumentException("Informe um nome para o componente personalizado.")
                : SelectedFurryComponentType.Label
            : NewFurryComponentName;
        var component = _furryStudioService.Add(ActiveProject!, SelectedFurryComponentType.Type, componentName);
        NewFurryComponentName = string.Empty;
        RefreshFurryComponents(component.Id);
        RefreshActiveProject();
        OperationMessage = $"Componente “{component.Name}” adicionado. Seus valores são configurações, não animações.";
        ErrorMessage = string.Empty;
    }

    private void RenameFurryComponent()
    {
        EnsureActiveProject();
        if (SelectedFurryComponent is null)
        {
            throw new InvalidOperationException("Selecione um componente furry para renomear.");
        }

        var component = _furryStudioService.Rename(
            ActiveProject!,
            SelectedFurryComponent.Id,
            SelectedFurryComponentName);
        RefreshFurryComponents(component.Id);
        RefreshActiveProject();
        OperationMessage = $"Componente renomeado para “{component.Name}”.";
        ErrorMessage = string.Empty;
    }

    private void RemoveFurryComponent()
    {
        EnsureActiveProject();
        if (SelectedFurryComponent is null)
        {
            return;
        }

        var selected = SelectedFurryComponent.Component;
        if (!_confirmRemoveFurryComponent(selected))
        {
            OperationMessage = "Remoção do componente cancelada. As configurações foram mantidas.";
            ErrorMessage = string.Empty;
            return;
        }

        var id = SelectedFurryComponent.Id;
        _furryStudioService.Remove(ActiveProject!, id);
        RefreshFurryComponents();
        RefreshActiveProject();
        OperationMessage = "Componente removido das configurações do projeto.";
        ErrorMessage = string.Empty;
    }

    private void AddFurryPreset(string presetKey)
    {
        EnsureActiveProject();
        var component = _furryStudioService.AddPreset(ActiveProject!, presetKey);
        RefreshFurryComponents(component.Id);
        RefreshActiveProject();
        OperationMessage = $"Configuração “{component.Name}” adicionada. Não é uma animação.";
        ErrorMessage = string.Empty;
    }

    private void SetFurryComponentEnabled(FurryComponentViewModel item, bool isEnabled)
    {
        try
        {
            EnsureActiveProject();
            var component = _furryStudioService.SetEnabled(ActiveProject!, item.Id, isEnabled);
            item.Update(component);
            RefreshActiveProject();
            OperationMessage = $"Componente “{component.Name}” {(isEnabled ? "ativado" : "desativado")} nas configurações.";
            ErrorMessage = string.Empty;
        }
        catch (Exception exception) when (IsProjectOperationException(exception))
        {
            ReportError("atualizar o estado do componente", exception);
        }
    }

    private void SetFurryNumberParameter(FurryParameterViewModel parameter, double value)
    {
        try
        {
            EnsureActiveProject();
            var component = _furryStudioService.SetNumberParameter(
                ActiveProject!,
                SelectedFurryComponent!.Id,
                parameter.Definition.Key,
                value);
            parameter.SetNumberValue(component.Parameters[parameter.Definition.Key].GetDouble());
            RefreshActiveProject();
            ErrorMessage = string.Empty;
        }
        catch (Exception exception) when (IsProjectOperationException(exception))
        {
            ReportError("salvar o parâmetro furry", exception);
        }
    }

    private void SetFurryBooleanParameter(FurryParameterViewModel parameter, bool value)
    {
        try
        {
            EnsureActiveProject();
            var component = _furryStudioService.SetBooleanParameter(
                ActiveProject!,
                SelectedFurryComponent!.Id,
                parameter.Definition.Key,
                value);
            parameter.SetBooleanValue(component.Parameters[parameter.Definition.Key].GetBoolean());
            RefreshActiveProject();
            ErrorMessage = string.Empty;
        }
        catch (Exception exception) when (IsProjectOperationException(exception))
        {
            ReportError("salvar a opção furry", exception);
        }
    }

    private void RefreshFurryComponents(Guid? selectedId = null)
    {
        IReadOnlyList<FurryComponent> components = ActiveProject is null
            ? []
            : _furryStudioService.Load(ActiveProject);
        FurryComponents.Clear();
        foreach (var component in components)
        {
            FurryComponents.Add(new FurryComponentViewModel(component, SetFurryComponentEnabled));
        }

        OnPropertyChanged(nameof(HasFurryComponents));
        SelectedFurryComponent = selectedId is null
            ? null
            : FurryComponents.FirstOrDefault(component => component.Id == selectedId.Value);
    }

    private void RefreshFurryParameters(FurryComponent? component)
    {
        FurryParameters.Clear();
        if (component is null)
        {
            return;
        }

        foreach (var definition in FurryComponentCatalog.GetParameters(component.Type))
        {
            var value = component.Parameters[definition.Key];
            var parameter = definition.Kind switch
            {
                FurryParameterKind.Number => new FurryParameterViewModel(
                    definition,
                    value.GetDouble(),
                    SetFurryNumberParameter),
                FurryParameterKind.Boolean => new FurryParameterViewModel(
                    definition,
                    value.GetBoolean(),
                    SetFurryBooleanParameter),
                _ => throw new InvalidDataException("Tipo de parâmetro furry inválido.")
            };
            FurryParameters.Add(parameter);
        }
    }

    private void RefreshActiveProject()
    {
        if (ActiveProject is null)
        {
            return;
        }

        ActiveProject = _projectRepository.Load(ActiveProject.Id);
        RefreshProjects(ActiveProject.Id);
        ImportTargetProject = Projects.FirstOrDefault(project => project.Id == ActiveProject.Id);
    }

    private void EnsureActiveProject()
    {
        if (ActiveProject is null)
        {
            throw new InvalidOperationException("Crie ou selecione um projeto antes de configurar componentes furry.");
        }
    }

    private void LoadCurrentImage(ProjectDocument project, string? preferredReference = null)
    {
        var imageReference = preferredReference ?? project.ResourceReferences
        .LastOrDefault(reference => Path.GetExtension(reference).Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                                    Path.GetExtension(reference).Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                    Path.GetExtension(reference).Equals(".jpeg", StringComparison.OrdinalIgnoreCase));
        if (imageReference is null)
        {
        ClearCurrentImage();
        return;
        }

        var image = _imageImportService.InspectResource(project, imageReference);
        CurrentImage = DecodeImage(image);
        _currentImageReference = imageReference;
        CurrentImageName = image.FileName;
        _currentImageWidth = image.Width;
        _currentImageHeight = image.Height;
        ImageZoom = 1;
        OnPropertyChanged(nameof(CurrentImageDimensions));
        IsMarkingEyes = false;
        EyeRegions = EyePreviewService.ReadEyes(project, imageReference);
        PsdLayers = PsdLayerStore.Read(project, imageReference);
        LoadLayerScene(project);
        ApplyRig(RigSettingsStore.Read(project));
        ApplyScenery(SceneryStore.Read(project));
        MotionLab.SetProject(project);
    }

    private void LoadLayerScene(ProjectDocument project)
    {
        _layerBitmaps = null;
        LayerItems.Clear();
        foreach (var layer in PsdLayers)
        {
            LayerItems.Add(new LayerItemViewModel(layer, OnLayerRenamed));
        }

        if (PsdLayers.Count > 0)
        {
            try
            {
                _layerBitmaps = LayerSceneLoader.LoadBitmaps(
                    PsdLayers,
                    reference => _projectRepository.GetResourcePath(project.Id, reference),
                    _currentImageWidth,
                    _currentImageHeight);
            }
            catch (Exception exception) when (IsProjectOperationException(exception))
            {
                ReportError("carregar as camadas para o preview", exception);
            }
        }

        RebuildScene();
    }

    private void RebuildScene()
    {
        RefreshDetectedParts();
        if (PsdLayers.Count == 0)
        {
            RigSummary = string.Empty;
            PreviewScene = null;
            return;
        }

        var plan = LayerRigger.Plan(PsdLayers);
        if (_layerBitmaps is null)
        {
            RigSummary = "O preview em camadas não está disponível (arquivos de camada ilegíveis ou PSD grande demais); usando a imagem composta.";
            PreviewScene = null;
            return;
        }

        RigSummary = LayerRigger.Summarize(plan);
        PreviewScene = plan.Any(entry => entry.Role.IsRecognized)
            ? new LayerScene(
                _currentImageWidth,
                _currentImageHeight,
                PsdLayers.Select((layer, index) =>
                    new SceneLayer(layer, plan[index], _layerBitmaps.GetValueOrDefault(layer.File))).ToList())
            : null;
    }

    private void RefreshDetectedParts()
    {
        var detection = FurryDetection.Analyze(PsdLayers);
        var previousName = SelectedDetectedPart?.LayerName;
        DetectedParts.Clear();
        foreach (var part in detection.Parts.Where(part => part.IsFurryPart))
        {
            DetectedParts.Add(part);
        }

        SelectedDetectedPart = DetectedParts.FirstOrDefault(part => part.LayerName == previousName);
        CharacterDetectionSummary = detection.Summary;
        OnPropertyChanged(nameof(HasDetectedParts));
    }

    private void OnLayerRenamed(LayerItemViewModel item, string name)
    {
        if (ActiveProject is null || _currentImageReference is null)
        {
            return;
        }

        var error = LayerNaming.Validate(name);
        if (error is not null)
        {
            ErrorMessage = error;
            OperationMessage = string.Empty;
            return;
        }

        RunProjectOperation(() =>
        {
            // Atualiza o campo (não a propriedade) para não recriar a lista enquanto o usuário digita.
            _psdLayers = PsdLayerStore.Rename(_projectRepository, ActiveProject.Id, _currentImageReference, item.File, name);
            ActiveProject = _projectRepository.Load(ActiveProject.Id);
            RebuildScene();
            ErrorMessage = string.Empty;
            OperationMessage = $"Camada renomeada para “{name.Trim()}” e salva no projeto.";
        });
    }

    private void MarkEye(EyeRegion eye)
    {
        if (ActiveProject is null || _currentImageReference is null)
        {
            throw new InvalidOperationException("Abra um projeto com uma imagem importada antes de marcar os olhos.");
        }

        EyeRegions = _eyePreviewService.AddEye(ActiveProject.Id, _currentImageReference, eye);
        ActiveProject = _projectRepository.Load(ActiveProject.Id);
        OperationMessage = "Olho marcado e salvo no projeto.";
        ErrorMessage = string.Empty;
    }

    private void ClearEyes()
    {
        if (ActiveProject is null)
        {
            return;
        }

        _eyePreviewService.ClearEyes(ActiveProject.Id);
        ActiveProject = _projectRepository.Load(ActiveProject.Id);
        EyeRegions = [];
        OperationMessage = "Marcações de olhos removidas. A imagem original não foi alterada.";
        ErrorMessage = string.Empty;
    }

    private void RemoveImageReference()
    {
        if (ActiveProject is null || _currentImageReference is null)
        {
        return;
        }

        var project = _projectRepository.Load(ActiveProject.Id);
        _imageImportService.RemoveReference(project, _currentImageReference);
        ActiveProject = _projectRepository.Load(project.Id);
        RefreshProjects(project.Id);
        ClearCurrentImage();
        OperationMessage = "A referência foi removida do projeto. O arquivo não foi apagado.";
        ErrorMessage = string.Empty;
    }

    private void ClearCurrentImage()
    {
        CurrentImage = null;
        _currentImageReference = null;
        CurrentImageName = string.Empty;
        _currentImageWidth = 0;
        _currentImageHeight = 0;
        ImageZoom = 1;
        OnPropertyChanged(nameof(CurrentImageDimensions));
        IsMarkingEyes = false;
        EyeRegions = [];
        PsdLayers = [];
        _layerBitmaps = null;
        LayerItems.Clear();
        RebuildScene();
        ApplyRig(RigSettings.Default);
        ApplyScenery(SceneryOptions.Default);
        MotionLab.SetProject(null);
        IsPoseTesting = false;
    }

    private void ChangeZoom(double multiplier)
    {
        if (DisplayImage is not null)
        {
        ImageZoom *= multiplier;
        }
    }

    private const long FullResolutionDisplayPixels = 40_000_000;
    private const int LargeImageDisplayWidth = 4096;

    // Imagens muito grandes são reduzidas só para exibição; o arquivo do projeto não muda.
    private static BitmapSource DecodeImage(ValidatedImage image)
    {
        using var stream = new MemoryStream(image.Bytes, writable: false);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        if ((long)image.Width * image.Height > FullResolutionDisplayPixels && image.Width >= image.Height)
        {
            bitmap.DecodePixelWidth = LargeImageDisplayWidth;
        }
        else if ((long)image.Width * image.Height > FullResolutionDisplayPixels)
        {
            bitmap.DecodePixelHeight = LargeImageDisplayWidth;
        }

        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private NavigationPageViewModel CreatePage(string key) => key switch
    {
        "home" => new(
            "home",
            "Início",
            "Bem-vindo ao VFtube AI — um espaço local para organizar e preparar personagens virtuais, com atenção especial a personagens furry.",
            "EARLY ACCESS",
            "Uma base feita para evoluir com você",
            "O VFtube AI está em early access: importe seu personagem, gere o rig, calibre o rosto com a câmera e use o Play.",
            ["Cada etapa tem sua aba: Criar, Furry Studio, Preview, Laboratório e Play.", "Recursos como Integrações chegam em atualizações futuras.", "Seus arquivos e a imagem da câmera não saem deste computador." ]),
        "studio" => new(
            "studio",
            "Meu estúdio",
            "Área de trabalho do seu personagem.",
            "ESPAÇO DE TRABALHO",
            ActiveProject is null ? "Nenhum projeto aberto" : ActiveProject.Name,
            "Gerencie projetos na Biblioteca, importe arte em Criar VTuber, configure componentes no Furry Studio e teste o resultado na aba Preview.",
            ["Cada etapa fica na sua própria aba, sem telas repetidas."]),
        "preview" => new(
            "preview",
            "Preview",
            "Escolha um VTuber e teste a interação com o mouse.",
            "PREVIEW",
            "Preview do VTuber",
            "Escolha o VTuber, clique nos olhos para piscar e dê dois cliques para a cabeça e os olhos seguirem o mouse.",
            []),
        "furry" => new(
            "furry",
            "Furry Studio",
            "Configure e salve a estrutura lógica de personagens furry no projeto local.",
            "CONFIGURAÇÕES LOCAIS",
            "Componentes do personagem",
            "Adicione componentes e ajuste somente os parâmetros disponíveis para cada tipo.",
            ["As configurações não deformam nem animam automaticamente uma imagem estática."]),
        "create" => new("create", "Criar VTuber", "Comece um personagem do zero ou importe uma imagem para um projeto existente.", "SEU PERSONAGEM", "Começar do zero ou importar uma imagem", "Criar do zero abre um projeto vazio no Furry Studio. Importar adiciona uma cópia local da imagem ao projeto selecionado.", ["Um personagem criado do zero não transforma nem substitui um avatar existente.", "Importar PNG ou JPEG não cria camadas, rigging ou animação. Em PSD, as camadas existentes são guardadas, mas nada é animado automaticamente."]),
        "motion" => new(
            "motion",
            "Laboratório de movimento",
            "Calibre o rastreamento facial com a câmera e veja o VTuber imitar você.",
            "RASTREAMENTO FACIAL",
            "Calibração facial",
            "A câmera e a detecção rodam neste computador; nenhuma imagem é enviada para fora.",
            []),
        "play" => new(
            "play",
            "Play",
            "Escolha o VTuber e o cenário e comece.",
            "PLAY",
            "Começar",
            "Abre uma janela limpa com o seu VTuber, pronta para transmissão.",
            []),
        "library" => new("library", "Biblioteca de projetos", "Crie, abra e organize projetos armazenados neste computador.", "PROJETOS LOCAIS", "Biblioteca de projetos", "Os projetos são armazenados localmente em JSON versionado.", []),
        "integrations" => Roadmap("integrations", "Integrações", "Conexão com OBS, plugins e serviços externos.", "EM BREVE", "Não disponível no early access", "As integrações ainda não existem nesta versão e chegam em uma atualização futura. Nada nesta área está ativo.", ["Nenhuma conexão externa é iniciada.", "Para transmitir hoje, capture a janela da aba Play (com cenário transparente ou colorido).", "Serviços pagos e chaves de API não são necessários."]),
        "themes" => new("themes", "Temas", "Aparência do aplicativo.", "APARÊNCIA", "Temas", "Escolha um tema.", []),
        "settings" => Roadmap("settings", "Configurações", "Informações do aplicativo e diagnóstico local.", "EARLY ACCESS", "Sobre esta versão", $"VFtube AI {AppInfo.Version}. Tudo roda neste computador.", ["O tema é escolhido na aba Temas.", $"Dados e preferências: {AppInfo.DataFolder}", $"Logs de erros, quando ocorrerem: {AppInfo.LogsFolder}"]),
        _ => CreatePage("home")
    };

    private static NavigationPageViewModel Roadmap(
        string key,
        string title,
        string description,
        string badge,
        string panelTitle,
        string panelDescription,
        string[] details) =>
        new(key, title, description, badge, panelTitle, panelDescription, details);
}

public sealed class NavigationItemViewModel(string key, string label, string icon, string? badge = null) : ObservableObject
{
    private bool _isSelected;

    public string Key { get; } = key;
    public string Label { get; } = label;
    public string Icon { get; } = icon;
    public string Badge { get; } = badge ?? string.Empty;
    public System.Windows.Visibility BadgeVisibility =>
        string.IsNullOrEmpty(Badge) ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed record NavigationPageViewModel(
    string Key,
    string Title,
    string Description,
    string Badge,
    string PanelTitle,
    string PanelDescription,
    IReadOnlyList<string> Details);

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
        {
            return false;
        }

        storage = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

public sealed class RelayCommand(Action<object?> execute) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => execute(parameter);
}
