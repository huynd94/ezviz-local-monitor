#!/usr/bin/env bash
set -euo pipefail
[[ $EUID == 0 ]] || exit 2
root=$(realpath "$(dirname -- "${BASH_SOURCE[0]}")/../..")
archive="${1:?Usage: Live-Failed-Upgrade-Smoke.sh FRESH_CANDIDATE_ARCHIVE}"
unit=ezviz-local-monitor.service
prefix=/opt/ezviz-local-monitor
state=/var/lib/ezviz-local-monitor
old=$(readlink "$prefix/current")
enabled=$(systemctl is-enabled "$unit" || true)
active=$(systemctl is-active "$unit" || true)
stage=$(mktemp -d /tmp/ezviz-live-failed-update.XXXXXXXX)
restore() {
  if [[ $active == active ]]; then systemctl start "$unit"; else systemctl stop "$unit"; fi
  if [[ $enabled == enabled ]]; then systemctl enable "$unit"; else systemctl disable "$unit"; fi
  rm -rf -- "$stage"
}
trap restore EXIT
python3 "$root/scripts/linux/Verify-Package.py" "$archive" "$archive.sha256sum" --extract "$stage/package"
version=$(cat "$stage/package/VERSION")
[[ ! -e "$prefix/releases/$version" ]] || { echo 'Use a fresh candidate version for negative acceptance.' >&2; exit 2; }
# Deliberate local fault injection after verified extraction: metadata/binary match,
# but doctor will reject the model after activation. No user state is modified.
printf 'acceptance-invalid-model\n' > "$stage/package/app/Models/yolov8n.onnx"
before=$(sha256sum "$state/keys/master.key" "$state/settings.protected" "$state/events.db")
systemctl start "$unit"
set +e
bash "$stage/package/installer/linux/update.sh" --package-root "$stage/package"
result=$?
set -e
[[ $result != 0 ]]
[[ $(readlink "$prefix/current") == "$old" ]]
[[ $(systemctl is-active "$unit") == active ]]
[[ $(systemctl is-enabled "$unit" || true) == "$enabled" ]]
[[ "$before" == "$(sha256sum "$state/keys/master.key" "$state/settings.protected" "$state/events.db")" ]]
echo 'PASS: failed real doctor validation automatically rolls back current and prior active/enabled state without modifying key/config/database'
echo "Negative fixture release retained for audit: $prefix/releases/$version (not current)."
