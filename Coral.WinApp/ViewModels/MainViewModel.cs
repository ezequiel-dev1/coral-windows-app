using Coral.WinApp.Bridge;
using Coral.WinApp.Infrastructure;

namespace Coral.WinApp.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly BridgeHost _bridge;
    private string _state = "Starting bridge…", _detail = "Starting the local loopback print bridge.", _queueDepth = "0", _lastUpdated = "Not yet refreshed";
    private bool _isBusy;

    public MainViewModel(BridgeHost bridge)
    {
        _bridge = bridge;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy && _bridge.IsRunning);
        TestKitchenConnectionCommand = new AsyncRelayCommand(() => RunAsync("Kitchen", () => _bridge.TestConnectionAsync(PrinterTarget.Kitchen)), () => !IsBusy && _bridge.IsRunning);
        TestKitchenPrintCommand = new AsyncRelayCommand(() => RunAsync("Kitchen", () => _bridge.TestPrintAsync(PrinterTarget.Kitchen)), () => !IsBusy && _bridge.IsRunning);
        TestRegisterConnectionCommand = new AsyncRelayCommand(() => RunAsync("Register", () => _bridge.TestConnectionAsync(PrinterTarget.Register)), () => !IsBusy && _bridge.IsRunning);
        TestRegisterPrintCommand = new AsyncRelayCommand(() => RunAsync("Register", () => _bridge.TestPrintAsync(PrinterTarget.Register)), () => !IsBusy && _bridge.IsRunning);
    }

    public string State { get => _state; private set => SetProperty(ref _state, value); }
    public string Detail { get => _detail; private set => SetProperty(ref _detail, value); }
    public string QueueDepth { get => _queueDepth; private set => SetProperty(ref _queueDepth, value); }
    public string LastUpdated { get => _lastUpdated; private set => SetProperty(ref _lastUpdated, value); }
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) RaiseCommandState(); } }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand TestKitchenConnectionCommand { get; }
    public AsyncRelayCommand TestKitchenPrintCommand { get; }
    public AsyncRelayCommand TestRegisterConnectionCommand { get; }
    public AsyncRelayCommand TestRegisterPrintCommand { get; }

    public Task RefreshAsync()
    {
        State = _bridge.IsRunning ? "Healthy" : "Offline";
        Detail = _bridge.IsRunning ? "The local Coral Printer Bridge is running on 127.0.0.1:8765." : "The local print bridge could not start.";
        QueueDepth = "0"; LastUpdated = DateTimeOffset.Now.ToString("g"); return Task.CompletedTask;
    }

    private async Task RunAsync(string printer, Func<Task> action)
    {
        IsBusy = true;
        try { await action(); Detail = $"{printer}: operation completed successfully."; LastUpdated = DateTimeOffset.Now.ToString("g"); }
        catch (BridgeBusyException) { Detail = "The print bridge is busy; try again."; }
        catch (PrinterException ex) { Detail = $"{printer}: {ex.Message}"; }
        catch (Exception ex) { Detail = $"{printer}: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    private void RaiseCommandState()
    {
        RefreshCommand.NotifyCanExecuteChanged(); TestKitchenConnectionCommand.NotifyCanExecuteChanged(); TestKitchenPrintCommand.NotifyCanExecuteChanged(); TestRegisterConnectionCommand.NotifyCanExecuteChanged(); TestRegisterPrintCommand.NotifyCanExecuteChanged();
    }
}
