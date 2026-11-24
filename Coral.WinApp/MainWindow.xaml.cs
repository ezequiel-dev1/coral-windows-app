using System.Windows;
using Coral.WinApp.Bridge;

namespace Coral.WinApp;

public partial class MainWindow : Window
{
    public MainWindow(BridgeHost bridge)
    {
        InitializeComponent();
        DataContext = new ViewModels.MainViewModel(bridge);
        Loaded += async (_, _) =>
        {
            if (DataContext is ViewModels.MainViewModel viewModel) await viewModel.RefreshAsync();
        };
    }
}
