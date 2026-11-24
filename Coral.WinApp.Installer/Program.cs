using System.Diagnostics;
using System.Reflection;
using Microsoft.Win32;

const string ApplicationName = "Coral";
const string ExecutableName = "Coral.exe";
const string LegacyExecutableName = "Coral.PrinterBridge.Console.exe";

try
{
    var installationDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Coral", "WinApp");
    RemovePreviousInstallations(installationDirectory);

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

    CreateStartMenuShortcut(installationDirectory);
    ConfigureAutostart(installationDirectory);
    Console.WriteLine($"{ApplicationName} installed successfully to {installationDirectory}.");
    Console.WriteLine("Use Start > Coral > Coral to launch it.");
    Console.WriteLine("Press any key to close this installer.");
    Console.ReadKey(intercept: true);
    return;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Installation failed: {ex.Message}");
    Console.Error.WriteLine("Press any key to close this installer.");
    Console.ReadKey(intercept: true);
    Environment.ExitCode = 1;
}

static void CreateStartMenuShortcut(string installationDirectory)
{
    var shortcutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs", "Coral", $"{ApplicationName}.lnk");
    Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
    var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows Script Host is unavailable.");
    dynamic shell = Activator.CreateInstance(shellType) ?? throw new InvalidOperationException("Could not create Windows Script Host.");
    dynamic shortcut = shell.CreateShortcut(shortcutPath);
    shortcut.TargetPath = Path.Combine(installationDirectory, ExecutableName);
    shortcut.WorkingDirectory = installationDirectory;
    shortcut.Description = ApplicationName;
    shortcut.Save();
}

static void ConfigureAutostart(string installationDirectory)
{
    var runKey = Registry.LocalMachine.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
    runKey.SetValue("Coral", $"\"{Path.Combine(installationDirectory, ExecutableName)}\" --background", RegistryValueKind.String);
}

static void RemovePreviousInstallations(string installationDirectory)
{
    if (Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExecutableName)).Length > 0 ||
        Process.GetProcessesByName(Path.GetFileNameWithoutExtension(LegacyExecutableName)).Length > 0)
        throw new InvalidOperationException("Close Coral before installing an update.");

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

static void StopAndDeleteLegacyService()
{
    foreach (var command in new[] { "stop CoralPrinterBridge", "delete CoralPrinterBridge" })
    {
        using var process = Process.Start(new ProcessStartInfo("sc.exe", command)
        {
            CreateNoWindow = true,
            UseShellExecute = false
        });
        process?.WaitForExit();
    }
}
