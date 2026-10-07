#!/usr/bin/env bash
set -euo pipefail
root=$(realpath "$(dirname -- "${BASH_SOURCE[0]}")/../..")
version="${1:?Usage: Package-Headless.sh VERSION [OUTPUT_DIRECTORY]}"
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]] || { echo 'Invalid package version.' >&2; exit 2; }
out=$(realpath -m "${2:-$root/artifacts/dist}")
mkdir -p -- "$out"
staging=$(mktemp -d /tmp/ezviz-package.XXXXXXXX)
trap 'rm -rf -- "$staging"' EXIT
bash "$root/scripts/linux/Get-YoloModel.sh" "$root/assets/Models/yolov8n.onnx"
dotnet publish "$root/src/EzvizLocalMonitor.Headless/EzvizLocalMonitor.Headless.csproj" \
  -c Release -r linux-x64 --self-contained true -p:NuGetAudit=false \
  -p:Version="$version" -p:InformationalVersion="$version" -p:IncludeSourceRevisionInInformationalVersion=false \
  -o "$staging/app"
mkdir -p -- "$staging/installer/linux"
git -C "$root" rev-parse HEAD > "$staging/app/BUILD_COMMIT"
cp -L -- "$root/installer/linux/"* "$staging/installer/linux/"
cp -- "$root/README.md" "$staging/README.md"
printf '%s\n' "$version" > "$staging/VERSION"
find "$staging" -type f -name '*.pdb' -delete
# Microsoft's optional LTTng provider targets ABI0, unavailable on Ubuntu24.04
# (ABI1). This product uses file/journald/EventPipe logging, not LTTng tracing.
find "$staging/app" -type f -name 'libcoreclrtraceptprovider.so' -delete
find "$staging" -type d -exec chmod 755 {} +
find "$staging" -type f -exec chmod 644 {} +
chmod 755 "$staging/app/ezviz-headless"
find "$staging/installer/linux" -name '*.sh' -exec chmod 755 {} +
archive="EZVIZ-Local-Monitor-Linux-Headless-x64-v$version.tar.gz"
tar -czf "$out/$archive.tmp" -C "$staging" app installer VERSION README.md
mv -- "$out/$archive.tmp" "$out/$archive"
(cd -- "$out"; sha256sum "$archive" > "$archive.sha256sum")
python3 "$root/scripts/linux/Verify-Package.py" "$out/$archive" "$out/$archive.sha256sum"
echo "Package: $out/$archive"
