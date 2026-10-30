using System.Windows;
using System.Windows.Input;

namespace VTuberStudioAI.App;

/// <summary>Janela limpa para exibir só o VTuber; o botão X aparece e some com duplo clique.</summary>
public partial class PlayWindow : Window
{
    public PlayWindow()
    {
        InitializeComponent();
    }

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && !Controls.IsMouseOver)
        {
            Controls.Visibility = Controls.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
            e.Handled = true;
        }
    }

    private void OnMovePressed(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            e.Handled = true;
            DragMove();
        }
    }

    private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Controls.Visibility == Visibility.Visible)
        {
            Close();
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
