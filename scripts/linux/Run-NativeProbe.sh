#!/usr/bin/env bash
set -euo pipefail
source /etc/os-release
if [[ "$ID" != ubuntu || "$VERSION_ID" != 24.04 || "$(uname -m)" != x86_64 ]]; then
  printf '%s\n' 'Native acceptance requires Ubuntu 24.04 x64.' >&2
  exit 2
fi
root=$(realpath "$(dirname -- "${BASH_SOURCE[0]}")/../..")
candidate="${1:-baseline}"
native="${2:-false}"
[[ "$candidate" == baseline || "$candidate" == headless5 ]] || exit 2
[[ "$native" == true || "$native" == false ]] || exit 2
fixtures="$root/artifacts/native-fixtures"
mkdir -p -- "$fixtures"
bash "$root/scripts/linux/Get-YoloModel.sh" "$root/assets/Models/yolov8n.onnx"
ffmpeg -hide_banner -loglevel error -y -f lavfi -i testsrc2=size=640x480:rate=5 -t 3 -c:v libx264 -pix_fmt yuv420p "$fixtures/native-smoke.mp4"
export EZVIZ_TEST_VIDEO="$fixtures/native-smoke.mp4"
export EZVIZ_TEST_MODEL="$root/assets/Models/yolov8n.onnx"
unset DISPLAY WAYLAND_DISPLAY
dotnet test "$root/tests/EzvizLocalMonitor.NativeProbe.Tests/EzvizLocalMonitor.NativeProbe.Tests.csproj" -c Release -p:NativeCandidate="$candidate" -p:EnableNativeRuntime="$native" -p:NuGetAudit=false
