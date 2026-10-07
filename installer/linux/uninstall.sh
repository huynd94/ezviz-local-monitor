#!/usr/bin/env bash
set -euo pipefail
source "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/common.sh"
[[ $# == 0 ]] || die 'Usage: uninstall.sh (state is always preserved)'
require_root_host
check_existing_install
[[ -d $PREFIX && -f $UNIT_PATH ]] || die 'No managed installation found.'
assert_program_tree
# Never traverse another filesystem when removing program files.
if mountpoint -q "$PREFIX"; then die 'Mounted program prefix requires manual review.'; fi
[[ -z $(find "$PREFIX" -mindepth 1 -type d -exec mountpoint -q {} \; -print -quit) ]] || die 'Mounted program directory requires manual review.'
systemctl stop "$UNIT"
systemctl disable "$UNIT"
rm -- "$UNIT_PATH"
systemctl daemon-reload
systemctl reset-failed "$UNIT" 2>/dev/null || true
rm -rf --one-file-system -- "$PREFIX"
echo "Uninstalled program and unit. Preserved $DATA and account $SERVICE_USER; no keys or settings were deleted."
