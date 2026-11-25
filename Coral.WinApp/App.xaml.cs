using System.Windows;
using System.IO.Pipes;
using System.Text;
using Coral.WinApp.Bridge;

namespace Coral.WinApp;

public partial class App : System.Windows.Application
{
    private const string InstanceMutexName = "Local\\Coral.PrinterBridge";
    private const string ActivationPipeName = "Coral.PrinterBridge.Activation";
    private readonly BridgeHost _bridge = new();
    private readonly CancellationTokenSource _activationCancellation = new();
    private Mutex? _instanceMutex;
    private MainWindow? _mainWindow;
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    internal static bool IsExiting { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            if (!await TellRunningInstanceToShowAsync())
                System.Windows.MessageBox.Show("Coral ya se está ejecutando con permisos distintos. Ciérralo desde el Administrador de tareas y vuelve a abrirlo desde el menú Inicio.", "Coral", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _ = ListenForActivationAsync(_activationCancellation.Token);
        try { await _bridge.StartAsync(); }
        catch (Exception ex)
        {
            var message = ex.Message.Contains("address already in use", StringComparison.OrdinalIgnoreCase)
                ? "Coral ya se está ejecutando en este equipo. Cierra la otra instancia desde el Administrador de tareas y vuelve a intentarlo."
                : $"No se pudo iniciar el puente de impresión Coral. {ex.Message}";
            System.Windows.MessageBox.Show(message, "Puente de impresión Coral", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }
        CreateTrayIcon();
        if (!e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase)) ShowMainWindow();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activationCancellation.Cancel();
        _trayIcon?.Dispose();
        _bridge.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        _activationCancellation.Dispose();
        base.OnExit(e);
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            _mainWindow = new MainWindow(_bridge);
            _mainWindow.Closed += (_, _) => _mainWindow = null;
        }

        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Left = SystemParameters.WorkArea.Right - _mainWindow.Width - 16;
        _mainWindow.Top = SystemParameters.WorkArea.Bottom - _mainWindow.Height - 16;
        _mainWindow.Activate();
        _mainWindow.Topmost = true;
        _mainWindow.Topmost = false;
        _mainWindow.Focus();
    }

    private void CreateTrayIcon()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Abrir Coral", null, (_, _) => Dispatcher.Invoke(ShowMainWindow));
        menu.Items.Add("Instalar actualización", null, (_, _) => Dispatcher.BeginInvoke(async () =>
        {
            ShowMainWindow();
            if (_mainWindow is not null) await _mainWindow.InstallLatestAsync();
        }));
        menu.Items.Add("Salir", null, (_, _) => Dispatcher.Invoke(ExitApplication));
        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application,
            Text = "Coral",
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowMainWindow);
    }

    private void ExitApplication()
    {
        IsExiting = true;
        Shutdown();
    }

    internal static void ShutdownForUpdate()
    {
        IsExiting = true;
        Current.Shutdown();
    }

    internal static void ExitFromUserAction()
    {
        IsExiting = true;
        Current.Shutdown();
    }

    private async Task ListenForActivationAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await using var pipe = new NamedPipeServerStream(ActivationPipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                if (await reader.ReadLineAsync(cancellationToken) == "show") await Dispatcher.InvokeAsync(ShowMainWindow);
            }
        }
        catch (OperationCanceledException) { }
    }

    private static async Task<bool> TellRunningInstanceToShowAsync()
    {
        try
        {
            await using var pipe = new NamedPipeClientStream(".", ActivationPipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(1_500);
            await using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
            await writer.WriteLineAsync("show");
            return true;
        }
        catch (TimeoutException) { return false; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
