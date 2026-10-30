using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VTuberStudioAI.App.Preview;
using VTuberStudioAI.App.Projects;
using VTuberStudioAI.App.Tracking;

namespace VTuberStudioAI.App.ViewModels;

/// <summary>Laboratório de movimento: câmera + detecção facial controlando o VTuber, com calibração.</summary>
public sealed class MotionLabViewModel : ObservableObject, IDisposable
{
    private const int MaxFrameWidth = 640;
    private const int LostFramesBeforeNeutral = 8;
    private const double CalibrationSeconds = 1.2;

    private readonly IProjectRepository _repository;
    private readonly ICameraSource _camera;
    private readonly Func<string, IFaceTracker> _trackerFactory;
    private readonly Action<Action> _post;
    private readonly Action<Exception, string> _log;
    private readonly object _trackerLock = new();
    private readonly PoseSmoother _smoother = new();
    private IFaceTracker? _tracker;
    private int _processing;
    private int _lostFrames;
    private Guid? _projectId;
    private FaceTrackingSettings _settings = FaceTrackingSettings.Default;
    private bool _isRunning;
    private bool _isBusy;
    private bool _faceDetected;
    private bool _isDirty;
    private List<FaceMeasurements>? _calibrationSamples;
    private Stopwatch? _calibrationClock;
    private CameraDevice? _selectedCamera;
    private FaceTrackerInfo _selectedTool;
    private BitmapSource? _cameraImage;
    private AvatarPose? _pose;
    private string _status = "Escolha uma câmera e toque em Ligar câmera.";
    private string _errorMessage = string.Empty;
    private string _settingsMessage = string.Empty;

    public MotionLabViewModel(
        IProjectRepository repository,
        ICameraSource? camera = null,
        Func<string, IFaceTracker>? trackerFactory = null,
        Action<Action>? post = null,
        Action<Exception, string>? log = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _camera = camera ?? new CameraService();
        _trackerFactory = trackerFactory ?? CreateCatalogTracker;
        _post = post ?? PostToDispatcher;
        _log = log ?? AppLogger.Log;
        _selectedTool = FaceTrackerCatalog.Available[0];
        _camera.FrameArrived += OnFrame;

        RefreshCamerasCommand = new RelayCommand(_ => RunSafe(RefreshCamerasAsync));
        ToggleCameraCommand = new RelayCommand(_ => RunSafe(IsRunning ? StopAsync : StartAsync));
        CalibrateCommand = new RelayCommand(_ => BeginCalibration());
        SaveCommand = new RelayCommand(_ => Save());
        ResetCommand = new RelayCommand(_ => ResetSettings());
    }

    public IReadOnlyList<FaceTrackerInfo> Tools => FaceTrackerCatalog.Available;

    public ObservableCollection<CameraDevice> Cameras { get; } = [];

    public ICommand RefreshCamerasCommand { get; }

    public ICommand ToggleCameraCommand { get; }

    public ICommand CalibrateCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand ResetCommand { get; }

    public bool HasProject => _projectId is not null;

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(CanChangeSetup));
                OnPropertyChanged(nameof(CameraButtonLabel));
                OnPropertyChanged(nameof(CanCalibrate));
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanChangeSetup));
            }
        }
    }

    public bool CanChangeSetup => !IsRunning && !IsBusy;

    public bool CanCalibrate => IsRunning && FaceDetected && _calibrationSamples is null;

    public string CameraButtonLabel => IsRunning ? "Desligar câmera" : "Ligar câmera";

    public FaceTrackerInfo SelectedTool
    {
        get => _selectedTool;
        set
        {
            if (value is not null && SetProperty(ref _selectedTool, value))
            {
                UpdateSettings(_settings with { ToolId = value.Id });
            }
        }
    }

    public string ToolDescription => _selectedTool.Description;

    public CameraDevice? SelectedCamera
    {
        get => _selectedCamera;
        set => SetProperty(ref _selectedCamera, value);
    }

    public BitmapSource? CameraImage
    {
        get => _cameraImage;
        private set => SetProperty(ref _cameraImage, value);
    }

    /// <summary>Pose do avatar; nulo enquanto não há câmera ligada (o preview volta ao comportamento normal).</summary>
    public AvatarPose? Pose
    {
        get => _pose;
        private set
        {
            if (SetProperty(ref _pose, value))
            {
                OnPropertyChanged(nameof(HeadTurnMeter));
                OnPropertyChanged(nameof(HeadTiltMeter));
                OnPropertyChanged(nameof(MouthMeter));
                OnPropertyChanged(nameof(LeftEyeMeter));
                OnPropertyChanged(nameof(RightEyeMeter));
            }
        }
    }

    public bool FaceDetected
    {
        get => _faceDetected;
        private set
        {
            if (SetProperty(ref _faceDetected, value))
            {
                OnPropertyChanged(nameof(CanCalibrate));
            }
        }
    }

    // Medidores 0..1 mostram exatamente o que está sendo enviado ao VTuber.
    public double HeadTurnMeter => (Pose?.GazeX ?? 0) / 2 + 0.5;

    public double HeadTiltMeter => (Pose?.GazeY ?? 0) / 2 + 0.5;

    public double MouthMeter => Pose?.MouthOpen ?? 0;

    public double LeftEyeMeter => Pose?.ScreenLeftEyeClosed ?? 0;

    public double RightEyeMeter => Pose?.ScreenRightEyeClosed ?? 0;

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public string SettingsMessage
    {
        get => _settingsMessage;
        private set => SetProperty(ref _settingsMessage, value);
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set => SetProperty(ref _isDirty, value);
    }

    public bool Mirror
    {
        get => _settings.Mirror;
        set => UpdateSettings(_settings with { Mirror = value });
    }

    public double HeadGain
    {
        get => _settings.HeadGain;
        set => UpdateSettings(_settings with { HeadGain = Clamp(value, 0.2, 3, _settings.HeadGain) });
    }

    public double MouthGain
    {
        get => _settings.MouthGain;
        set => UpdateSettings(_settings with { MouthGain = Clamp(value, 0.2, 3, _settings.MouthGain) });
    }

    public double IrisGain
    {
        get => _settings.IrisGain;
        set => UpdateSettings(_settings with { IrisGain = Clamp(value, 0, 3, _settings.IrisGain) });
    }

    public double BrowGain
    {
        get => _settings.BrowGain;
        set => UpdateSettings(_settings with { BrowGain = Clamp(value, 0, 3, _settings.BrowGain) });
    }

    public string BrowStatus => _settings.BrowsCalibrated
        ? "Sobrancelhas calibradas."
        : "Sobrancelhas paradas: use “Calibrar rosto neutro” com a expressão relaxada para ativá-las.";

    public double Smoothing
    {
        get => _settings.Smoothing;
        set => UpdateSettings(_settings with { Smoothing = Clamp(value, 0, 1, _settings.Smoothing) });
    }

    /// <summary>Chamado quando o VTuber ativo muda; carrega os ajustes salvos dele.</summary>
    public void SetProject(ProjectDocument? project)
    {
        _projectId = project?.Id;
        _settings = project is null ? FaceTrackingSettings.Default : TrackingSettingsStore.Read(project);
        _selectedTool = FaceTrackerCatalog.Available.FirstOrDefault(tool => tool.Id == _settings.ToolId)
            ?? FaceTrackerCatalog.Available[0];
        IsDirty = false;
        SettingsMessage = string.Empty;
        OnPropertyChanged(string.Empty);
    }

    public async Task RefreshCamerasAsync()
    {
        try
        {
            var devices = await _camera.ListAsync();
            var previous = SelectedCamera?.Id;
            Cameras.Clear();
            foreach (var device in devices)
            {
                Cameras.Add(device);
            }

            SelectedCamera = Cameras.FirstOrDefault(device => device.Id == previous) ?? Cameras.FirstOrDefault();
            if (Cameras.Count == 0)
            {
                ErrorMessage = "Nenhuma câmera foi encontrada. Conecte uma webcam e toque em Atualizar.";
            }
            else if (ErrorMessage.StartsWith("Nenhuma câmera", StringComparison.Ordinal))
            {
                ErrorMessage = string.Empty;
            }
        }
        catch (CameraException exception)
        {
            _log(exception, "listar câmeras");
            ErrorMessage = exception.Message;
        }
    }

    public async Task StartAsync()
    {
        if (IsRunning || IsBusy)
        {
            return;
        }

        ErrorMessage = string.Empty;
        if (SelectedCamera is null)
        {
            ErrorMessage = "Escolha uma câmera antes de ligar.";
            return;
        }

        if (_selectedTool.CheckAvailability() is { } unavailable)
        {
            ErrorMessage = unavailable;
            return;
        }

        IsBusy = true;
        Status = "Carregando o detector facial…";
        IFaceTracker tracker;
        try
        {
            tracker = await Task.Run(() => _trackerFactory(_selectedTool.Id));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _log(exception, "carregar o detector facial");
            ErrorMessage = "Não foi possível carregar o detector facial. Detalhes foram salvos no registro do aplicativo.";
            Status = "Câmera desligada.";
            IsBusy = false;
            return;
        }

        lock (_trackerLock)
        {
            _tracker = tracker;
        }

        _smoother.Reset();
        _lostFrames = 0;
        try
        {
            await _camera.StartAsync(SelectedCamera.Id);
        }
        catch (CameraException exception)
        {
            _log(exception, "ligar a câmera");
            ErrorMessage = exception.Message;
            Status = "Câmera desligada.";
            ReleaseTracker();
            IsBusy = false;
            return;
        }

        IsRunning = true;
        IsBusy = false;
        Status = "Procurando seu rosto…";
    }

    public async Task StopAsync()
    {
        if (!IsRunning && _tracker is null)
        {
            return;
        }

        IsRunning = false;
        try
        {
            await _camera.StopAsync();
        }
        catch (CameraException exception)
        {
            _log(exception, "desligar a câmera");
        }

        ReleaseTracker();
        _calibrationSamples = null;
        _calibrationClock = null;
        FaceDetected = false;
        CameraImage = null;
        Pose = null;
        Status = "Câmera desligada.";
        OnPropertyChanged(nameof(CanCalibrate));
    }

    /// <summary>Desliga a câmera sem esperar (usado ao sair da aba ou fechar o app).</summary>
    public void Stop() => RunSafe(StopAsync);

    public void Dispose()
    {
        _camera.FrameArrived -= OnFrame;
        _camera.Dispose();
        ReleaseTracker();
    }

    private void BeginCalibration()
    {
        if (!CanCalibrate)
        {
            return;
        }

        SettingsMessage = string.Empty;
        _calibrationSamples = [];
        _calibrationClock = Stopwatch.StartNew();
        Status = "Calibrando: olhe para a tela, olhos abertos e boca fechada…";
        OnPropertyChanged(nameof(CanCalibrate));
    }

    private void FinishCalibration()
    {
        var samples = _calibrationSamples ?? [];
        _calibrationSamples = null;
        _calibrationClock = null;
        var result = FaceCalibrationBuilder.Build(samples, _settings);
        if (result.Settings is { } calibrated)
        {
            UpdateSettings(calibrated);
            _smoother.Reset();
            SettingsMessage = "Rosto neutro calibrado. Toque em Salvar ajustes para guardar no VTuber.";
        }
        else
        {
            SettingsMessage = result.Error ?? "A calibração falhou.";
        }

        OnPropertyChanged(nameof(CanCalibrate));
    }

    private void Save()
    {
        if (_projectId is not { } id)
        {
            SettingsMessage = "Escolha um VTuber para salvar os ajustes.";
            return;
        }

        try
        {
            TrackingSettingsStore.Save(_repository, id, _settings);
            IsDirty = false;
            SettingsMessage = "Ajustes de rastreamento salvos.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
            or InvalidOperationException or InvalidDataException)
        {
            _log(exception, "salvar ajustes de rastreamento");
            SettingsMessage = "Não foi possível salvar os ajustes. Detalhes foram salvos no registro do aplicativo.";
        }
    }

    private void ResetSettings()
    {
        _selectedTool = FaceTrackerCatalog.Available.First(tool => tool.Id == FaceTrackingSettings.Default.ToolId);
        UpdateSettings(FaceTrackingSettings.Default);
        _smoother.Reset();
        SettingsMessage = "Ajustes padrão restaurados. Salve para manter.";
    }

    private void UpdateSettings(FaceTrackingSettings settings)
    {
        if (settings.Equals(_settings) || !settings.IsValid)
        {
            return;
        }

        _settings = settings;
        IsDirty = true;
        OnPropertyChanged(nameof(Mirror));
        OnPropertyChanged(nameof(HeadGain));
        OnPropertyChanged(nameof(MouthGain));
        OnPropertyChanged(nameof(IrisGain));
        OnPropertyChanged(nameof(BrowGain));
        OnPropertyChanged(nameof(BrowStatus));
        OnPropertyChanged(nameof(Smoothing));
        OnPropertyChanged(nameof(SelectedTool));
        OnPropertyChanged(nameof(ToolDescription));
    }

    // Thread de fundo da câmera: descarta quadros enquanto o anterior ainda está sendo processado.
    private void OnFrame(BgraFrame frame)
    {
        if (Interlocked.Exchange(ref _processing, 1) == 1)
        {
            return;
        }

        try
        {
            var small = frame.Downscale(MaxFrameWidth);
            FaceMeasurements? measurements;
            lock (_trackerLock)
            {
                if (_tracker is null)
                {
                    return;
                }

                measurements = _tracker.Track(small);
            }

            BitmapSource? image = null;
            if (small.IsValid)
            {
                image = BitmapSource.Create(
                    small.Width, small.Height, 96, 96, PixelFormats.Bgr32, null, small.Pixels, small.Width * 4);
                image.Freeze();
            }

            _post(() => Apply(image, measurements));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _log(exception, "processar quadro da câmera");
            _post(() => ReportFrameFailure(exception));
        }
        finally
        {
            Volatile.Write(ref _processing, 0);
        }
    }

    private void ReportFrameFailure(Exception exception)
    {
        if (!IsRunning)
        {
            return;
        }

        ErrorMessage = "O rastreamento falhou e foi desligado. Detalhes foram salvos no registro do aplicativo.";
        Stop();
    }

    internal void Apply(BitmapSource? image, FaceMeasurements? measurements)
    {
        if (!IsRunning)
        {
            return;
        }

        CameraImage = image ?? CameraImage;
        if (measurements is null)
        {
            _lostFrames++;
            if (_lostFrames >= LostFramesBeforeNeutral)
            {
                FaceDetected = false;
                Status = "Não encontrei seu rosto. Fique de frente para a câmera com boa iluminação.";
                Pose = _smoother.Apply(AvatarPose.Neutral, _settings.Smoothing);
            }

            return;
        }

        _lostFrames = 0;
        FaceDetected = true;
        if (_calibrationSamples is { } samples && _calibrationClock is { } clock)
        {
            samples.Add(measurements);
            if (clock.Elapsed.TotalSeconds >= CalibrationSeconds)
            {
                FinishCalibration();
            }
        }

        if (_calibrationSamples is null)
        {
            Status = "Rosto detectado: o VTuber está seguindo você.";
        }

        Pose = _smoother.Apply(FaceMotionMapper.Map(measurements, _settings), _settings.Smoothing);
    }

    private void ReleaseTracker()
    {
        lock (_trackerLock)
        {
            _tracker?.Dispose();
            _tracker = null;
        }
    }

    private void RunSafe(Func<Task> action)
    {
        _ = RunSafeAsync(action);
    }

    private async Task RunSafeAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _log(exception, "laboratório de movimento");
            ErrorMessage = "Algo deu errado no laboratório de movimento. Detalhes foram salvos no registro do aplicativo.";
            IsBusy = false;
        }
    }

    private static double Clamp(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    private static IFaceTracker CreateCatalogTracker(string id) =>
        FaceTrackerCatalog.Available.First(tool => tool.Id == id).Create();

    private static void PostToDispatcher(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }
}
