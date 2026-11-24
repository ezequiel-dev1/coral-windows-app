[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$publishDirectory = Join-Path $repositoryRoot 'artifacts\publish'
$consoleProject = Join-Path $repositoryRoot 'Coral.PrinterBridge.Console\Coral.PrinterBridge.Console.csproj'
$installerProject = Join-Path $repositoryRoot 'Coral.PrinterBridge.Console.Installer\Coral.PrinterBridge.Console.Installer.csproj'

dotnet publish $consoleProject --configuration Release --runtime win-x64 --self-contained true --output $publishDirectory
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet publish $installerProject --configuration Release --runtime win-x64 --self-contained true --output (Join-Path $repositoryRoot 'artifacts\installer') -p:PublishSingleFile=true -p:Version=$Version
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Installer: $repositoryRoot\artifacts\installer\Coral.PrinterBridge.Console.Installer.exe"
