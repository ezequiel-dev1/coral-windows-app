using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using Coral.WinApp.Bridge;

namespace Coral.WinApp.Updates;

public sealed record UpdateRelease(Version Version, Uri InstallerUri, string Sha256, string ReleaseNotes, Uri? PackageUri, string? PackageSha256);

public sealed class UpdateService
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly UpdateOptions _options;

    public UpdateService(UpdateOptions options) => _options = options;

    public async Task<UpdateRelease> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ManifestUrl)) throw new InvalidOperationException("No se configuró la dirección de actualizaciones.");
        var manifestUri = new Uri(_options.ManifestUrl, UriKind.Absolute);
        using var response = await Client.GetAsync(manifestUri, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken) ?? throw new InvalidDataException("El manifiesto de actualización está vacío.");
        if (!Version.TryParse(manifest.Version, out var version)) throw new InvalidDataException("El manifiesto de actualización contiene una versión no válida.");
        if (!Uri.TryCreate(manifest.InstallerUrl, UriKind.Absolute, out var installerUri) || installerUri.Scheme != Uri.UriSchemeHttps) throw new InvalidDataException("La URL del instalador de actualización debe usar HTTPS.");
        if (manifest.Sha256 is null || manifest.Sha256.Length != 64 || !manifest.Sha256.All(Uri.IsHexDigit)) throw new InvalidDataException("El manifiesto de actualización tiene una suma de comprobación SHA-256 no válida.");
        var packageUri = TryGetSecureUri(manifest.PackageUrl);
        if (packageUri is not null && (manifest.PackageSha256 is null || manifest.PackageSha256.Length != 64 || !manifest.PackageSha256.All(Uri.IsHexDigit)))
            throw new InvalidDataException("El paquete de actualización tiene una suma de comprobación SHA-256 no válida.");
        return new UpdateRelease(version, installerUri, manifest.Sha256, manifest.ReleaseNotes ?? "", packageUri, manifest.PackageSha256);
    }

    public async Task StartUpdateAsync(UpdateRelease release, CancellationToken cancellationToken = default)
    {
        if (release.PackageUri is not null && release.PackageSha256 is not null)
        {
            await StartOverlayUpdateAsync(release, cancellationToken);
            return;
        }

        await StartFullInstallerAsync(release, cancellationToken);
    }

    private static async Task StartFullInstallerAsync(UpdateRelease release, CancellationToken cancellationToken)
    {
        var updateDirectory = Path.Combine(Path.GetTempPath(), "Coral", "Updates");
        Directory.CreateDirectory(updateDirectory);
        var installerPath = Path.Combine(updateDirectory, $"Coral-{release.Version}.exe");
        await DownloadAndVerifyAsync(release.InstallerUri, installerPath, release.Sha256, "El instalador descargado no coincide con su suma de comprobación publicada.", cancellationToken);

        Process.Start(new ProcessStartInfo(installerPath, $"--wait-for-pid {Environment.ProcessId}") { UseShellExecute = true });
    }

    private static async Task StartOverlayUpdateAsync(UpdateRelease release, CancellationToken cancellationToken)
    {
        var updateDirectory = Path.Combine(Path.GetTempPath(), "Coral", "Updates");
        Directory.CreateDirectory(updateDirectory);
        var packagePath = Path.Combine(updateDirectory, $"Coral-{release.Version}.zip");
        await DownloadAndVerifyAsync(release.PackageUri!, packagePath, release.PackageSha256!, "El paquete descargado no coincide con su suma de comprobación publicada.", cancellationToken);

        var scriptPath = Path.Combine(updateDirectory, $"Apply-Coral-{release.Version}.ps1");
        await File.WriteAllTextAsync(scriptPath, UpdateScript, cancellationToken);
        var startInfo = new ProcessStartInfo("powershell.exe") { UseShellExecute = true, Verb = "runas" };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("-WaitForPid");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        startInfo.ArgumentList.Add("-PackagePath");
        startInfo.ArgumentList.Add(packagePath);
        startInfo.ArgumentList.Add("-InstallDirectory");
        startInfo.ArgumentList.Add(AppContext.BaseDirectory);
        startInfo.ArgumentList.Add("-RestartPath");
        startInfo.ArgumentList.Add(Environment.ProcessPath ?? throw new InvalidOperationException("No se pudo determinar la ubicación de Coral."));
        Process.Start(startInfo);
    }

    private static async Task DownloadAndVerifyAsync(Uri source, string destination, string expectedChecksum, string checksumError, CancellationToken cancellationToken)
    {
        await using (var output = File.Create(destination))
        await using (var input = await Client.GetStreamAsync(source, cancellationToken))
            await input.CopyToAsync(output, cancellationToken);
        await using var downloadedFile = File.OpenRead(destination);
        var checksum = Convert.ToHexString(await SHA256.HashDataAsync(downloadedFile, cancellationToken));
        if (string.Equals(checksum, expectedChecksum, StringComparison.OrdinalIgnoreCase)) return;
        File.Delete(destination);
        throw new InvalidDataException(checksumError);
    }

    private static Uri? TryGetSecureUri(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri : null;

    private const string UpdateScript = """
param(
    [int] $WaitForPid,
    [string] $PackagePath,
    [string] $InstallDirectory,
    [string] $RestartPath
)

$ErrorActionPreference = 'Stop'
$logDirectory = Join-Path $env:ProgramData 'Coral\PrinterBridge\logs'
$logPath = Join-Path $logDirectory 'update.log'
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
function Write-Status([string] $Message) {
    $timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    Write-Host $Message
    Add-Content -LiteralPath $logPath -Value "$timestamp  $Message"
}

try {
    Write-Status 'Coral se está actualizando. No cierres esta ventana.'
    Write-Status 'Esperando a que Coral se cierre…'
    $process = Get-Process -Id $WaitForPid -ErrorAction SilentlyContinue
    if ($null -ne $process) {
        $process.CloseMainWindow() | Out-Null
        Start-Sleep -Seconds 2
        if (-not $process.HasExited) {
            Write-Status 'Finalizando Coral para continuar con la actualización…'
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        }
    }
    $runningCoral = @(Get-Process -Name 'Coral' -ErrorAction SilentlyContinue)
    if ($runningCoral.Count -gt 0) {
        Write-Status 'Cerrando otras instancias de Coral…'
        foreach ($runningProcess in $runningCoral) { $runningProcess.CloseMainWindow() | Out-Null }
        Start-Sleep -Seconds 2
        foreach ($runningProcess in $runningCoral) {
            if (-not $runningProcess.HasExited) {
                Write-Status 'Finalizando una instancia de Coral que no respondió…'
                Stop-Process -Id $runningProcess.Id -Force -ErrorAction SilentlyContinue
            }
        }
    }
    Write-Status 'Preparando los archivos…'
    $stagingDirectory = Join-Path $env:TEMP ('Coral-Update-' + [guid]::NewGuid().ToString())
    New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null
    Expand-Archive -LiteralPath $PackagePath -DestinationPath $stagingDirectory -Force
    Write-Status 'Instalando la versión más reciente…'
    Get-ChildItem -LiteralPath $stagingDirectory | Copy-Item -Destination $InstallDirectory -Recurse -Force
    Write-Status 'Iniciando Coral…'
    Start-Process -FilePath 'explorer.exe' -ArgumentList ('"' + $RestartPath + '"')
}
catch {
    $errorMessage = $_ | Out-String
    Add-Content -LiteralPath $logPath -Value $errorMessage
    Write-Host ''
    Write-Host 'No se pudo instalar la actualización.' -ForegroundColor Red
    Write-Host "Detalles guardados en: $logPath" -ForegroundColor Yellow
    Write-Host $errorMessage -ForegroundColor Red
    Read-Host 'Presiona Enter para cerrar'
    exit 1
}
finally {
    if ($null -ne $stagingDirectory -and (Test-Path -LiteralPath $stagingDirectory)) { Remove-Item -LiteralPath $stagingDirectory -Recurse -Force }
    if (Test-Path -LiteralPath $PackagePath) { Remove-Item -LiteralPath $PackagePath -Force }
}
""";

    private sealed class UpdateManifest
    {
        public string Version { get; init; } = "";
        public string InstallerUrl { get; init; } = "";
        public string? Sha256 { get; init; }
        public string? ReleaseNotes { get; init; }
        public string? PackageUrl { get; init; }
        public string? PackageSha256 { get; init; }
    }
}
