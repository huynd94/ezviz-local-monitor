#!/usr/bin/env bash
set -euo pipefail
PREFIX=/opt/ezviz-local-monitor
DATA=/var/lib/ezviz-local-monitor
SERVICE_USER=ezviz-monitor
UNIT=ezviz-local-monitor.service
UNIT_PATH=/etc/systemd/system/$UNIT
SCRIPT_DIR=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)

die() { echo "ERROR: $*" >&2; exit 1; }

validate_version() {
    local version=$1 identifier
    [[ $version =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?(\+[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$ ]] || die 'Unsafe or invalid semantic version.'
    if [[ $version == *-* ]]; then
        local prerelease=${version#*-}
        prerelease=${prerelease%%+*}
        local -a identifiers
        IFS=. read -r -a identifiers <<< "$prerelease"
        for identifier in "${identifiers[@]}"; do
            [[ ! $identifier =~ ^0[0-9]+$ ]] || die 'Numeric prerelease identifiers cannot have leading zeros.'
        done
    fi
}

parse_package_args() {
    [[ $# == 2 && $1 == --package-root ]] || die 'Usage: --package-root PATH (an already extracted, SHA-256 verified package)'
    [[ ! -L $2 ]] || die 'Package root cannot be a symlink.'
    PACKAGE=$(realpath -e -- "$2") || die 'Package root does not exist.'
    validate_package "$PACKAGE"
    VERSION=$(cat -- "$PACKAGE/VERSION")
}

validate_package() {
    local package=$1 entry
    [[ -d $package && ! -L $package ]] || die 'Package must be a real directory.'
    # Refuse links and special files before privileged copying or executing anything.
    [[ -z $(find "$package" -mindepth 1 ! -type d ! -type f -print -quit) ]] || die 'Package contains symlinks or special files.'
    for entry in VERSION README.md app/ezviz-headless installer/linux/install.sh installer/linux/update.sh installer/linux/uninstall.sh installer/linux/common.sh installer/linux/ezviz-local-monitor.service; do
        [[ -f $package/$entry ]] || die "Missing package file: $entry"
    done
    [[ $(wc -l < "$package/VERSION") == 1 ]] || die 'VERSION must be exactly one newline-terminated ASCII line.'
    local version
    version=$(cat -- "$package/VERSION")
    LC_ALL=C validate_version "$version"
    [[ $(wc -c < "$package/VERSION") == $((${#version} + 1)) ]] || die 'VERSION contains non-ASCII or embedded NUL bytes.'
    [[ -z $(find "$package" -mindepth 1 -maxdepth 1 ! -name app ! -name installer ! -name VERSION ! -name README.md -print -quit) ]] || die 'Unexpected package-root entries.'
}

require_root_host() {
    [[ $EUID == 0 ]] || die 'Run as root.'
    # os-release also defines VERSION; keep it from replacing the package version.
    local ID VERSION_ID VERSION
    source /etc/os-release
    [[ $ID == ubuntu && $VERSION_ID == 24.04 && $(uname -m) == x86_64 ]] || die 'Ubuntu 24.04 x64 required.'
    [[ -d /run/systemd/system ]] || die 'A running systemd host is required.'
    local tool
    for tool in flock runuser useradd systemctl systemd-analyze ldd readelf sha256sum timeout; do
        command -v "$tool" >/dev/null || die "Missing prerequisite: $tool (no packages are installed automatically)"
    done
    assert_secure_path /opt
    assert_secure_path /var/lib
    assert_secure_path /etc/systemd/system
    [[ ! -L /run/lock/ezviz-local-monitor-installer.lock ]] || die 'Installer lock is a symlink.'
    exec 9>/run/lock/ezviz-local-monitor-installer.lock
    flock -n 9 || die 'Another installer is running.'
}

assert_secure_path() {
    local path=$1 current=/ part mode
    local -a parts
    IFS=/ read -r -a parts <<< "$path"
    for part in "${parts[@]}"; do
        [[ -n $part ]] || continue
        current=${current%/}/$part
        [[ -d $current && ! -L $current && $(stat -c %u "$current") == 0 ]] || die "Unsafe privileged directory: $current"
        mode=$(stat -c %a "$current")
        (( (8#$mode & 0022) == 0 )) || die "Writable privileged directory: $current"
    done
}

assert_program_tree() {
    assert_secure_path "$PREFIX"
    [[ -f $PREFIX/.managed-by-installer && ! -L $PREFIX/.managed-by-installer ]] || die 'Existing prefix is not installer-managed.'
    [[ $(cat "$PREFIX/.managed-by-installer") == ezviz-local-monitor-v1 ]] || die 'Unknown installation marker.'
    local path mode
    while IFS= read -r -d '' path; do
        [[ $path == "$PREFIX/current" && -L $path ]] && continue
        [[ ! -L $path && ( -f $path || -d $path ) && $(stat -c %u "$path") == 0 ]] || die "Unsafe program entry: $path"
        mode=$(stat -c %a "$path")
        (( (8#$mode & 0022) == 0 )) || die "Writable program entry: $path"
    done < <(find "$PREFIX" -mindepth 1 -print0)
    [[ ! -e $PREFIX/current && ! -L $PREFIX/current ]] || current_release >/dev/null
}

current_release() {
    [[ -L $PREFIX/current ]] || die 'current must be a symlink.'
    local target version
    target=$(readlink -- "$PREFIX/current")
    [[ $target == "$PREFIX/releases/"* ]] || die 'current target escapes releases.'
    version=${target#"$PREFIX/releases/"}
    validate_version "$version"
    [[ -d $target && ! -L $target && $(realpath -e -- "$target") == "$target" ]] || die 'Invalid current release.'
    printf '%s\n' "$target"
}

check_existing_install() {
    if [[ -e $PREFIX || -L $PREFIX ]]; then assert_program_tree; fi
    if [[ -e $UNIT_PATH || -L $UNIT_PATH ]]; then
        [[ -d $PREFIX && ! -L $UNIT_PATH && -f $UNIT_PATH && $(stat -c %u "$UNIT_PATH") == 0 ]] || die 'Unexpected existing unit.'
        cmp -s -- "$SCRIPT_DIR/$UNIT" "$UNIT_PATH" || die 'Existing unit differs; refusing to overwrite it.'
    elif [[ -e $PREFIX ]]; then
        die 'Managed prefix exists without its unit; inspect the interrupted installation manually.'
    elif [[ -n $(systemctl show "$UNIT" -p FragmentPath --value) ]]; then
        die 'A unit from another location already exists.'
    fi
    [[ ! -e /etc/systemd/system/$UNIT.d && ! -L /etc/systemd/system/$UNIT.d ]] || die 'Existing unit overrides require manual review.'
    [[ -z $(systemctl show "$UNIT" -p DropInPaths --value) ]] || die 'Existing unit drop-ins require manual review.'
    if getent passwd "$SERVICE_USER" >/dev/null; then
        local name password uid gid gecos home shell
        IFS=: read -r name password uid gid gecos home shell < <(getent passwd "$SERVICE_USER")
        [[ $uid != 0 && $home == "$DATA" && ( $shell == /usr/sbin/nologin || $shell == /sbin/nologin ) ]] || die 'Unexpected existing service account.'
    fi
    if [[ -e $DATA || -L $DATA ]]; then
        [[ -d $DATA && ! -L $DATA ]] || die 'Unsafe data root.'
        getent passwd "$SERVICE_USER" >/dev/null || die 'Existing data without the expected account.'
        [[ $(stat -c %u "$DATA") == "$(id -u "$SERVICE_USER")" && $(stat -c %a "$DATA") == 700 ]] || die 'Existing data root owner/mode requires manual review.'
        [[ ! -L $DATA/keys ]] || die 'Key directory cannot be a symlink.'
        if [[ -e $DATA/keys/master.key || -L $DATA/keys/master.key ]]; then
            [[ -f $DATA/keys/master.key && ! -L $DATA/keys/master.key && $(stat -c %a "$DATA/keys/master.key") == 600 && $(stat -c %u "$DATA/keys/master.key") == "$(id -u "$SERVICE_USER")" ]] || die 'Existing private key owner/mode requires manual review.'
        fi
    fi
}

stage_package() {
    local package=$1 destination=$2
    [[ ! -e $destination && ! -L $destination ]] || die 'Stage already exists.'
    mkdir -m 0700 -- "$destination"
    cp -R --no-preserve=ownership,mode -- "$package/." "$destination/"
    # Recheck the copy too, to catch source changes while copying.
    validate_package "$destination"
    find "$destination" -type d -exec chmod 0755 {} +
    find "$destination" -type f -exec chmod 0644 {} +
    chmod 0755 "$destination/app/ezviz-headless"
    find "$destination/installer/linux" -name '*.sh' -exec chmod 0755 {} +
    [[ $EUID != 0 ]] || chown -R root:root -- "$destination"
}

verify_binary() {
    local release=$1 version=$2 file output library_path
    library_path=$(find "$release/app" -type d -printf '%p:' )
    # readelf does not execute the candidate; ldd is used only for ELF files.
    readelf -h "$release/app/ezviz-headless" | grep -q 'Advanced Micro Devices X86-64' || die 'Expected Linux x64 ELF executable.'
    while IFS= read -r -d '' file; do
        readelf -h "$file" >/dev/null 2>&1 || continue
        output=$(timeout 30 runuser -u nobody -- env "LD_LIBRARY_PATH=$library_path" ldd "$file" 2>&1) || die "Cannot inspect native dependencies: $file"
        if grep -Eiq 'not found|libgtk|libgdk|libX11|libAvalonia' <<< "$output"; then
            die "Missing or GUI native dependency: $file"
        fi
    done < <(find "$release/app" -type f -print0)
    output=$(timeout 30 runuser -u nobody -- "$release/app/ezviz-headless" version) || die 'Package version command failed.'
    [[ $output == "ezviz-headless $version linux-x64" ]] || die 'Binary version does not match VERSION.'
}

publish_release() {
    local stage=$1 target=$PREFIX/releases/$VERSION
    if [[ -e $target || -L $target ]]; then
        [[ -d $target && ! -L $target ]] || die 'Invalid existing release.'
        diff -qr -- "$stage" "$target" >/dev/null || die 'Version already installed with different content; use a new version.'
        rm -rf -- "$stage"
    else
        mv -- "$stage" "$target"
    fi
    RELEASE=$target
}

switch_current() {
    local target=$1 link
    [[ $target == "$PREFIX/releases/"* && -d $target && ! -L $target ]] || die 'Invalid switch target.'
    validate_version "${target#"$PREFIX/releases/"}"
    link=$PREFIX/.current.$$
    [[ ! -e $link && ! -L $link ]] || die 'Temporary current link exists.'
    ln -s -- "$target" "$link"
    mv -Tf -- "$link" "$PREFIX/current"
}

validate_health() {
    local app=$PREFIX/current/app/ezviz-headless
    timeout 60 runuser -u "$SERVICE_USER" -- "$app" config validate --data-dir "$DATA" || return $?
    timeout 120 runuser -u "$SERVICE_USER" -- "$app" doctor --data-dir "$DATA"
}

start_healthy() {
    # Disabled/inactive units can be garbage-collected by systemd. Only reset
    # a loaded failed unit; start will load an inactive unit from its unit file.
    if systemctl is-failed --quiet "$UNIT"; then systemctl reset-failed "$UNIT" || return $?; fi
    systemctl start "$UNIT" || return $?
    sleep 7
    systemctl is-active --quiet "$UNIT" || return $?
    [[ $(systemctl show "$UNIT" -p NRestarts --value) == 0 ]]
}

activate_release() (
    # A subshell gives the transaction its own signal/exit traps.
    set -euo pipefail
    old=$1 enabled=$2 active=$3 release=$4
    transaction=1
    rollback() {
        local result=$?
        trap - EXIT INT TERM
        if (( transaction )); then
            echo 'Update failed; restoring previous release and unit state.' >&2
            systemctl stop "$UNIT" || result=1
            switch_current "$old" || result=1
            if [[ $enabled == enabled ]]; then systemctl enable "$UNIT" || result=1; else systemctl disable "$UNIT" || result=1; fi
            if [[ $active == active ]]; then start_healthy || { echo 'ERROR: old service could not be restarted.' >&2; result=1; }; fi
        fi
        exit "$result"
    }
    trap rollback EXIT
    trap 'exit 130' INT
    trap 'exit 143' TERM
    systemctl stop "$UNIT" || exit 1
    switch_current "$release" || exit 1
    validate_health || exit 1
    if [[ $active == active ]]; then start_healthy || exit 1; fi
    [[ $(systemctl is-enabled "$UNIT" || true) == "$enabled" ]] || die 'Enabled state changed unexpectedly.'
    transaction=0
)
