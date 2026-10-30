using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace VTuberStudioAI.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        base.OnStartup(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLogger.Log(e.Exception, "Exceção não tratada na interface.");
        MessageBox.Show(
            "Ocorreu um erro inesperado. Os detalhes foram registrados no log local.",
            "VTuber Studio AI",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            AppLogger.Log(exception, "Exceção não tratada no processo.");
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLogger.Log(e.Exception, "Exceção não observada em uma tarefa.");
        e.SetObserved();
    }
}

internal static class AppLogger
{
    public static void Log(Exception exception, string context)
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VTuberStudioAI",
            "logs",
            $"app-{DateTime.Now:yyyy-MM-dd}.log");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.AppendAllText(
                logPath,
                $"[{DateTimeOffset.Now:O}] {context}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException loggingException)
        {
            Trace.WriteLine($"Falha ao gravar log: {loggingException}");
        }
        catch (UnauthorizedAccessException loggingException)
        {
            Trace.WriteLine($"Sem permissão para gravar log: {loggingException}");
        }
    }
}
