[CmdletBinding()]
param(
    [string]$InstallDir,
    [switch]$Disable,
    [switch]$RunNow,
    [switch]$NoPause
)

$ErrorActionPreference = "Stop"
try {
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    [Console]::InputEncoding = $utf8
    [Console]::OutputEncoding = $utf8
    $OutputEncoding = $utf8
    chcp 65001 | Out-Null
} catch { }
$TaskName = "EZVIZ Local Monitor"
if ([string]::IsNullOrWhiteSpace($InstallDir)) { $InstallDir = $PSScriptRoot }
$InstallDir = [IO.Path]::GetFullPath($InstallDir)
$Executable = Join-Path $InstallDir "EzvizLocalMonitor.exe"

function Invoke-Schtasks([string[]]$Arguments) {
    $output = & schtasks.exe @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "schtasks.exe thất bại (exit $LASTEXITCODE): $($output -join ' ')"
    }
    return $output
}

if ($Disable) {
    try {
        Invoke-Schtasks @('/Delete', '/TN', $TaskName, '/F') | Out-Null
        Write-Host "Đã tắt task khởi động cùng Windows: $TaskName" -ForegroundColor Green
    } catch {
        Write-Warning $_.Exception.Message
    }
    if (-not $NoPause) { Read-Host "Nhấn Enter để đóng" }
    exit 0
}

if (-not (Test-Path -LiteralPath $Executable)) {
    throw "Không tìm thấy executable: $Executable. Hãy chạy script trong thư mục cài đặt đúng."
}

$taskCommand = "`"$Executable`" --background"
Write-Host "Đang tạo lại task: $TaskName" -ForegroundColor Cyan
Invoke-Schtasks @(
    '/Create', '/TN', $TaskName,
    '/TR', $taskCommand,
    '/SC', 'ONLOGON',
    '/DELAY', '0000:10',
    '/RL', 'LIMITED',
    '/IT',
    '/F'
) | Out-Null

$details = Invoke-Schtasks @('/Query', '/TN', $TaskName, '/FO', 'LIST', '/V')
Write-Host "Đã tạo task thành công. Kiểm tra thông tin:" -ForegroundColor Green
$details | Select-String -Pattern 'TaskName:|Status:|Task To Run:|Run As User:|Logon Mode:|Trigger:' | ForEach-Object { Write-Host "  $($_.Line.Trim())" }

if ($RunNow) {
    Invoke-Schtasks @('/Run', '/TN', $TaskName) | Out-Null
    Write-Host "Đã yêu cầu chạy task ngay. Kiểm tra biểu tượng ứng dụng trong khay thông báo." -ForegroundColor Green
}

if (-not $NoPause) { Read-Host "Nhấn Enter để đóng" }
