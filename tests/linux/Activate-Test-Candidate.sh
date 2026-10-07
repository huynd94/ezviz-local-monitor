#!/usr/bin/env bash
set -euo pipefail
[[ $EUID == 0 ]] || exit 2
root=$(realpath "$(dirname -- "${BASH_SOURCE[0]}")/../..")
archive="${1:?Usage: Activate-Test-Candidate.sh ARCHIVE}"
stage=$(mktemp -d /tmp/ezviz-final-candidate.XXXXXXXX)
trap 'rm -rf -- "$stage"' EXIT
python3 "$root/scripts/linux/Verify-Package.py" "$archive" "$archive.sha256sum" --extract "$stage/package"
bash "$stage/package/installer/linux/update.sh" --package-root "$stage/package"
bash "$root/tests/linux/Systemd-Smoke.sh"
systemctl stop ezviz-local-monitor.service
systemctl disable ezviz-local-monitor.service
readlink /opt/ezviz-local-monitor/current
echo 'PASS: final test candidate installed; service left disabled/inactive with private idle state'
