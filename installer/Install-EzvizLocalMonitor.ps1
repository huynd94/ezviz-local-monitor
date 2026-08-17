[CmdletBinding()]
param(
    [string]$InstallDir = "D:\EZVIZ-Local-Monitor",
    [switch]$ChooseLocation,
    [switch]$NoShortcut,
    [switch]$NoLaunch,
    [switch]$ForceUpdate
)

$ErrorActionPreference = "Stop"
# Windows PowerShell 5.1 needs both a UTF-8 BOM in this file and an explicit console encoding for Vietnamese output.
$utf8 = New-Object System.Text.UTF8Encoding($false)
[Console]::InputEncoding = $utf8
[Console]::OutputEncoding = $utf8
$OutputEncoding = $utf8
try { chcp 65001 | Out-Null } catch { }
Add-Type -AssemblyName System.Windows.Forms
$ApplicationName = "EZVIZ Local Monitor"
$ExecutableName = "EzvizLocalMonitor.exe"
$InstallerDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$PackageRoot = Split-Path -Parent $InstallerDirectory
$ApplicationSource = Join-Path $PackageRoot "app"
if (-not (Test-Path -LiteralPath (Join-Path $ApplicationSource $ExecutableName))) {
    # Fallback: hỗ trợ trường hợp script được đặt cùng thư mục app.
    $PackageRoot = $InstallerDirectory
    $ApplicationSource = Join-Path $PackageRoot "app"
}

if (-not (Test-Path (Join-Path $ApplicationSource $ExecutableName))) {
    throw "Không tìm thấy gói ứng dụng. Hãy chạy Install-EzvizLocalMonitor.ps1 từ thư mục bộ cài đầy đủ."
}

function Test-VcRuntimeInstalled {
    $systemDirectory = Join-Path $env:WINDIR "System32"
    return (Test-Path (Join-Path $systemDirectory "vcruntime140_1.dll")) -and
        (Test-Path (Join-Path $systemDirectory "msvcp140.dll"))
}

function Ensure-VcRuntime {
    if (Test-VcRuntimeInstalled) {
        Write-Host "Microsoft Visual C++ x64 Runtime đã sẵn sàng." -ForegroundColor DarkGreen
        return
    }

    $redist = Join-Path $InstallerDirectory "vc_redist.x64.exe"
    if (-not (Test-Path -LiteralPath $redist)) {
        throw "Thiếu vc_redist.x64.exe. Hãy chạy bộ cài đầy đủ v1.6.3, không chỉ sao chép riêng thư mục app."
    }

    Write-Host "Đang cài Microsoft Visual C++ x64 Runtime cần cho ONNX Runtime/OpenCV..." -ForegroundColor Cyan
    $process = Start-Process -FilePath $redist -ArgumentList @("/install", "/quiet", "/norestart") -Wait -PassThru
    if ($process.ExitCode -notin @(0, 1638, 3010) -or -not (Test-VcRuntimeInstalled)) {
        throw "Không cài được Microsoft Visual C++ x64 Runtime. ExitCode=$($process.ExitCode). Hãy chạy vc_redist.x64.exe bằng quyền Administrator rồi thử lại."
    }
    Write-Host "Đã cài Microsoft Visual C++ x64 Runtime." -ForegroundColor Green
}

Ensure-VcRuntime

if ($ChooseLocation) {
    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
    $dialog.Description = "Chọn thư mục cài đặt $ApplicationName"
    $dialog.SelectedPath = $InstallDir
    if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {
        Write-Host "Đã hủy cài đặt." -ForegroundColor Yellow
        exit 0
    }
    $InstallDir = $dialog.SelectedPath
}

if ((Test-Path -LiteralPath $InstallDir) -and -not $ForceUpdate) {
        $existing = Get-ChildItem -LiteralPath $InstallDir -Force -ErrorAction SilentlyContinue
        if ($existing.Count -gt 0) {

        $answer = [System.Windows.Forms.MessageBox]::Show(
            "Thư mục đã có dữ liệu. Cập nhật nội dung ứng dụng trong thư mục này? Dữ liệu cảnh báo tại LocalAppData sẽ không bị xóa.",
            $ApplicationName,
            [System.Windows.Forms.MessageBoxButtons]::YesNo,
            [System.Windows.Forms.MessageBoxIcon]::Question)
        if ($answer -ne [System.Windows.Forms.DialogResult]::Yes) { exit 0 }
    }
}

New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
Copy-Item -Path (Join-Path $ApplicationSource "*") -Destination $InstallDir -Recurse -Force

$uninstaller = @"
`$ErrorActionPreference = 'Stop'
`$target = Split-Path -Parent `$MyInvocation.MyCommand.Path
Get-Process EzvizLocalMonitor -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item -LiteralPath `$target -Recurse -Force
"@
Set-Content -LiteralPath (Join-Path $InstallDir "Uninstall-EzvizLocalMonitor.ps1") -Value $uninstaller -Encoding UTF8
$cleanUninstallerSource = Join-Path $PackageRoot "installer\Uninstall-EzvizLocalMonitor-Clean.ps1"
$startupRepairSource = Join-Path $PackageRoot "installer\Repair-EzvizLocalMonitorStartup.ps1"
if (Test-Path -LiteralPath $cleanUninstallerSource) {
    Copy-Item -LiteralPath $cleanUninstallerSource -Destination (Join-Path $InstallDir "Uninstall-EzvizLocalMonitor-Clean.ps1") -Force
}
if (Test-Path -LiteralPath $startupRepairSource) {
    Copy-Item -LiteralPath $startupRepairSource -Destination (Join-Path $InstallDir "Repair-EzvizLocalMonitorStartup.ps1") -Force
}

if (-not $NoShortcut) {
    $shell = New-Object -ComObject WScript.Shell
    $desktop = [Environment]::GetFolderPath("Desktop")
    $shortcut = $shell.CreateShortcut((Join-Path $desktop "$ApplicationName.lnk"))
    $shortcut.TargetPath = Join-Path $InstallDir $ExecutableName
    $shortcut.WorkingDirectory = $InstallDir
    $shortcut.Description = "Giám sát người cục bộ từ camera EZVIZ"
    $shortcut.Save()
}

Write-Host "Đã cài $ApplicationName vào: $InstallDir" -ForegroundColor Green
Write-Host "Dữ liệu và token được lưu mã hóa theo tài khoản Windows tại: %LOCALAPPDATA%\EZVIZ Local Monitor" -ForegroundColor Cyan
if (-not $NoLaunch) { Start-Process (Join-Path $InstallDir $ExecutableName) }
