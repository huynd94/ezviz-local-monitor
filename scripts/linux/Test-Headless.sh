#!/usr/bin/env bash
set -euo pipefail
root=$(realpath "$(dirname -- "${BASH_SOURCE[0]}")/../..")
unset DISPLAY WAYLAND_DISPLAY
export EZVIZ_TEST_MODEL="$root/assets/Models/yolov8n.onnx"
dotnet build "$root/tests/EzvizLocalMonitor.TestChild/EzvizLocalMonitor.TestChild.csproj" -c Release -p:NuGetAudit=false
dotnet build "$root/src/EzvizLocalMonitor.Headless/EzvizLocalMonitor.Headless.csproj" -c Release -p:NuGetAudit=false
export EZVIZ_TEST_CHILD_PATH="$root/tests/EzvizLocalMonitor.TestChild/bin/Unix/Release/net8.0/EzvizLocalMonitor.TestChild.dll"
export EZVIZ_HEADLESS_EXE="$root/src/EzvizLocalMonitor.Headless/bin/Unix/Release/net8.0/linux-x64/ezviz-headless"
if [[ $# -gt 0 ]]; then
  dotnet test "$root/tests/EzvizLocalMonitor.Headless.Tests/EzvizLocalMonitor.Headless.Tests.csproj" -c Release -p:NuGetAudit=false --filter "$1"
else
  dotnet test "$root/tests/EzvizLocalMonitor.Headless.Tests/EzvizLocalMonitor.Headless.Tests.csproj" -c Release -p:NuGetAudit=false
fi
