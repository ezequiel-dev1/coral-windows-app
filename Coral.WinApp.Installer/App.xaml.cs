using System.Windows;
using System.Windows.Threading;
using System.IO;

namespace Coral.WinApp.Installer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        try
        {
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            ShowStartupFailure(ex);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        ShowStartupFailure(e.Exception);
    }

    private void ShowStartupFailure(Exception exception)
    {
        var logPath = Path.Combine(Path.GetTempPath(), "CoralInstaller.log");
        try { File.AppendAllText(logPath, $"{DateTimeOffset.Now:O}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}"); }
        catch { /* Preserve the original startup error even if logging fails. */ }
        MessageBox.Show($"No se pudo iniciar el instalador de Coral. Los detalles se guardaron en:{Environment.NewLine}{logPath}{Environment.NewLine}{Environment.NewLine}{exception.Message}", "Instalador de Coral", MessageBoxButton.OK, MessageBoxImage.Error);
        Shutdown(1);
    }
}
