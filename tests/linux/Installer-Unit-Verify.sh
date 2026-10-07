#!/usr/bin/env bash
# Verify the production unit in an isolated filesystem; never install a fixture to /opt.
set -euo pipefail
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
tmp=$(mktemp -d)
trap 'rm -rf -- "$tmp"' EXIT
mkdir -p "$tmp/etc/systemd/system" "$tmp/opt/ezviz-local-monitor/current/app"
cp "$root/installer/linux/ezviz-local-monitor.service" "$tmp/etc/systemd/system/"
chmod 0644 "$tmp/etc/systemd/system/ezviz-local-monitor.service"
printf '#!/bin/sh\nexit 0\n' > "$tmp/opt/ezviz-local-monitor/current/app/ezviz-headless"
chmod 0755 "$tmp/opt/ezviz-local-monitor/current/app/ezviz-headless"
for target in sysinit basic sockets shutdown network-online multi-user; do
    printf '[Unit]\nDescription=Isolated verify target\n' > "$tmp/etc/systemd/system/$target.target"
done
systemd-analyze verify --root="$tmp" --man=no ezviz-local-monitor.service
echo 'PASS: systemd parses production unit with executable path in isolated root (not live acceptance)'
