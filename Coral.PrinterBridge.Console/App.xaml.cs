using System.Windows;
using Coral.PrinterBridge.Console.Bridge;

namespace Coral.PrinterBridge.Console;

public partial class App : Application
{
    private readonly BridgeHost _bridge = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try { await _bridge.StartAsync(); }
        catch (Exception ex)
        {
            MessageBox.Show($"Coral Printer Bridge could not start. {ex.Message}", "Coral Printer Bridge", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }
        if (!e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase)) new MainWindow(_bridge).Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _bridge.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.OnExit(e);
    }
}
