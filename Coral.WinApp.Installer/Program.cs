using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Coral.WinApp.Installer;

internal static class InstallerService
{
    private const string ApplicationName = "Coral";
    private const string ExecutableName = "Coral.exe";
    private const string LegacyExecutableName = "Coral.PrinterBridge.Console.exe";

    public static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Coral", "PrinterBridge", "settings.json");

    public static async Task InstallAsync(IProgress<string> progress, int? waitForProcessId = null)
    {
        var installationDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Coral", "WinApp");
        progress.Report("Preparando la instalación de Coral…");
        if (waitForProcessId is { } processId) await WaitForProcessExitAsync(processId, progress);
        await StopRunningCoralAsync(progress);
        RemovePreviousInstallations(installationDirectory);

        progress.Report("Instalando el puente de impresión Coral…");
        Directory.CreateDirectory(installationDirectory);
        var assembly = Assembly.GetExecutingAssembly();
        foreach (var resourceName in assembly.GetManifestResourceNames().Where(name => name.StartsWith("Payload/", StringComparison.Ordinal)))
        {
            var relativePath = resourceName["Payload/".Length..].Replace('/', Path.DirectorySeparatorChar);
            var destination = Path.Combine(installationDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var source = assembly.GetManifestResourceStream(resourceName) ?? throw new InvalidDataException($"Missing embedded file: {resourceName}");
            await using var target = File.Create(destination);
            await source.CopyToAsync(target);
        }

        progress.Report("Agregando accesos directos al menú Inicio y al inicio de sesión…");
        CreateStartMenuShortcut(installationDirectory);
        ConfigureAutostart(installationDirectory);
        progress.Report("Instalación completada.");
    }

    private static async Task WaitForProcessExitAsync(int processId, IProgress<string> progress)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited) return;
            progress.Report("Esperando a que Coral se cierre…");
            _ = process.CloseMainWindow();
            await Task.Delay(TimeSpan.FromSeconds(2));
            if (!process.HasExited)
            {
                progress.Report("Finalizando Coral para continuar con la instalación…");
                process.Kill(entireProcessTree: true);
            }
            await process.WaitForExitAsync();
        }
        catch (ArgumentException)
        {
            // The originating Coral process ended before the installer opened.
        }
    }

    private static async Task StopRunningCoralAsync(IProgress<string> progress)
    {
        var processes = new[] { Path.GetFileNameWithoutExtension(ExecutableName), Path.GetFileNameWithoutExtension(LegacyExecutableName) }
            .SelectMany(Process.GetProcessesByName).ToArray();
        if (processes.Length == 0) return;

        progress.Report("Cerrando una instancia anterior de Coral…");
        foreach (var process in processes)
        {
            try { _ = process.CloseMainWindow(); }
            catch (InvalidOperationException) { }
        }
        await Task.Delay(TimeSpan.FromSeconds(2));

        foreach (var process in processes)
        {
            try
            {
                if (!process.HasExited)
                {
                    progress.Report("Finalizando una instancia de Coral que no respondió…");
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }
            catch (InvalidOperationException) { }
            finally { process.Dispose(); }
        }
    }

    public static void LaunchCoral()
    {
        var executablePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Coral", "WinApp", ExecutableName);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{executablePath}\"") { UseShellExecute = true });
    }

    private static void CreateStartMenuShortcut(string installationDirectory)
    {
        var shortcutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs", "Coral", $"{ApplicationName}.lnk");
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows Script Host no está disponible.");
        dynamic shell = Activator.CreateInstance(shellType) ?? throw new InvalidOperationException("No se pudo crear Windows Script Host.");
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = Path.Combine(installationDirectory, ExecutableName);
        shortcut.WorkingDirectory = installationDirectory;
        shortcut.Description = ApplicationName;
        shortcut.Save();
    }

    private static void ConfigureAutostart(string installationDirectory)
    {
        using var runKey = Registry.LocalMachine.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        runKey.SetValue("Coral", $"\"{Path.Combine(installationDirectory, ExecutableName)}\" --background", RegistryValueKind.String);
    }

    private static void RemovePreviousInstallations(string installationDirectory)
    {
        StopAndDeleteLegacyService();
        using var runKey = Registry.LocalMachine.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        runKey.DeleteValue("CoralPrinterBridgeConsole", throwOnMissingValue: false);
        runKey.DeleteValue("CoralPrinterBridge", throwOnMissingValue: false);
        runKey.DeleteValue("Coral", throwOnMissingValue: false);

        var programsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs", "Coral");
        foreach (var shortcutName in new[] { "Coral Printer Bridge Console.lnk", "Coral Printer Bridge.lnk", "Coral.lnk" })
        {
            var shortcutPath = Path.Combine(programsDirectory, shortcutName);
            if (File.Exists(shortcutPath)) File.Delete(shortcutPath);
        }

        foreach (var legacyDirectory in new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Coral", "PrinterBridgeConsole"),
            installationDirectory
        })
        {
            if (Directory.Exists(legacyDirectory)) Directory.Delete(legacyDirectory, recursive: true);
        }
    }

    private static void StopAndDeleteLegacyService()
    {
        foreach (var command in new[] { "stop CoralPrinterBridge", "delete CoralPrinterBridge" })
        {
            using var process = Process.Start(new ProcessStartInfo("sc.exe", command) { CreateNoWindow = true, UseShellExecute = false });
            process?.WaitForExit();
        }
    }
}
