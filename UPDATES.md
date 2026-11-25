# Coral Windows updates

Coral checks an HTTPS release manifest only when `Updates.ManifestUrl` is configured in:

```text
C:\ProgramData\Coral\PrinterBridge\settings.json
```

The initial installation uses a self-contained installer. After the transition release, the in-app **Instalar versión más reciente** action downloads a small signed-by-checksum overlay package instead of the installer. It retains the installed .NET/WPF runtime, waits for Coral to close, applies only the application files, and restarts Coral. The first update from an older installation may still use the full installer.

## Provision AWS hosting

The Terraform configuration in `infra/updates` creates a private S3 bucket and a CloudFront distribution with Origin Access Control. The bucket is not public; CloudFront is the only reader.

```powershell
cd infra/updates
terraform init
terraform apply -var='aws_region=us-east-1' -var='release_bucket_name=your-globally-unique-coral-release-bucket'
```

Copy the `update_manifest_url` Terraform output into the `Updates` section of `settings.json`:

```json
"Updates": {
  "ManifestUrl": "https://your-distribution.cloudfront.net/windows/stable.json"
}
```

Use a custom CloudFront domain later if desired; the CloudFront domain output is sufficient to start.

## Publish a release

The release publisher builds both a self-contained installer (for new installations and backward compatibility) and a lightweight overlay package (for routine in-app updates). It hashes both files and writes the stable manifest.

```powershell
.\deploy\publish-update.ps1 `
  -Version 1.0.1 `
  -ReleaseBucket your-globally-unique-coral-release-bucket `
  -CloudFrontDomain your-distribution.cloudfront.net `
  -ReleaseNotes 'Improved printer diagnostics'
```

On macOS or Linux, use the equivalent shell command instead. It cross-publishes the Windows installer, so Windows is not needed to publish a release.

```bash
chmod +x ./deploy/publish-update.sh
./deploy/publish-update.sh 1.0.1 'Improved printer diagnostics'
```

The AWS identity that runs Terraform needs permissions to manage the listed S3 and CloudFront resources. The release-publishing identity only needs permission to put objects under `windows/` in the release bucket.

Before production distribution, code-sign `Coral.WinApp.Installer.exe` before running the publisher, and keep the signing certificate private. HTTPS, the private S3 origin and the manifest checksum protect normal delivery; Authenticode gives Windows and operators a separate publisher-identity check.
