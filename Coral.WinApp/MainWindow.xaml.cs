using System.Windows;
using System.ComponentModel;
using Coral.WinApp.Bridge;
using Coral.WinApp.Updates;

namespace Coral.WinApp;

public partial class MainWindow : Window
{
    private readonly BridgeHost _bridge;

    public MainWindow(BridgeHost bridge)
    {
        _bridge = bridge;
        InitializeComponent();
        DataContext = new ViewModels.MainViewModel(bridge);
        Loaded += async (_, _) =>
        {
            if (DataContext is ViewModels.MainViewModel viewModel) await viewModel.RefreshAsync();
        };
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (App.IsExiting) return;
        e.Cancel = true;
        Hide();
    }

    private bool _isInstallingUpdate;

    private async void InstallLatest_Click(object sender, RoutedEventArgs e) => await InstallLatestAsync();

    internal async Task InstallLatestAsync()
    {
        if (_isInstallingUpdate) return;

        _isInstallingUpdate = true;
        InstallLatestMenuItem.IsEnabled = false;
        InstallLatestMenuItem.Header = "Descargando actualización…";
        try
        {
            var update = await new UpdateService(_bridge.UpdateOptions).GetLatestAsync();
            await new UpdateService(_bridge.UpdateOptions).StartUpdateAsync(update);
            App.ShutdownForUpdate();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Coral no pudo descargar la versión más reciente. {ex.Message}", "Actualizaciones de Coral", MessageBoxButton.OK, MessageBoxImage.Warning);
            InstallLatestMenuItem.Header = "Instalar la versión más reciente";
            InstallLatestMenuItem.IsEnabled = true;
            _isInstallingUpdate = false;
        }
    }

    private void HideToTray_Click(object sender, RoutedEventArgs e) => Hide();

    private void Exit_Click(object sender, RoutedEventArgs e) => App.ExitFromUserAction();

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized) Hide();
    }

}
