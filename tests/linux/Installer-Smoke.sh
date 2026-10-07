#!/usr/bin/env bash
set -euo pipefail
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
[[ -f "$root/installer/linux/common.sh" ]] || { echo 'FAIL: installer absent'; exit 1; }
source "$root/installer/linux/common.sh"
passed=0
reject() {
    if ( "$@" ) >/dev/null 2>&1; then echo "FAIL: accepted $*"; exit 1; fi
    passed=$((passed + 1))
}
for version in '../escape' '1.2' '01.2.3' '1.2.3/evil' '1.2.3-preview..1' '1.2.3-preview.01'; do
    reject validate_version "$version"
done
validate_version '1.9.0-preview.1'
passed=$((passed + 1))
tmp=$(mktemp -d)
chmod 0755 "$tmp"
trap 'rm -rf -- "$tmp"' EXIT
mkdir -p "$tmp/package/app" "$tmp/package/installer/linux"
printf '1.9.0-preview.1\n' > "$tmp/package/VERSION"
printf 'fixture\n' > "$tmp/package/README.md"
cp "$root/installer/linux/"* "$tmp/package/installer/linux/"
cp /bin/true "$tmp/package/app/ezviz-headless"
validate_package "$tmp/package"
passed=$((passed + 1))
ln -s /etc/passwd "$tmp/package/app/escape"
reject validate_package "$tmp/package"
rm "$tmp/package/app/escape"
printf '1.9.0-preview.1\nextra\n' > "$tmp/package/VERSION"
reject validate_package "$tmp/package"
printf '../escape\n' > "$tmp/package/VERSION"
reject validate_package "$tmp/package"
printf '1.9.0-preview.1\0\n' > "$tmp/package/VERSION"
reject validate_package "$tmp/package"
printf '1.9.0-preview.1\n' > "$tmp/package/VERSION"
mkfifo "$tmp/package/app/pipe"
reject validate_package "$tmp/package"
rm "$tmp/package/app/pipe"
chmod 0666 "$tmp/package/app/ezviz-headless"
# Copying must normalize writable source permissions; the source stays unchanged.
stage_package "$tmp/package" "$tmp/staged"
[[ $(stat -c %a "$tmp/staged/app/ezviz-headless") == 755 ]]
[[ $(stat -c %a "$tmp/staged/VERSION") == 644 ]]
[[ $(stat -c %a "$tmp/package/app/ezviz-headless") == 666 ]]
passed=$((passed + 1))
reject verify_binary "$tmp/staged" '1.9.0-preview.1'
# Refusing an existing changed version protects rollback binaries from replacement.
PREFIX=$tmp/program
VERSION=1.9.0-preview.1
mkdir -p "$PREFIX/releases"
publish_release "$tmp/staged"
switch_current "$RELEASE"
[[ $(current_release) == "$tmp/program/releases/1.9.0-preview.1" ]]
passed=$((passed + 1))
stage_package "$tmp/package" "$tmp/identical"
publish_release "$tmp/identical"
[[ ! -e $tmp/identical ]]
passed=$((passed + 1))
stage_package "$tmp/package" "$tmp/changed"
printf 'changed\n' >> "$tmp/changed/README.md"
reject publish_release "$tmp/changed"
[[ $(cat "$RELEASE/README.md") == fixture ]]
ln -sfn /etc "$PREFIX/current"
reject current_release
ln -sfn "$PREFIX/releases/../escape" "$PREFIX/current"
reject current_release
ln -s /etc "$PREFIX/releases/2.0.0"
ln -sfn "$PREFIX/releases/2.0.0" "$PREFIX/current"
reject current_release
for file in "$root/installer/linux/"*.sh "$root/tests/linux/"*Smoke.sh; do bash -n "$file"; done
echo "PASS: $passed installer behavior checks + shell syntax"
