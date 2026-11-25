using System.Windows;
using System;

namespace Coral.WinApp.Installer;

public partial class MainWindow : Window
{
    private readonly int? _waitForProcessId;

    public MainWindow()
    {
        InitializeComponent();
        SettingsPathText.Text = InstallerService.SettingsPath;
        _waitForProcessId = ParseWaitForProcessId(Environment.GetCommandLineArgs());
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        WelcomePanel.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Visible;
        InstallButton.Visibility = Visibility.Collapsed;
        CloseButton.IsEnabled = false;
        var progress = new Progress<string>(message => ProgressText.Text = message);
        try
        {
            await InstallerService.InstallAsync(progress, _waitForProcessId);
            ProgressPanel.Visibility = Visibility.Collapsed;
            CompletePanel.Visibility = Visibility.Visible;
            LaunchButton.Visibility = Visibility.Visible;
            CloseButton.IsEnabled = true;
            CloseButton.Content = "Finish";
        }
        catch (Exception ex)
        {
            ProgressPanel.Visibility = Visibility.Collapsed;
            WelcomePanel.Visibility = Visibility.Visible;
            InstallButton.Visibility = Visibility.Visible;
            CloseButton.IsEnabled = true;
            MessageBox.Show($"No se pudo instalar Coral. {ex.Message}", "Error de instalación", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        InstallerService.LaunchCoral();
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private static int? ParseWaitForProcessId(IReadOnlyList<string> arguments)
    {
        for (var index = 0; index + 1 < arguments.Count; index++)
            if (string.Equals(arguments[index], "--wait-for-pid", StringComparison.OrdinalIgnoreCase) && int.TryParse(arguments[index + 1], out var processId) && processId > 0) return processId;
        return null;
    }
}
