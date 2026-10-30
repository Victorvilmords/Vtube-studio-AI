using System.Windows;
using Microsoft.Win32;
using VTuberStudioAI.App.Images;
using VTuberStudioAI.App.Projects;
using VTuberStudioAI.App.ViewModels;

namespace VTuberStudioAI.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closed += (_, _) =>
        {
            _playWindow?.Close();
            (DataContext as MainWindowViewModel)?.MotionLab.Dispose();
        };
        var projectRepository = new ProjectRepository();
        DataContext = new MainWindowViewModel(
            projectRepository,
            project => MessageBox.Show(
                this,
                $"Excluir “{project.Name}” da biblioteca? Os arquivos armazenados dentro da pasta do projeto serão removidos. Arquivos externos não serão apagados.",
                "Confirmar exclusão do projeto",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No) == MessageBoxResult.Yes,
            imageImportService: new ImageImportService(projectRepository),
            selectImageFile: SelectImageFile,
            confirmRemoveFurryComponent: component => MessageBox.Show(
                this,
                $"Remover “{component.Name}” das configurações do projeto? Os arquivos do projeto e imagens não serão apagados.",
                "Confirmar remoção do componente",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No) == MessageBoxResult.Yes,
            openPlayWindow: OpenPlayWindow,
            themeService: (Application.Current as App)?.Themes,
            requestRestart: RestartToApplyTheme);
    }

    private void RestartToApplyTheme()
    {
        var answer = MessageBox.Show(
            this,
            "O tema foi salvo e será aplicado ao reiniciar. Reiniciar agora? Alterações não salvas serão perdidas.",
            "Aplicar tema",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes || Environment.ProcessPath is not { } exe)
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
            Application.Current.Shutdown();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            AppLogger.Log(exception, "Falha ao reiniciar para aplicar o tema.");
            MessageBox.Show(this, "Não foi possível reiniciar. Feche e abra o aplicativo para aplicar o tema.", "Aplicar tema",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private PlayWindow? _playWindow;

    private void OpenPlayWindow()
    {
        if (_playWindow is { IsLoaded: true })
        {
            _playWindow.Activate();
            return;
        }

        _playWindow = new PlayWindow { DataContext = DataContext, Owner = null };
        _playWindow.Closed += (_, _) =>
        {
            _playWindow = null;
            (DataContext as MainWindowViewModel)?.EndPlay();
        };
        _playWindow.Show();
    }

    private string? SelectImageFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecionar imagem do personagem",
            Filter = "Imagens e PSD (*.png;*.jpg;*.jpeg;*.psd)|*.png;*.jpg;*.jpeg;*.psd|PNG (*.png)|*.png|JPEG (*.jpg;*.jpeg)|*.jpg;*.jpeg|PSD com camadas (*.psd)|*.psd",
            CheckFileExists = true,
            Multiselect = false,
            ValidateNames = true
        };

        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }
}
