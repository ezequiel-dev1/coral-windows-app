[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$publishDirectory = Join-Path $repositoryRoot 'artifacts\publish'
$winAppProject = Join-Path $repositoryRoot 'Coral.WinApp\Coral.WinApp.csproj'
$installerProject = Join-Path $repositoryRoot 'Coral.WinApp.Installer\Coral.WinApp.Installer.csproj'

dotnet publish $winAppProject --configuration Release --runtime win-x64 --self-contained true --output $publishDirectory -p:Version=$Version
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet publish $installerProject --configuration Release --runtime win-x64 --self-contained true --output (Join-Path $repositoryRoot 'artifacts\installer') -p:PublishSingleFile=true -p:Version=$Version
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Installer: $repositoryRoot\artifacts\installer\Coral.WinApp.Installer.exe"
