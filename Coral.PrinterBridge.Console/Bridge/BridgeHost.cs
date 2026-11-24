using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Coral.PrinterBridge.Console.Bridge;

public sealed class BridgeHost : IAsyncDisposable
{
    private WebApplication? _application;
    public bool IsRunning => _application is not null;
    public string SettingsPath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Coral", "PrinterBridge", "settings.json");

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_application is not null) return;
        EnsureSettingsFile();
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddJsonFile(SettingsPath, optional: false, reloadOnChange: false);
        var options = builder.Configuration.Get<BridgeOptions>() ?? throw new InvalidOperationException("Printer bridge settings are missing.");
        if (BridgeOptionsValidator.Validate(options) is { } error) throw new InvalidOperationException(error);
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new RollingFileLoggerProvider(Path.Combine(Path.GetDirectoryName(SettingsPath)!, "logs")));
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<IPrinterClient, TcpPrinterClient>();
        builder.Services.AddSingleton<PrintCoordinator>();
        builder.WebHost.ConfigureKestrel(server => { server.Listen(IPAddress.Loopback, options.Http.Port); server.Limits.MaxRequestBodySize = 64 * 1024; server.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10); });
        var application = builder.Build();
        ConfigureRoutes(application, options);
        await application.StartAsync(cancellationToken);
        _application = application;
    }

    public async Task TestConnectionAsync(PrinterTarget target, CancellationToken cancellationToken = default) => await Coordinator().ExecuteAsync(target, ReadOnlyMemory<byte>.Empty, cancellationToken);
    public async Task TestPrintAsync(PrinterTarget target, CancellationToken cancellationToken = default) => await Coordinator().ExecuteAsync(target, EscPosRenderer.TestReceipt(target.ToString()), cancellationToken);
    public async Task OpenDrawerAsync(CancellationToken cancellationToken = default) => await Coordinator().ExecuteAsync(PrinterTarget.Register, EscPosRenderer.DrawerPulse(Options().Printers.Register.DrawerPin), cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_application is null) return;
        await _application.StopAsync();
        await _application.DisposeAsync();
        _application = null;
    }

    private PrintCoordinator Coordinator() => _application?.Services.GetRequiredService<PrintCoordinator>() ?? throw new InvalidOperationException("The bridge is not running.");
    private BridgeOptions Options() => _application?.Services.GetRequiredService<BridgeOptions>() ?? throw new InvalidOperationException("The bridge is not running.");
    private void EnsureSettingsFile()
    {
        if (File.Exists(SettingsPath)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, "{\n  \"Http\": { \"Address\": \"127.0.0.1\", \"Port\": 8765 },\n  \"AllowedOrigins\": [\"https://admin.coral.restaurant\"],\n  \"Printers\": { \"Kitchen\": { \"Ip\": \"10.0.0.100\", \"Port\": 9100, \"DrawerPin\": 0 }, \"Register\": { \"Ip\": \"10.0.0.101\", \"Port\": 9100, \"DrawerPin\": 0 } },\n  \"Timeouts\": { \"ConnectSeconds\": 3, \"RetryWindowSeconds\": 120, \"WriteSeconds\": 5 }\n}\n");
    }

    private static void ConfigureRoutes(WebApplication app, BridgeOptions options)
    {
        app.Use(async (context, next) =>
        {
            var origin = context.Request.Headers.Origin.FirstOrDefault();
            if (origin is not null && !options.AllowedOrigins.Contains(origin, StringComparer.Ordinal)) { await Results.Json(new ApiResult(false, "Origin not allowed"), statusCode: 403).ExecuteAsync(context); return; }
            if (origin is not null) { context.Response.Headers.AccessControlAllowOrigin = origin; context.Response.Headers.Vary = "Origin"; context.Response.Headers.AccessControlAllowMethods = "GET, POST, OPTIONS"; context.Response.Headers.AccessControlAllowHeaders = "Content-Type"; context.Response.Headers.Append("Access-Control-Allow-Private-Network", "true"); context.Response.Headers.AccessControlMaxAge = "600"; }
            if (HttpMethods.IsOptions(context.Request.Method)) { context.Response.StatusCode = 204; return; }
            await next();
        });
        app.MapGet("/status", () => Results.Json(new ApiResult(true, "Bridge running")));
        app.MapPost("/print/test", async (HttpContext context, PrintCoordinator coordinator) => await Execute(context, coordinator, await TargetFromBody(context.Request), target => EscPosRenderer.TestReceipt(target.ToString())));
        app.MapPost("/drawer/open", async (HttpContext context, PrintCoordinator coordinator) => await Execute(context, coordinator, (true, PrinterTarget.Register), _ => EscPosRenderer.DrawerPulse(options.Printers.Register.DrawerPin)));
        app.MapPost("/print", async (HttpContext context, PrintCoordinator coordinator) =>
        {
            try { using var body = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted); if (!PrintJobParser.TryParse(body.RootElement, out var job, out var error)) return Results.Json(new ApiResult(false, error), statusCode: 400); return await Execute(context, coordinator, (true, job!.Printer), _ => EscPosRenderer.Render(job)); }
            catch (JsonException) { return Results.Json(new ApiResult(false, "Body must be valid JSON"), statusCode: 400); }
        });
    }

    private static async Task<IResult> Execute(HttpContext context, PrintCoordinator coordinator, (bool Valid, PrinterTarget Target) parsed, Func<PrinterTarget, byte[]> render)
    {
        if (!parsed.Valid) return Results.Json(new ApiResult(false, "\"printer\" must be kitchen or register"), statusCode: 400);
        try { await coordinator.ExecuteAsync(parsed.Target, render(parsed.Target), context.RequestAborted); return Results.Json(new ApiResult(true, "Command sent")); }
        catch (BridgeBusyException) { return Results.Json(new ApiResult(false, "Printer bridge is busy; try again"), statusCode: 503); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { return Results.StatusCode(499); }
        catch (PrinterException ex) { return Results.Json(new ApiResult(false, ex.Message), statusCode: 502); }
    }

    private static async Task<(bool Valid, PrinterTarget Target)> TargetFromBody(HttpRequest request)
    {
        if (request.ContentLength is null or 0) return (true, PrinterTarget.Kitchen);
        try { using var body = await JsonDocument.ParseAsync(request.Body); if (!body.RootElement.TryGetProperty("printer", out var value)) return (true, PrinterTarget.Kitchen); return value.ValueKind == JsonValueKind.String && Enum.TryParse<PrinterTarget>(value.GetString(), true, out var target) ? (true, target) : (false, default); }
        catch (JsonException) { return (false, default); }
    }
}
