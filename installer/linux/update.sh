#!/usr/bin/env bash
set -euo pipefail
source "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/common.sh"
parse_package_args "$@"
require_root_host
check_existing_install
old=$(current_release)
enabled=$(systemctl is-enabled "$UNIT" || true)
active=$(systemctl is-active "$UNIT" || true)
[[ $enabled == enabled || $enabled == disabled ]] || die "Unsupported unit state: $enabled"
[[ $active == active || $active == inactive ]] || die "Unsupported active state: $active (resolve failed/transitional state before updating)"
stage=$(mktemp -d /opt/.ezviz-stage.XXXXXXXX)
rmdir "$stage"
trap '[[ ! -d $stage ]] || rm -rf -- "$stage"' EXIT
stage_package "$PACKAGE" "$stage"
verify_binary "$stage" "$VERSION"
publish_release "$stage"
activate_release "$old" "$enabled" "$active" "$RELEASE"
echo "Updated to $VERSION; previous release retained at $old. Enabled=$enabled, prior active=$active. State preserved."
