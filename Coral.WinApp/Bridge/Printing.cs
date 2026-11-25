using System.Net.Sockets;
using System.Text;

namespace Coral.WinApp.Bridge;

public static class EscPosRenderer
{
    private static readonly byte[] Initialize = [0x1b, 0x40];
    private static readonly byte[] Cut = [0x1d, 0x56, 0x00];
    private static readonly Encoding Cp437 = CreateCp437();

    public static byte[] TestReceipt(string station) => [.. Initialize, .. Encoding.ASCII.GetBytes($"================================================\n               CORAL PRINTER BRIDGE\n================================================\n                  PRINTER TEST\nStation: {station}\nConnection: Ethernet\nStatus: Test command received\n------------------------------------------------\n                 TEST SUCCESSFUL\n------------------------------------------------\n\n\n\n"), .. Cut];
    public static byte[] DrawerPulse(int pin) => [0x1b, 0x70, (byte)(pin == 1 ? 1 : 0), 0x32, 0x32];

    public static byte[] Render(PrintJob job)
    {
        using var output = new MemoryStream();
        output.Write(Initialize); output.Write([0x1b, 0x74, 0x00]);
        if (job.OpenDrawer) output.Write(DrawerPulse(0));
        foreach (var line in job.Lines)
        {
            output.Write([0x1b, 0x61, (byte)line.Align, 0x1b, 0x45, line.Bold ? (byte)1 : (byte)0, 0x1b, 0x2d, line.Underline ? (byte)1 : (byte)0]);
            var size = (byte)(line.Size - 1); output.Write([0x1d, 0x21, (byte)((size << 4) | size)]);
            output.Write(Cp437.GetBytes(line.Text.Replace("\r", string.Empty))); output.WriteByte(0x0a);
        }
        output.Write([0x1b, 0x61, 0x00, 0x1b, 0x45, 0x00, 0x1b, 0x2d, 0x00, 0x1d, 0x21, 0x00]);
        for (var index = 0; index < job.Feed; index++) output.WriteByte(0x0a);
        if (job.Cut) output.Write(Cut);
        return output.ToArray();
    }

    private static Encoding CreateCp437() { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); return Encoding.GetEncoding(437, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback); }
}

public interface IPrinterClient { Task SendAsync(PrinterEndpoint endpoint, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken); }

public sealed class TcpPrinterClient(BridgeOptions options, ILogger<TcpPrinterClient> logger) : IPrinterClient
{
    public async Task SendAsync(PrinterEndpoint endpoint, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(options.Timeouts.RetryWindowSeconds); Exception? lastError = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            using var socket = new TcpClient();
            try
            {
                using var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(options.Timeouts.ConnectSeconds));
                await socket.ConnectAsync(endpoint.Ip, endpoint.Port, connectTimeout.Token);
                if (!bytes.IsEmpty) { using var writeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(options.Timeouts.WriteSeconds)); await socket.GetStream().WriteAsync(bytes, writeTimeout.Token); await socket.GetStream().FlushAsync(writeTimeout.Token); }
                return;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new PrinterException("Se agotó el tiempo de espera de conexión o escritura de la impresora."); }
            catch (SocketException ex) { lastError = ex; await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken); }
            catch (IOException ex) { throw new PrinterException("Falló la escritura en la impresora.", ex); }
        }
        logger.LogWarning(lastError, "Printer {Address}:{Port} unavailable", endpoint.Ip, endpoint.Port);
        throw new PrinterException("No se pudo conectar con la impresora. Verifica que esté encendida y conectada.", lastError);
    }
}

public sealed class PrinterException(string message, Exception? inner = null) : Exception(message, inner);
public sealed class BridgeBusyException : Exception;

public sealed class PrintCoordinator(IPrinterClient client, BridgeOptions options, ILogger<PrintCoordinator> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _capacity = new(9, 9);
    public async Task ExecuteAsync(PrinterTarget target, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        if (!await _capacity.WaitAsync(0, cancellationToken)) throw new BridgeBusyException();
        var started = false;
        try { await _gate.WaitAsync(cancellationToken); started = true; await client.SendAsync(target == PrinterTarget.Kitchen ? options.Printers.Kitchen : options.Printers.Register, bytes, CancellationToken.None); }
        catch (OperationCanceledException) when (!started) { throw; }
        catch (Exception ex) { logger.LogWarning(ex, "Printer operation failed for {Target}", target); throw; }
        finally { if (started) _gate.Release(); _capacity.Release(); }
    }
}
