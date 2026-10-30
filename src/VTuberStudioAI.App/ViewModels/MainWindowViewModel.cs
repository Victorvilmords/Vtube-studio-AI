using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.IO;
using System.Windows.Media.Imaging;
using VTuberStudioAI.App.Projects;
using VTuberStudioAI.App.Images;
using VTuberStudioAI.App.Furry;
using VTuberStudioAI.App.Preview;

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
        Func<FurryComponent, bool>? confirmRemoveFurryComponent = null)
    {
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
            new NavigationItemViewModel("motion", "Laboratório de movimento", "\uE768"),
            new NavigationItemViewModel("library", "Biblioteca de projetos", "\uE8F1"),
            new NavigationItemViewModel("integrations", "Integrações", "\uE8D7"),
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
                OnPropertyChanged(nameof(PreviewHint));
            }
        }
    }

    public bool HasEyeRegions => EyeRegions.Count > 0;

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
            if (SetProperty(ref _isMarkingEyes, value && HasCurrentImage))
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
            : HasEyeRegions
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
        else if (key == "create")
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
            "Bem-vindo ao VTuber Studio AI — um espaço local para organizar e preparar personagens virtuais, com atenção especial a personagens furry.",
            "FUNDAÇÃO LOCAL",
            "Uma base feita para evoluir com você",
            "O VTuber Studio AI está começando pelo essencial: uma experiência desktop local, clara e preparada para ferramentas criativas.",
            ["A navegação do estúdio já está organizada.", "Projetos e recursos avançados serão adicionados em etapas verificáveis.", "Seus arquivos não são enviados a serviços externos." ]),
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
        "motion" => Roadmap("motion", "Laboratório de movimento", "Explore a evolução futura de comportamentos e rastreamento.", "ROADMAP", "Movimento e rastreamento", "Rastreamento facial, captura corporal e física procedural ainda não estão implementados.", ["Não há câmera ativa nem dados simulados nesta tela.", "Parâmetros futuros só serão chamados de animação quando aplicados por um motor real."]),
        "library" => new("library", "Biblioteca de projetos", "Crie, abra e organize projetos armazenados neste computador.", "PROJETOS LOCAIS", "Biblioteca de projetos", "Os projetos são armazenados localmente em JSON versionado.", []),
        "integrations" => Roadmap("integrations", "Integrações", "Conectividade opcional com ferramentas externas.", "ROADMAP", "Integrações futuras", "Integrações com OBS, plugins e serviços externos estão fora desta fundação.", ["Nenhuma conexão externa é iniciada.", "Serviços pagos e chaves de API não são necessários."]),
        "settings" => Roadmap("settings", "Configurações", "Preferências do aplicativo e diagnóstico local.", "ROADMAP", "Preferências do aplicativo", "Persistência de preferências será adicionada junto com configurações reais do produto.", ["A interface usa tema escuro nesta versão.", "Logs de erros, quando ocorrerem, ficam na pasta local do usuário."]),
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

public sealed class NavigationItemViewModel(string key, string label, string icon) : ObservableObject
{
    private bool _isSelected;

    public string Key { get; } = key;
    public string Label { get; } = label;
    public string Icon { get; } = icon;

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
