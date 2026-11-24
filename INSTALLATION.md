# Coral Printer Bridge Console — Surface installation

## Install

1. Copy `Coral.PrinterBridge.Console.Installer.exe` to the Surface.
2. Right-click the file and select **Run as administrator**.
3. Approve the Windows security prompt.
4. When installation completes, open **Start** and select **Coral > Coral Printer Bridge Console**.

The installer places the application in `C:\Program Files\Coral\PrinterBridgeConsole` and configures it to start silently when a Surface user signs in. A separate Windows service is not required.

## Configure printers

Before printing, edit this file with Notepad run as Administrator:

```text
C:\ProgramData\Coral\PrinterBridge\settings.json
```

Update the two IP addresses under `Printers`:

```json
"Printers": {
  "Kitchen": { "Ip": "10.0.0.100", "Port": 9100, "DrawerPin": 0 },
  "Register": { "Ip": "10.0.0.101", "Port": 9100, "DrawerPin": 0 }
}
```

Replace `10.0.0.100` and `10.0.0.101` with the actual static IP addresses of the kitchen and register printers. Keep port `9100` unless the printer has been configured differently. Save the file as `settings.json`, not `settings.json.txt`.

## Apply settings

Sign out and sign back in to the Surface, or restart it. The background bridge reads the settings when it starts.

Open **Coral Printer Bridge Console** from Start and use the **Printers** tab to run a connection check and print a test receipt for each printer.

## Troubleshooting

- Coral Admin must point to `http://127.0.0.1:8765`.
- The Surface user must have signed in; this app starts at user sign-in, not before it.
- Logs are in `C:\ProgramData\Coral\PrinterBridge\logs`.
- Do not install the old `CoralPrinterBridge` Windows service alongside this app; both use port `8765`.
