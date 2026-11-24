using System.Windows;
using Coral.PrinterBridge.Console.Bridge;

namespace Coral.PrinterBridge.Console;

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
