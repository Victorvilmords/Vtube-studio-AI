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
                MessageBoxResult.No) == MessageBoxResult.Yes);
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
