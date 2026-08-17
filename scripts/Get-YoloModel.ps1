[CmdletBinding()]
param(
    [string]$Destination = (Join-Path $PSScriptRoot "..\assets\Models\yolov8n.onnx")
)

$ErrorActionPreference = "Stop"
$utf8 = New-Object System.Text.UTF8Encoding($false)
[Console]::OutputEncoding = $utf8
$OutputEncoding = $utf8

$uri = "https://github.com/ultralytics/assets/releases/download/v8.4.0/yolov8n.onnx"
$expectedSha256 = "b2bc52f40e8e1c532427d5bde3575a5d5b571b739fab2c6df443733ed1589cbd"
$destinationDirectory = Split-Path -Parent $Destination
New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null

Write-Host "Downloading local YOLO model..." -ForegroundColor Cyan
try
{
    Invoke-WebRequest -Uri $uri -OutFile $Destination
    $hash = (Get-FileHash -Algorithm SHA256 -Path $Destination).Hash.ToLowerInvariant()
    if ($hash -ne $expectedSha256)
    {
        Remove-Item -LiteralPath $Destination -Force -ErrorAction SilentlyContinue
        throw "SHA-256 model không khớp. Expected=$expectedSha256 Actual=$hash"
    }
    Write-Host "Model saved to: $Destination" -ForegroundColor Green
    Write-Host "SHA-256: $hash" -ForegroundColor Green
}
catch
{
    Remove-Item -LiteralPath $Destination -Force -ErrorAction SilentlyContinue
    throw
}
