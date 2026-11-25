[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string]$Version,
    [Parameter(Mandatory)]
    [string]$ReleaseBucket = 'updates-coral-restaurant-site',
    [Parameter(Mandatory)]
    [string]$CloudFrontDomain = 'updates.coral.restaurant',
    [string]$ReleaseNotes = '',
    [string]$Region = 'us-east-1'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$updatePackageDirectory = Join-Path $repositoryRoot 'artifacts\updates'
$updatePackagePath = Join-Path $updatePackageDirectory "Coral-$Version.zip"
$packageKey = "windows/releases/Coral-$Version.zip"
$manifestKey = 'windows/stable.json'
$bootstrapManifestKey = 'windows/bootstrap/bootstrap.json'

$bootstrap = (aws s3 cp "s3://$ReleaseBucket/$bootstrapManifestKey" - --region $Region | ConvertFrom-Json)
if ([string]::IsNullOrWhiteSpace($bootstrap.InstallerUrl) -or $bootstrap.Sha256 -notmatch '^[A-Fa-f0-9]{64}$') { throw 'A valid bootstrap installer must be published before a lightweight release.' }
$updatePublishDirectory = Join-Path $env:TEMP ("coral-update-" + [guid]::NewGuid().ToString())
$selfContainedPublishDirectory = Join-Path $env:TEMP ("coral-self-contained-" + [guid]::NewGuid().ToString())
$updatePackageStagingDirectory = Join-Path $env:TEMP ("coral-update-staging-" + [guid]::NewGuid().ToString())
try {
    dotnet publish (Join-Path $repositoryRoot 'Coral.WinApp\Coral.WinApp.csproj') --configuration Release --runtime win-x64 --self-contained false --output $updatePublishDirectory "-p:Version=$Version"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet publish (Join-Path $repositoryRoot 'Coral.WinApp\Coral.WinApp.csproj') --configuration Release --runtime win-x64 --self-contained true --output $selfContainedPublishDirectory "-p:Version=$Version"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    New-Item -ItemType Directory -Path $updatePackageStagingDirectory -Force | Out-Null
    Copy-Item -Path (Join-Path $updatePublishDirectory '*') -Destination $updatePackageStagingDirectory -Recurse -Force
    Remove-Item -Path (Join-Path $updatePackageStagingDirectory 'Coral.exe'), (Join-Path $updatePackageStagingDirectory 'Coral.pdb') -Force -ErrorAction SilentlyContinue
    Get-ChildItem -Path $updatePublishDirectory -File -Recurse | Where-Object { $_.Name -ne 'Coral.exe' -and $_.Extension -ne '.pdb' } | ForEach-Object {
        $relativePath = $_.FullName.Substring($updatePublishDirectory.Length).TrimStart('\')
        Copy-Item -Path (Join-Path $selfContainedPublishDirectory $relativePath) -Destination (Join-Path $updatePackageStagingDirectory $relativePath) -Force
    }
    New-Item -ItemType Directory -Path $updatePackageDirectory -Force | Out-Null
    $updateItems = Get-ChildItem -Path $updatePackageStagingDirectory
    Compress-Archive -Path $updateItems.FullName -DestinationPath $updatePackagePath -Force
}
finally {
    if (Test-Path $updatePublishDirectory) { Remove-Item -Path $updatePublishDirectory -Recurse -Force }
    if (Test-Path $selfContainedPublishDirectory) { Remove-Item -Path $selfContainedPublishDirectory -Recurse -Force }
    if (Test-Path $updatePackageStagingDirectory) { Remove-Item -Path $updatePackageStagingDirectory -Recurse -Force }
}

$packageChecksum = (Get-FileHash -Path $updatePackagePath -Algorithm SHA256).Hash.ToLowerInvariant()
$packageUrl = "https://$CloudFrontDomain/$packageKey"
$manifest = [ordered]@{
    Version = $Version
    InstallerUrl = $bootstrap.InstallerUrl
    Sha256 = $bootstrap.Sha256
    PackageUrl = $packageUrl
    PackageSha256 = $packageChecksum
    ReleaseNotes = $ReleaseNotes
} | ConvertTo-Json -Compress
$manifestPath = Join-Path $env:TEMP 'coral-stable.json'
Set-Content -Path $manifestPath -Value $manifest -NoNewline -Encoding utf8

aws s3 cp $updatePackagePath "s3://$ReleaseBucket/$packageKey" --region $Region --cache-control 'public,max-age=31536000,immutable'
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
aws s3 cp $manifestPath "s3://$ReleaseBucket/$manifestKey" --region $Region --cache-control 'no-cache'
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Published Coral $Version"
Write-Host "Manifest: https://$CloudFrontDomain/$manifestKey"
