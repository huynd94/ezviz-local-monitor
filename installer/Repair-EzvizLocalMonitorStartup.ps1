﻿param(
    [string]$InstallDir,
    [switch]$Disable,
    [switch]$RunNow,
    [switch]$NoPause
)

$ErrorActionPreference = 'Stop'
$TaskName = 'EZVIZ Local Monitor'

if ([string]::IsNullOrWhiteSpace($InstallDir)) {
    $InstallDir = Split-Path -Parent $MyInvocation.MyCommand.Path
}
$InstallDir = [System.IO.Path]::GetFullPath($InstallDir)
$Executable = Join-Path -Path $InstallDir -ChildPath 'EzvizLocalMonitor.exe'

function Invoke-Schtasks {
    param([string[]]$Arguments)
    $output = & schtasks.exe @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw ('schtasks.exe failed with exit code {0}: {1}' -f $LASTEXITCODE, ($output -join ' '))
    }
    return $output
}

function Pause-IfNeeded {
    if (-not $NoPause) {
        Read-Host 'Press Enter to close'
    }
}

try {
    try { chcp 65001 | Out-Null } catch { }

    if ($Disable) {
        try {
            Invoke-Schtasks @('/Delete', '/TN', $TaskName, '/F') | Out-Null
            Write-Host ('Startup task disabled: {0}' -f $TaskName) -ForegroundColor Green
        }
        catch {
            Write-Warning $_.Exception.Message
        }
        Pause-IfNeeded
        exit 0
    }

    if (-not (Test-Path -LiteralPath $Executable -PathType Leaf)) {
        throw ('Executable not found: {0}' -f $Executable)
    }

    $taskCommand = '"{0}" --background' -f $Executable
    Write-Host ('Creating startup task: {0}' -f $TaskName) -ForegroundColor Cyan

    Invoke-Schtasks @(
        '/Create',
        '/TN', $TaskName,
        '/TR', $taskCommand,
        '/SC', 'ONLOGON',
        '/DELAY', '0000:10',
        '/RL', 'LIMITED',
        '/IT',
        '/F'
    ) | Out-Null

    Write-Host 'Startup task created successfully.' -ForegroundColor Green
    $details = Invoke-Schtasks @('/Query', '/TN', $TaskName, '/V', '/FO', 'LIST')
    $details | Select-String -Pattern 'TaskName:|Status:|Task To Run:|Run As User:|Logon Mode:|Trigger:' | ForEach-Object {
        Write-Host ('  {0}' -f $_.Line.Trim())
    }

    if ($RunNow) {
        Invoke-Schtasks @('/Run', '/TN', $TaskName) | Out-Null
        Write-Host 'Task run requested. Check the EZVIZ Local Monitor tray icon.' -ForegroundColor Green
    }
}
catch {
    Write-Host ('ERROR: {0}' -f $_.Exception.Message) -ForegroundColor Red
    Write-Host 'Verify that the script is copied from the v1.8.3 release and that InstallDir is correct.' -ForegroundColor Yellow
    exit 1
}

Pause-IfNeeded
