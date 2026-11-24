# Coral

Windows WPF application that includes the Coral Printer Bridge. It owns the loopback print API and the printer TCP connection, while providing setup and diagnostics in the same executable.

For Surface setup, see [INSTALLATION.md](INSTALLATION.md). The installer also places that guide in the installed application folder.

## Current foundation

- `Coral.WinApp` targets `net8.0-windows` with a touch-friendly WPF shell and an embedded loopback bridge at `127.0.0.1:8765`.
- The existing Coral Admin HTTP routes (`/status`, `/print`, `/print/test`, and `/drawer/open`) are served by this app.
- Printer connection and test-print controls use the same serialized print coordinator as Coral Admin.

On first run, the app creates `%ProgramData%\Coral\PrinterBridge\settings.json`. Update the two printer IP addresses there before use. Logs are written beneath the same directory. The installer starts the app in background mode at interactive-user sign-in, so Coral Admin can print even when the console window is closed.

## Build on Windows

```powershell
dotnet build Coral.WinApp/Coral.WinApp.csproj
```

The app is deliberately Windows-only. Building it on macOS can restore cross-targeting assets but cannot run the WPF executable.

## Build an installer

The installer is a single, self-contained Windows `.exe`. It installs or updates the app in Program Files and creates a Start Menu shortcut. From Windows PowerShell (or PowerShell on macOS with the .NET SDK):

```powershell
.\deploy\build-installer.ps1 -Version 1.0.0
```

Copy `artifacts\installer\Coral.WinApp.Installer.exe` to the Surface and run it as an administrator. For an update, rebuild with a higher version (for example `1.0.1`) and run the new installer after closing Coral. The installer removes prior Coral Printer Bridge installations, their startup entries and shortcuts, and the legacy `CoralPrinterBridge` Windows service before it installs Coral.
