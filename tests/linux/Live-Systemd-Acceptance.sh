#!/usr/bin/env bash
set -euo pipefail
[[ $EUID == 0 ]] || { echo 'Run only with explicit root-install test approval.' >&2; exit 2; }
root=$(realpath "$(dirname -- "${BASH_SOURCE[0]}")/../..")
archive="${1:?Usage: Live-Systemd-Acceptance.sh ARCHIVE}"
prefix=/opt/ezviz-local-monitor
state=/var/lib/ezviz-local-monitor
unit=ezviz-local-monitor.service
if [[ -e "$prefix" || -L "$prefix" || -e "$state" || -e /etc/systemd/system/$unit ]] || getent passwd ezviz-monitor >/dev/null; then
  echo 'Refusing first-install acceptance over an existing installation/account.' >&2
  exit 2
fi
stage=$(mktemp -d /tmp/ezviz-live-install.XXXXXXXX)
trap 'rm -rf -- "$stage"' EXIT
python3 "$root/scripts/linux/Verify-Package.py" "$archive" "$archive.sha256sum" --extract "$stage/package"
bash "$stage/package/installer/linux/install.sh" --package-root "$stage/package"
[[ $(systemctl is-enabled "$unit" || true) == disabled ]]
[[ $(systemctl is-active "$unit" || true) == inactive ]]
[[ ! -e "$state/keys/master.key" && ! -e "$state/settings.protected" ]]
systemctl start "$unit"
deadline=$((SECONDS + 20))
while [[ $(systemctl show "$unit" -p ExecMainStatus --value) != 2 ]]; do
  (( SECONDS < deadline )) || { echo 'Missing configuration did not exit2.' >&2; exit 1; }
  sleep 0.1
done
[[ $(systemctl show "$unit" -p NRestarts --value) == 0 ]]
[[ ! -e "$state/keys/master.key" && ! -e "$state/settings.protected" ]]
systemctl stop "$unit"
systemctl reset-failed "$unit"
app="$prefix/current/app/ezviz-headless"
printf '{"Cameras":[]}' | runuser -u ezviz-monitor -- "$app" configure --stdin --data-dir "$state"
bash "$root/tests/linux/Systemd-Smoke.sh"
# Reinstallation of the identical release must not touch state or enable/start.
before=$(sha256sum "$state/keys/master.key" "$state/settings.protected")
bash "$stage/package/installer/linux/install.sh" --package-root "$stage/package"
[[ "$before" == "$(sha256sum "$state/keys/master.key" "$state/settings.protected")" ]]
[[ $(systemctl is-enabled "$unit" || true) == disabled ]]
[[ $(systemctl is-active "$unit" || true) == inactive ]]
echo 'PASS: real first install, missing-config exit2/no restart, idle service smoke and idempotent reinstall'
echo 'Environment retained: ezviz-monitor account, versioned /opt release, private idle state and disabled/inactive unit.'
