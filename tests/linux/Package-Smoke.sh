#!/usr/bin/env bash
set -euo pipefail
root=$(realpath "$(dirname -- "${BASH_SOURCE[0]}")/../..")
archive="${1:?Usage: Package-Smoke.sh ARCHIVE}"
staging=$(mktemp -d /tmp/ezviz-package-smoke.XXXXXXXX)
trap 'rm -rf -- "$staging"' EXIT
python3 "$root/scripts/linux/Verify-Package.py" "$archive" "$archive.sha256sum" --extract "$staging/extracted"
app="$staging/extracted/app/ezviz-headless"
state="$staging/state"
version=$(cat "$staging/extracted/VERSION")
[[ "$("$app" version)" == "ezviz-headless $version linux-x64" ]]
printf '{"Cameras":[]}' | "$app" configure --stdin --data-dir "$state"
"$app" config validate --data-dir "$state"
env -u DISPLAY -u WAYLAND_DISPLAY "$app" doctor --data-dir "$state"
python3 - "$staging/extracted/app/ezviz-headless.deps.json" <<'PY'
import json, sys
libraries=json.load(open(sys.argv[1]))["libraries"]
for item in libraries:
    if item.startswith(("Avalonia", "OpenCvSharp5.runtime.win/", "System.Security.Cryptography.ProtectedData/")):
        raise SystemExit("Windows/GUI dependency in Linux package: " + item)
print("PASS: Linux dependency graph excludes GUI/Windows runtime/DPAPI")
PY
echo 'PASS: extracted self-contained executable, private idle configuration, native doctor and dependency graph'
