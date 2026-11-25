using System.Net;

namespace Coral.WinApp.Bridge;

public sealed class BridgeOptions { public HttpOptions Http { get; init; } = new(); public string[] AllowedOrigins { get; init; } = []; public PrinterOptions Printers { get; init; } = new(); public TimeoutOptions Timeouts { get; init; } = new(); public UpdateOptions Updates { get; init; } = new(); }
public sealed class HttpOptions { public string Address { get; init; } = "127.0.0.1"; public int Port { get; init; } = 8765; }
public sealed class PrinterOptions { public PrinterEndpoint Kitchen { get; init; } = new(); public PrinterEndpoint Register { get; init; } = new(); }
public sealed class PrinterEndpoint { public string Ip { get; init; } = "127.0.0.1"; public int Port { get; init; } = 9100; public int DrawerPin { get; init; } }
public sealed class TimeoutOptions { public int ConnectSeconds { get; init; } = 3; public int RetryWindowSeconds { get; init; } = 120; public int WriteSeconds { get; init; } = 5; }
public sealed class UpdateOptions { public string ManifestUrl { get; init; } = "https://updates.coral.restaurant/windows/stable.json"; }

public static class BridgeOptionsValidator
{
    public static string? Validate(BridgeOptions value)
    {
        if (value.Http.Address != "127.0.0.1" || value.Http.Port is < 1 or > 65535) return "HTTP debe vincularse a 127.0.0.1 con un puerto válido.";
        if (value.AllowedOrigins.Any(origin => !Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.GetLeftPart(UriPartial.Authority) != origin || string.IsNullOrWhiteSpace(uri.Host))) return "AllowedOrigins debe contener orígenes absolutos exactos sin una barra final.";
        foreach (var printer in new[] { value.Printers.Kitchen, value.Printers.Register }) if (!IPAddress.TryParse(printer.Ip, out _) || printer.Port is < 1 or > 65535 || printer.DrawerPin is < 0 or > 1) return "La configuración de las impresoras no es válida.";
        if (!string.IsNullOrWhiteSpace(value.Updates.ManifestUrl) && (!Uri.TryCreate(value.Updates.ManifestUrl, UriKind.Absolute, out var manifestUri) || manifestUri.Scheme != Uri.UriSchemeHttps)) return "Updates.ManifestUrl debe ser una URL HTTPS cuando esté configurada.";
        return value.Timeouts.ConnectSeconds is <= 0 || value.Timeouts.RetryWindowSeconds is <= 0 || value.Timeouts.WriteSeconds is <= 0 ? "Los tiempos de espera deben ser positivos." : null;
    }
}
