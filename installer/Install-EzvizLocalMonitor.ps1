[CmdletBinding()]
param(
    [string]$InstallDir = "D:\EZVIZ-Local-Monitor",
    [switch]$ChooseLocation,
    [switch]$NoShortcut,
    [switch]$NoLaunch
)

$ErrorActionPreference = "Stop"
# Windows PowerShell 5.1 needs both a UTF-8 BOM in this file and an explicit console encoding for Vietnamese output.
$utf8 = New-Object System.Text.UTF8Encoding($false)
[Console]::InputEncoding = $utf8
[Console]::OutputEncoding = $utf8
$OutputEncoding = $utf8
try { chcp 65001 | Out-Null } catch { }
Add-Type -AssemblyName System.Windows.Forms
$PackageRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ApplicationSource = Join-Path $PackageRoot "app"
$ApplicationName = "EZVIZ Local Monitor"
$ExecutableName = "EzvizLocalMonitor.exe"

if (-not (Test-Path (Join-Path $ApplicationSource $ExecutableName))) {
    throw "Không tìm thấy gói ứng dụng. Hãy chạy Install-EzvizLocalMonitor.ps1 từ thư mục bộ cài đầy đủ."
}

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

if (Test-Path $InstallDir) {
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
