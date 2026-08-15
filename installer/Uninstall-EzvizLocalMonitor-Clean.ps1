[CmdletBinding()]
param(
    [string]$InstallDir,
    [switch]$Force,
    [switch]$KeepData,
    [switch]$KeepUpdaterTemp,
    [switch]$NoPause
)

$ErrorActionPreference = "Stop"
$ApplicationName = "EZVIZ Local Monitor"
$ProcessName = "EzvizLocalMonitor"
$TaskName = "EZVIZ Local Monitor"
if ([string]::IsNullOrWhiteSpace($InstallDir)) { $InstallDir = $PSScriptRoot }
$DefaultDataDir = Join-Path $env:LOCALAPPDATA $ApplicationName
$SelfPath = $MyInvocation.MyCommand.Path
$DesktopShortcut = Join-Path ([Environment]::GetFolderPath("Desktop")) "$ApplicationName.lnk"
$StartMenuShortcut = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\$ApplicationName.lnk"

function Write-Step([string]$Message) {
    Write-Host "[EZVIZ] $Message" -ForegroundColor Cyan
}

function Remove-Safely([string]$Path, [string]$Description) {
    if (-not (Test-Path -LiteralPath $Path)) {
        Write-Host "  Bỏ qua: không tìm thấy $Description" -ForegroundColor DarkGray
        return
    }
    Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
    Write-Host "  Đã xóa: $Description" -ForegroundColor Green
}

if (-not [Environment]::Is64BitOperatingSystem) {
    Write-Warning "Ứng dụng này chỉ phát hành cho Windows x64; script vẫn tiếp tục dọn theo đường dẫn đã chọn."
}

$InstallDir = [IO.Path]::GetFullPath($InstallDir)
if (-not $Force) {
    Write-Host "Thao tác này sẽ gỡ $ApplicationName và xóa dữ liệu cục bộ." -ForegroundColor Yellow
    Write-Host "Thư mục chương trình: $InstallDir" -ForegroundColor Yellow
    if ($KeepData) {
        Write-Host "Chế độ hiện tại: giữ lại $DefaultDataDir" -ForegroundColor Green
    } else {
        Write-Host "Chế độ hiện tại: XÓA cả cấu hình, token mã hóa, log, database và ảnh sự kiện tại $DefaultDataDir" -ForegroundColor Red
    }
    $answer = Read-Host "Nhập REMOVE để tiếp tục"
    if ($answer -cne "REMOVE") {
        Write-Host "Đã hủy, không có dữ liệu nào bị xóa." -ForegroundColor Yellow
        exit 0
    }
}

Write-Step "Dừng tiến trình và watchdog"
Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 800

Write-Step "Xóa Task Scheduler khởi động cùng Windows"
try {
    & schtasks.exe /Delete /TN $TaskName /F 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  Đã xóa task: $TaskName" -ForegroundColor Green
    } else {
        Write-Host "  Task không tồn tại hoặc đã được xóa trước đó." -ForegroundColor DarkGray
    }
} catch {
    Write-Warning "Không xóa được Task Scheduler: $($_.Exception.Message)"
}

Write-Step "Xóa shortcut"
Remove-Safely $DesktopShortcut "shortcut ngoài Desktop"
Remove-Safely $StartMenuShortcut "shortcut trong Start Menu"

Write-Step "Xóa thư mục chương trình"
$installDirContainsThisScript = $false
try { $installDirContainsThisScript = [StringComparer]::OrdinalIgnoreCase.Equals((Split-Path -Parent $SelfPath).TrimEnd('\\'), $InstallDir.TrimEnd('\\')) } catch { }
if ($installDirContainsThisScript) {
    $deferredCmd = Join-Path ([IO.Path]::GetTempPath()) ("EZVIZ-Uninstall-" + [guid]::NewGuid().ToString('N') + ".cmd")
    $escapedInstallDir = $InstallDir.Replace('"', '""')
    @("@echo off", "timeout /t 2 /nobreak >nul", "rmdir /s /q \"$escapedInstallDir\"", "del /f /q \"%~f0\" >nul 2>&1") | Set-Content -LiteralPath $deferredCmd -Encoding ASCII
    Start-Process -FilePath "cmd.exe" -ArgumentList @('/c', $deferredCmd) -WindowStyle Hidden
    Write-Host "  Đã lên lịch xóa thư mục sau khi script kết thúc: $InstallDir" -ForegroundColor Green
} else {
    try {
        Remove-Safely $InstallDir "thư mục cài đặt"
    } catch {
        Write-Warning "Không xóa hết thư mục cài đặt: $($_.Exception.Message)"
    }
}

if (-not $KeepData) {
    Write-Step "Xóa dữ liệu cục bộ"
    Remove-Safely $DefaultDataDir "cấu hình, token mã hóa, log, database và ảnh sự kiện"
} else {
    Write-Host "Giữ lại dữ liệu cục bộ: $DefaultDataDir" -ForegroundColor Green
}

if (-not $KeepUpdaterTemp) {
    Write-Step "Dọn các thư mục tạm của updater"
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    Get-ChildItem -LiteralPath $tempRoot -Directory -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like "EZVIZ-GUI-*" -or $_.Name -like "EZVIZ-Update-*" -or $_.Name -like "EZVIZ-AutoUpdater-*" } |
        ForEach-Object {
            try { Remove-Safely $_.FullName "thư mục tạm updater $($_.Name)" } catch { Write-Warning "Không xóa được $($_.FullName)" }
        }
}

Write-Host "" 
Write-Host "Đã hoàn tất gỡ $ApplicationName." -ForegroundColor Green
if ($KeepData) {
    Write-Host "Dữ liệu vẫn còn tại: $DefaultDataDir" -ForegroundColor Cyan
} else {
    Write-Host "Đã xóa cả chương trình và dữ liệu cục bộ." -ForegroundColor Cyan
}
if (-not $NoPause -and -not $Force) {
    Read-Host "Nhấn Enter để đóng"
}
