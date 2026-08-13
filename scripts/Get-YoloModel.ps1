[CmdletBinding()]
param(
    [string]$Destination = (Join-Path $PSScriptRoot "..\assets\Models\yolov8n.onnx")
)

$ErrorActionPreference = "Stop"
$utf8 = New-Object System.Text.UTF8Encoding($false)
[Console]::OutputEncoding = $utf8
$OutputEncoding = $utf8

$uri = "https://github.com/ultralytics/assets/releases/download/v8.4.0/yolov8n.onnx"
$destinationDirectory = Split-Path -Parent $Destination
New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null

Write-Host "Downloading local YOLO model..." -ForegroundColor Cyan
Invoke-WebRequest -Uri $uri -OutFile $Destination
$hash = (Get-FileHash -Algorithm SHA256 -Path $Destination).Hash.ToLowerInvariant()
Write-Host "Model saved to: $Destination" -ForegroundColor Green
Write-Host "SHA-256: $hash" -ForegroundColor Green
