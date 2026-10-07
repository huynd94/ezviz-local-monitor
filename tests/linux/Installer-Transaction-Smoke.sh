#!/usr/bin/env bash
# Real release symlink/state-file operations; only the systemd/application health
# boundary is simulated. This does not claim live systemd or native acceptance.
set -euo pipefail
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
source "$root/installer/linux/common.sh"
tmp=$(mktemp -d)
trap 'rm -rf -- "$tmp"' EXIT
PREFIX=$tmp/program
DATA=$tmp/state
mkdir -p "$PREFIX/releases/1.0.0" "$PREFIX/releases/2.0.0" "$DATA/keys"
printf 'fixture only\n' > "$DATA/keys/master.key"
printf '{"Cameras":[]}\n' > "$DATA/settings.json"
printf 'fixture database bytes\n' > "$DATA/events.db"
printf 'fixture image bytes\n' > "$DATA/snapshot.jpg"
before=$(sha256sum "$DATA/keys/master.key" "$DATA/settings.json" "$DATA/events.db" "$DATA/snapshot.jpg")
old=$PREFIX/releases/1.0.0
new=$PREFIX/releases/2.0.0
systemctl() {
    case $1 in
        stop) printf inactive > "$tmp/active" ;;
        enable) printf enabled > "$tmp/enabled" ;;
        disable) printf disabled > "$tmp/enabled" ;;
        is-enabled) cat "$tmp/enabled" ;;
        *) echo "Unexpected systemctl boundary: $*" >&2; return 1 ;;
    esac
}
validate_health() { [[ ${health_fail:-0} == 0 ]]; }
start_healthy() {
    if [[ ${start_fail:-0} == 1 && $(current_release) == "$new" ]]; then return 1; fi
    printf active > "$tmp/active"
}
passed=0
for enabled in enabled disabled; do
    for active in active inactive; do
        for failure in none health startup; do
            printf '%s' "$enabled" > "$tmp/enabled"
            printf '%s' "$active" > "$tmp/active"
            switch_current "$old"
            health_fail=0 start_fail=0
            [[ $failure != health ]] || health_fail=1
            [[ $failure != startup ]] || start_fail=1
            expected=$new
            if [[ $failure == health || ( $failure == startup && $active == active ) ]]; then
                expected=$old
                if activate_release "$old" "$enabled" "$active" "$new" >/dev/null 2>&1; then echo 'FAIL: health failure was accepted'; exit 1; fi
            else
                activate_release "$old" "$enabled" "$active" "$new"
            fi
            [[ $(current_release) == "$expected" ]]
            [[ $(cat "$tmp/enabled") == "$enabled" && $(cat "$tmp/active") == "$active" ]]
            [[ -d $old && -d $new ]]
            [[ $(sha256sum "$DATA/keys/master.key" "$DATA/settings.json" "$DATA/events.db" "$DATA/snapshot.jpg") == "$before" ]]
            passed=$((passed + 1))
        done
    done
done
echo "PASS: $passed update/rollback cases (enabled/disabled × active/inactive × success/health/startup failure); state hashes preserved"
