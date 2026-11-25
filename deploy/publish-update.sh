#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 1 || $# -gt 2 ]]; then
  echo "Usage: $0 VERSION [RELEASE_NOTES]" >&2
  exit 64
fi

version="$1"
release_notes="${2:-}"
release_bucket="updates-coral-restaurant-site"
cloudfront_domain="updates.coral.restaurant"
distribution_id="E3TQ9GV3I6CV8I"
aws_region="us-east-1"
repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
update_package_directory="$repository_root/artifacts/updates"
update_package_path="$update_package_directory/Coral-${version}.zip"
package_key="windows/releases/Coral-${version}.zip"
manifest_key="windows/stable.json"
bootstrap_manifest_key="windows/bootstrap/bootstrap.json"
manifest_path="$(mktemp "${TMPDIR:-/tmp}/coral-stable.XXXXXX.json")"
update_publish_directory="$(mktemp -d "${TMPDIR:-/tmp}/coral-update.XXXXXX")"
self_contained_publish_directory="$(mktemp -d "${TMPDIR:-/tmp}/coral-self-contained.XXXXXX")"
update_package_staging_directory="$(mktemp -d "${TMPDIR:-/tmp}/coral-update-staging.XXXXXX")"

cleanup() { rm -f "$manifest_path"; rm -rf "$update_publish_directory" "$self_contained_publish_directory" "$update_package_staging_directory"; }
trap cleanup EXIT

if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(\.[0-9]+)?$ ]]; then
  echo "VERSION must be in major.minor.patch[.build] format." >&2
  exit 64
fi
command -v aws >/dev/null || { echo "AWS CLI is required." >&2; exit 69; }
command -v jq >/dev/null || { echo "jq is required." >&2; exit 69; }
command -v zip >/dev/null || { echo "zip is required." >&2; exit 69; }

bootstrap_manifest="$(aws s3 cp "s3://${release_bucket}/${bootstrap_manifest_key}" - --region "$aws_region")"
installer_url="$(jq -r '.InstallerUrl // empty' <<< "$bootstrap_manifest")"
checksum="$(jq -r '.Sha256 // empty' <<< "$bootstrap_manifest")"
[[ "$installer_url" =~ ^https:// && "$checksum" =~ ^[A-Fa-f0-9]{64}$ ]] || { echo "A valid bootstrap installer must be published before a lightweight release." >&2; exit 69; }

dotnet publish "$repository_root/Coral.WinApp/Coral.WinApp.csproj" \
  --configuration Release --runtime win-x64 --self-contained false --output "$update_publish_directory" \
  -p:Version="$version"
dotnet publish "$repository_root/Coral.WinApp/Coral.WinApp.csproj" \
  --configuration Release --runtime win-x64 --self-contained true --output "$self_contained_publish_directory" \
  -p:Version="$version"
cp -R "$update_publish_directory/." "$update_package_staging_directory/"
rm -f "$update_package_staging_directory/Coral.exe" "$update_package_staging_directory/Coral.pdb"
while IFS= read -r -d '' update_file; do
  cp "$self_contained_publish_directory/${update_file#./}" "$update_package_staging_directory/${update_file#./}"
done < <(cd "$update_publish_directory" && find . -type f ! -name 'Coral.exe' ! -name '*.pdb' -print0)
mkdir -p "$update_package_directory"
rm -f "$update_package_path"
(cd "$update_package_staging_directory" && zip -qr "$update_package_path" .)

package_checksum="$(shasum -a 256 "$update_package_path" | awk '{print tolower($1)}')"
package_url="https://${cloudfront_domain}/${package_key}"
jq -n --arg version "$version" --arg installerUrl "$installer_url" --arg sha256 "$checksum" --arg packageUrl "$package_url" --arg packageSha256 "$package_checksum" --arg releaseNotes "$release_notes" \
  '{Version: $version, InstallerUrl: $installerUrl, Sha256: $sha256, PackageUrl: $packageUrl, PackageSha256: $packageSha256, ReleaseNotes: $releaseNotes}' > "$manifest_path"

aws s3 cp "$update_package_path" "s3://${release_bucket}/${package_key}" --region "$aws_region" \
  --cache-control 'public,max-age=31536000,immutable'
aws s3 cp "$manifest_path" "s3://${release_bucket}/${manifest_key}" --region "$aws_region" --cache-control 'no-cache'
aws cloudfront create-invalidation --distribution-id "$distribution_id" --paths "/${manifest_key}" --no-cli-pager >/dev/null

echo "Published Coral ${version}"
echo "Manifest: https://${cloudfront_domain}/${manifest_key}"
