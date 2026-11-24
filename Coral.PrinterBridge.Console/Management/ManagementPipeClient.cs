using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Coral.PrinterBridge.Console.Management;

public interface IManagementPipeClient
{
    Task<ManagementResponse> SendAsync(string command, object? data = null, CancellationToken cancellationToken = default);
}

public sealed class ManagementPipeClient : IManagementPipeClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ManagementResponse> SendAsync(string command, object? data = null, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(command is "printer.testPrint" or "drawer.testOpen" ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(10));
        await using var pipe = new NamedPipeClientStream(".", ManagementProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
        var request = new ManagementRequest(ManagementProtocol.MajorVersion, Guid.NewGuid().ToString("N"), command, data);
        var payload = JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions);
        if (payload.Length > ManagementProtocol.MaximumMessageBytes - 1) throw new InvalidOperationException("Management request exceeds 64 KiB.");
        await pipe.WriteAsync(payload, timeout.Token).ConfigureAwait(false);
        await pipe.WriteAsync("\n"u8.ToArray(), timeout.Token).ConfigureAwait(false);
        await pipe.FlushAsync(timeout.Token).ConfigureAwait(false);

        using var reader = new StreamReader(pipe, Encoding.UTF8, false, ManagementProtocol.MaximumMessageBytes, leaveOpen: true);
        var responseLine = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(responseLine)) throw new InvalidDataException("Management service returned an empty response.");
        if (Encoding.UTF8.GetByteCount(responseLine) > ManagementProtocol.MaximumMessageBytes) throw new InvalidDataException("Management response exceeds 64 KiB.");
        var response = JsonSerializer.Deserialize<ManagementResponse>(responseLine, JsonOptions) ?? throw new InvalidDataException("Management service returned invalid JSON.");
        if (response.RequestId != request.RequestId) throw new InvalidDataException("Management response ID did not match request.");
        return response;
    }
}
