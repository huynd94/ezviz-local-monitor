﻿[CmdletBinding()]
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
if ([string]::IsNullOrWhiteSpace($InstallDir)) { $InstallDir = Split-Path -Parent $MyInvocation.MyCommand.Path }
$InstallDir = [IO.Path]::GetFullPath($InstallDir)
$Executable = Join-Path $InstallDir "EzvizLocalMonitor.exe"
$currentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name

function Invoke-Schtasks([string[]]$Arguments) {
    $output = & schtasks.exe @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "schtasks.exe failed (exit $LASTEXITCODE): $($output -join ' ')"
    }
    return $output
}

function Register-StartupTask {
    $action = New-ScheduledTaskAction -Execute $Executable -Argument "--background" -WorkingDirectory $InstallDir
    $trigger = New-ScheduledTaskTrigger -AtLogOn
    $principal = New-ScheduledTaskPrincipal -UserId $currentUser -LogonType InteractiveToken -RunLevel Limited
    $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
}

function Ensure-StartupTask {
    try {
        Import-Module ScheduledTasks -ErrorAction Stop
        Register-StartupTask
        return "ScheduledTasks API"
    }
    catch {
        $taskCommand = "`"$Executable`" --background"
        Invoke-Schtasks @(
            '/Create', '/TN', $TaskName,
            '/TR', $taskCommand,
            '/SC', 'ONLOGON',
            '/DELAY', '0000:10',
            '/RL', 'LIMITED',
            '/IT',
            '/F'
        ) | Out-Null
        return "schtasks fallback"
    }
}

if ($Disable) {
    try {
        Invoke-Schtasks @('/Delete', '/TN', $TaskName, '/F') | Out-Null
        Write-Host "Startup task disabled: $TaskName" -ForegroundColor Green
    }
    catch {
        Write-Warning $_.Exception.Message
    }
    if (-not $NoPause) { Read-Host "Press Enter to close" }
    exit 0
}

if (-not (Test-Path -LiteralPath $Executable)) {
    throw "Executable not found: $Executable. Run this script with -InstallDir pointing to the installation directory."
}

Write-Host "Creating startup task: $TaskName" -ForegroundColor Cyan
$method = Ensure-StartupTask
$details = Invoke-Schtasks @('/Query', '/TN', $TaskName, '/FO', 'LIST', '/V')
Write-Host "Startup task created using $method." -ForegroundColor Green
$details | Select-String -Pattern 'TaskName:|Status:|Task To Run:|Run As User:|Logon Mode:|Trigger:' | ForEach-Object { Write-Host "  $($_.Line.Trim())" }

if ($RunNow) {
    Invoke-Schtasks @('/Run', '/TN', $TaskName) | Out-Null
    Write-Host "Task run requested. Check the EZVIZ Local Monitor tray icon." -ForegroundColor Green
}

if (-not $NoPause) { Read-Host "Press Enter to close" }
