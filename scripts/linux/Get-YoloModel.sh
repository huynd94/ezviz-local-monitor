#!/usr/bin/env bash
set -euo pipefail
destination="${1:?Usage: Get-YoloModel.sh DESTINATION}"
expected=b2bc52f40e8e1c532427d5bde3575a5d5b571b739fab2c6df443733ed1589cbd
mkdir -p -- "$(dirname -- "$destination")"
if [[ -f "$destination" ]] && [[ "$(sha256sum -- "$destination" | cut -d ' ' -f 1)" == "$expected" ]]; then
  exit 0
fi
temporary=$(mktemp "${destination}.XXXXXX")
trap 'rm -f -- "$temporary"' EXIT
curl --fail --location --silent --show-error https://github.com/ultralytics/assets/releases/download/v8.4.0/yolov8n.onnx --output "$temporary"
printf '%s  %s\n' "$expected" "$temporary" | sha256sum --check --status
mv -- "$temporary" "$destination"
