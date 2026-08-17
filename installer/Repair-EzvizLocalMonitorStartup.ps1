# EZVIZ Local Monitor startup repair - compatible with Windows PowerShell 5.1
$ErrorActionPreference = 'Stop'
$TaskName = 'EZVIZ Local Monitor'
$InstallDir = $null
$Disable = $false
$RunNow = $false
$NoPause = $false

for ($i = 0; $i -lt $args.Count; $i++) {
    switch ($args[$i].ToLowerInvariant()) {
        '-installdir' {
            if ($i + 1 -ge $args.Count) { throw 'Missing value for -InstallDir.' }
            $i++
            $InstallDir = $args[$i]
        }
        '-disable' { $Disable = $true }
        '-runnow' { $RunNow = $true }
        '-nopause' { $NoPause = $true }
        default { throw ('Unknown argument: {0}' -f $args[$i]) }
    }
}

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
    if (-not $NoPause) { Read-Host 'Press Enter to close' }
}

try {
    try { chcp 65001 | Out-Null } catch { }

    if ($Disable) {
        Invoke-Schtasks @('/Delete', '/TN', $TaskName, '/F') | Out-Null
        Write-Host ('Startup task disabled: {0}' -f $TaskName) -ForegroundColor Green
        Pause-IfNeeded
        exit 0
    }

    if (-not (Test-Path -LiteralPath $Executable -PathType Leaf)) {
        throw ('Executable not found: {0}' -f $Executable)
    }

    $taskCommand = '"{0}" --background' -f $Executable
    Write-Host ('Creating startup task: {0}' -f $TaskName) -ForegroundColor Cyan

    # Do not use /IT: it can return Access Denied for a non-elevated PowerShell.
    # The task is still interactive because ONLOGON runs under the current user.
    Invoke-Schtasks @(
        '/Create', '/TN', $TaskName,
        '/TR', $taskCommand,
        '/SC', 'ONLOGON',
        '/DELAY', '0000:10',
        '/RL', 'LIMITED',
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
    if ($_.Exception.Message -match 'Access is denied|access denied') {
        Write-Host 'Open PowerShell with Run as administrator, then run this command again.' -ForegroundColor Yellow
    }
    exit 1
}

Pause-IfNeeded
