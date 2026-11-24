using System.Diagnostics;
using System.Reflection;
using Microsoft.Win32;

const string ApplicationName = "Coral Printer Bridge Console";
const string ExecutableName = "Coral.PrinterBridge.Console.exe";

try
{
    var installationDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Coral", "PrinterBridgeConsole");
    if (Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExecutableName)).Length > 0)
        throw new InvalidOperationException("Close Coral Printer Bridge Console before installing an update.");

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
    Console.WriteLine("Use Start > Coral > Coral Printer Bridge Console to launch it.");
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
    runKey.SetValue("CoralPrinterBridgeConsole", $"\"{Path.Combine(installationDirectory, ExecutableName)}\" --background", RegistryValueKind.String);
}
