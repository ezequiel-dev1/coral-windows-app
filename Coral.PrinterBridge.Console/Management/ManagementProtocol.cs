using System.Text.Json;

namespace Coral.PrinterBridge.Console.Management;

public static class ManagementProtocol
{
    public const int MajorVersion = 1;
    public const string PipeName = "Coral.PrinterBridge.Management";
    public const int MaximumMessageBytes = 64 * 1024;
}

public sealed record ManagementRequest(int ProtocolVersion, string RequestId, string Command, object? Data = null);
public sealed record ManagementResponse(string RequestId, bool Ok, string Message, JsonElement? Data = null);
public sealed record BridgeStatus(string ServiceVersion, TimeSpan Uptime, bool ConfigurationValid, bool LoopbackListening, int QueueDepth, IReadOnlyList<PrinterSummary> Printers);
public sealed record PrinterSummary(string Name, string Endpoint, string LastResult, DateTimeOffset? LastOperationAt);
