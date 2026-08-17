[CmdletBinding()]
param(
    [string]$DataDir = (Join-Path $env:LOCALAPPDATA "EZVIZ Local Monitor"),
    [switch]$Force,
    [switch]$NoPause
)

$ErrorActionPreference = "Stop"
$lockFile = Join-Path $DataDir "app-lock.protected"

if (-not $Force) {
    Write-Host "This resets only the EZVIZ Local Monitor app password/PIN." -ForegroundColor Yellow
    Write-Host "Camera settings, tokens, logs, events and images will be kept." -ForegroundColor Cyan
    $confirmation = Read-Host "Type RESET to continue"
    if ($confirmation -cne "RESET") {
        Write-Host "Cancelled. No file was changed." -ForegroundColor Yellow
        exit 0
    }
}

Get-Process -Name EzvizLocalMonitor -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue

if (Test-Path -LiteralPath $lockFile) {
    Remove-Item -LiteralPath $lockFile -Force
    Write-Host "App password/PIN reset successfully." -ForegroundColor Green
} else {
    Write-Host "No app lock credential was found. Nothing to reset." -ForegroundColor Gray
}

Write-Host "Start the application and set a new password or PIN in System Settings > App Security." -ForegroundColor Cyan
if (-not $NoPause) {
    Read-Host "Press Enter to close"
}
