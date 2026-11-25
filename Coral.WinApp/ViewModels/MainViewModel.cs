using Coral.WinApp.Bridge;
using Coral.WinApp.Infrastructure;

namespace Coral.WinApp.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly BridgeHost _bridge;
    private string _state = "Verificando impresoras…", _detail = "Comprobando la conexión de red de las impresoras.", _kitchenStatus = "Verificando…", _registerStatus = "Verificando…", _kitchenStatusColor = "#7A7572", _registerStatusColor = "#7A7572", _queueDepth = "0", _lastUpdated = "Aún no se ha actualizado";
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
    public string KitchenStatus { get => _kitchenStatus; private set => SetProperty(ref _kitchenStatus, value); }
    public string RegisterStatus { get => _registerStatus; private set => SetProperty(ref _registerStatus, value); }
    public string KitchenStatusColor { get => _kitchenStatusColor; private set => SetProperty(ref _kitchenStatusColor, value); }
    public string RegisterStatusColor { get => _registerStatusColor; private set => SetProperty(ref _registerStatusColor, value); }
    public string QueueDepth { get => _queueDepth; private set => SetProperty(ref _queueDepth, value); }
    public string LastUpdated { get => _lastUpdated; private set => SetProperty(ref _lastUpdated, value); }
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) RaiseCommandState(); } }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand TestKitchenConnectionCommand { get; }
    public AsyncRelayCommand TestKitchenPrintCommand { get; }
    public AsyncRelayCommand TestRegisterConnectionCommand { get; }
    public AsyncRelayCommand TestRegisterPrintCommand { get; }

    public async Task RefreshAsync()
    {
        if (!_bridge.IsRunning)
        {
            State = "Puente sin conexión";
            Detail = "No se pudo iniciar el puente de impresión local.";
            SetPrinterStatus(false, false);
            return;
        }

        try
        {
            var connectivity = await _bridge.CheckPrinterConnectivityAsync();
            SetPrinterStatus(connectivity.KitchenOnline, connectivity.RegisterOnline);
            State = connectivity is { KitchenOnline: true, RegisterOnline: true } ? "Impresoras conectadas" : connectivity is { KitchenOnline: false, RegisterOnline: false } ? "Impresoras sin conexión" : "Conexión parcial";
            Detail = connectivity is { KitchenOnline: true, RegisterOnline: true }
                ? "Las impresoras responden en la red del restaurante."
                : "Conéctate a la red del restaurante para usar las impresoras.";
        }
        catch (Exception)
        {
            State = "No se pudo verificar la red";
            Detail = "No fue posible comprobar la conexión de las impresoras.";
            SetPrinterStatus(false, false);
        }
        finally
        {
            QueueDepth = "0";
            LastUpdated = DateTimeOffset.Now.ToString("g");
        }
    }

    private async Task RunAsync(string printer, Func<Task> action)
    {
        IsBusy = true;
        try { await action(); Detail = $"{printer}: la operación se completó correctamente."; LastUpdated = DateTimeOffset.Now.ToString("g"); }
        catch (BridgeBusyException) { Detail = "El puente de impresión está ocupado; inténtalo de nuevo."; }
        catch (PrinterException ex) { Detail = $"{printer}: {ex.Message}"; }
        catch (Exception ex) { Detail = $"{printer}: {ex.Message}"; }
        finally { IsBusy = false; await RefreshAsync(); }
    }

    private void SetPrinterStatus(bool kitchenOnline, bool registerOnline)
    {
        KitchenStatus = kitchenOnline ? "Conectada" : "Sin conexión";
        RegisterStatus = registerOnline ? "Conectada" : "Sin conexión";
        KitchenStatusColor = kitchenOnline ? "#15803D" : "#B42318";
        RegisterStatusColor = registerOnline ? "#15803D" : "#B42318";
    }

    private void RaiseCommandState()
    {
        RefreshCommand.NotifyCanExecuteChanged(); TestKitchenConnectionCommand.NotifyCanExecuteChanged(); TestKitchenPrintCommand.NotifyCanExecuteChanged(); TestRegisterConnectionCommand.NotifyCanExecuteChanged(); TestRegisterPrintCommand.NotifyCanExecuteChanged();
    }
}
