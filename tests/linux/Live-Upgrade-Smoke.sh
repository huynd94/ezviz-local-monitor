#!/usr/bin/env bash
set -euo pipefail
[[ $EUID == 0 ]] || exit 2
root=$(realpath "$(dirname -- "${BASH_SOURCE[0]}")/../..")
new_archive="${1:?Usage: Live-Upgrade-Smoke.sh NEW_ARCHIVE OLD_ARCHIVE}"
old_archive="${2:?Usage: Live-Upgrade-Smoke.sh NEW_ARCHIVE OLD_ARCHIVE}"
unit=ezviz-local-monitor.service
state=/var/lib/ezviz-local-monitor
prefix=/opt/ezviz-local-monitor
old_target=$(readlink "$prefix/current")
prior_enabled=$(systemctl is-enabled "$unit" || true)
prior_active=$(systemctl is-active "$unit" || true)
stage=$(mktemp -d /tmp/ezviz-live-upgrade.XXXXXXXX)
restore() {
  if [[ $prior_active == active ]]; then systemctl start "$unit"; else systemctl stop "$unit"; fi
  if [[ $prior_enabled == enabled ]]; then systemctl enable "$unit"; else systemctl disable "$unit"; fi
  rm -rf -- "$stage"
}
trap restore EXIT
python3 "$root/scripts/linux/Verify-Package.py" "$new_archive" "$new_archive.sha256sum" --extract "$stage/new"
python3 "$root/scripts/linux/Verify-Package.py" "$old_archive" "$old_archive.sha256sum" --extract "$stage/old"
before=$(sha256sum "$state/keys/master.key" "$state/settings.protected" "$state/events.db")
systemctl enable --now "$unit"
sleep 2
bash "$stage/new/installer/linux/update.sh" --package-root "$stage/new"
[[ $(readlink "$prefix/current") != "$old_target" ]]
[[ $(systemctl is-enabled "$unit") == enabled ]]
[[ $(systemctl is-active "$unit") == active ]]
[[ "$before" == "$(sha256sum "$state/keys/master.key" "$state/settings.protected" "$state/events.db")" ]]
bash "$stage/old/installer/linux/update.sh" --package-root "$stage/old"
[[ $(readlink "$prefix/current") == "$old_target" ]]
[[ $(systemctl is-enabled "$unit") == enabled ]]
[[ $(systemctl is-active "$unit") == active ]]
[[ "$before" == "$(sha256sum "$state/keys/master.key" "$state/settings.protected" "$state/events.db")" ]]
echo 'PASS: real active/enabled upgrade and explicit rollback preserve key/settings/database and unit state'
