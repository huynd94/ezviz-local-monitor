#!/usr/bin/env bash
# Run only after the primary has installed a real package and configured idle state.
# This test temporarily starts/stops the installed unit and restores its prior state.
set -euo pipefail
[[ $EUID == 0 ]] || { echo 'Run as root on Ubuntu 24.04 with systemd.'; exit 1; }
unit=ezviz-local-monitor.service
app=/opt/ezviz-local-monitor/current/app/ezviz-headless
state=/var/lib/ezviz-local-monitor
[[ -x "$app" && -f /etc/systemd/system/$unit ]] || { echo 'FAIL: real installation required'; exit 1; }
[[ $(systemctl show "$unit" -p User --value) == ezviz-monitor ]]
[[ $(id -u ezviz-monitor) != 0 ]]
[[ $(stat -c %a "$state") == 700 ]]
enabled=$(systemctl is-enabled "$unit" || true)
active=$(systemctl is-active "$unit" || true)
restore() {
    if [[ $active == active ]]; then systemctl start "$unit"; else systemctl stop "$unit"; fi
}
trap restore EXIT
before=$(find "$state" -type f \( -iname '*key*' -o -name 'settings.protected' \) -exec sha256sum {} + | sort)
systemd-analyze verify /etc/systemd/system/"$unit"
runuser -u ezviz-monitor -- "$app" config validate --data-dir "$state"
runuser -u ezviz-monitor -- "$app" doctor --data-dir "$state"
systemctl start "$unit"
sleep 7
systemctl is-active --quiet "$unit"
pid=$(systemctl show "$unit" -p MainPID --value)
[[ $pid -gt 0 && $(stat -c %u "/proc/$pid") == "$(id -u ezviz-monitor)" ]]
set +e
runuser -u ezviz-monitor -- "$app" run --data-dir "$state" >/dev/null 2>&1
duplicate=$?
set -e
[[ $duplicate == 3 ]]
systemctl stop "$unit"
[[ $(systemctl show "$unit" -p MainPID --value) == 0 ]]
after=$(find "$state" -type f \( -iname '*key*' -o -name 'settings.protected' \) -exec sha256sum {} + | sort)
[[ $before == "$after" ]]
[[ $(systemctl is-enabled "$unit" || true) == "$enabled" ]]
echo 'PASS: unit, non-root idle start/stop, duplicate lock, key/config hashes, enabled state'
echo 'Reboot/logout, missing-config exit2, real upgrade/rollback and camera soak require separate live acceptance.'
