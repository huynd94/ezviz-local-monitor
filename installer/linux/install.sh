#!/usr/bin/env bash
set -euo pipefail
source "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/common.sh"
parse_package_args "$@"
require_root_host
check_existing_install
if [[ -L $PREFIX/current ]]; then
    existing=$(current_release)
    [[ $existing == "$PREFIX/releases/$VERSION" ]] || die 'Use update.sh for an existing installation.'
fi
# Stage under a root-owned parent, never execute from the supplied writable tree.
stage=$(mktemp -d /opt/.ezviz-stage.XXXXXXXX)
rmdir "$stage"
trap '[[ ! -d $stage ]] || rm -rf -- "$stage"' EXIT
stage_package "$PACKAGE" "$stage"
verify_binary "$stage" "$VERSION"
if [[ ! -d $PREFIX ]]; then
    install -d -o root -g root -m 0755 "$PREFIX" "$PREFIX/releases"
    printf 'ezviz-local-monitor-v1\n' > "$PREFIX/.managed-by-installer"
    chmod 0644 "$PREFIX/.managed-by-installer"
fi
publish_release "$stage"
if ! getent passwd "$SERVICE_USER" >/dev/null; then
    useradd --system --user-group --home-dir "$DATA" --no-create-home --shell /usr/sbin/nologin "$SERVICE_USER"
fi
if [[ ! -d $DATA ]]; then install -d -o "$SERVICE_USER" -g "$SERVICE_USER" -m 0700 "$DATA"; fi
switch_current "$RELEASE"
if [[ ! -f $UNIT_PATH ]]; then install -o root -g root -m 0644 "$SCRIPT_DIR/$UNIT" "$UNIT_PATH"; fi
systemctl daemon-reload
systemd-analyze verify "$UNIT_PATH"
echo "Installed $VERSION. Service enabled/active state was not changed. State is preserved at $DATA."
echo "Configure as $SERVICE_USER, then run config validate and doctor before systemctl enable --now $UNIT."
