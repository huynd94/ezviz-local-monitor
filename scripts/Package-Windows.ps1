[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ($Version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$') { throw 'Invalid package version.' }
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $root 'artifacts\dist' }
if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw 'Repository root is missing.' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($output) | Out-Null
$stage = Join-Path (Join-Path $root 'artifacts') ('windows-package-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($stage) | Out-Null
try {
    $app = Join-Path $stage 'app'
    & dotnet publish (Join-Path $root 'src\EzvizLocalMonitor\EzvizLocalMonitor.csproj') -c Release -r win-x64 --self-contained true -p:NuGetAudit=false "-p:Version=$Version" "-p:InformationalVersion=$Version" -p:IncludeSourceRevisionInInformationalVersion=false -o $app
    if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed.' }
    $commit = & git -C $root rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Cannot determine release build commit.' }
    [IO.File]::WriteAllText((Join-Path $app 'BUILD_COMMIT'), "$commit`n", [Text.Encoding]::ASCII)
    $installer = Join-Path $stage 'installer'
    [IO.Directory]::CreateDirectory($installer) | Out-Null
    foreach ($name in @('Install-EzvizLocalMonitor.ps1', 'Repair-EzvizLocalMonitorStartup.ps1', 'Reset-EzvizLocalMonitorAppLock.ps1', 'Uninstall-EzvizLocalMonitor-Clean.ps1', 'Setup.cmd', 'vc_redist.x64.exe')) {
        Copy-Item -LiteralPath (Join-Path (Join-Path $root 'installer') $name) -Destination $installer
    }
    foreach ($directory in @((Join-Path $stage 'scripts'), (Join-Path $app 'scripts'))) {
        [IO.Directory]::CreateDirectory($directory) | Out-Null
        foreach ($name in @('Update-EzvizLocalMonitor.ps1', 'Update-EzvizLocalMonitor-Worker.ps1', 'Update-EzvizLocalMonitor.cmd')) {
            Copy-Item -LiteralPath (Join-Path (Join-Path $root 'scripts') $name) -Destination $directory
        }
    }
    Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $stage
    Get-ChildItem -LiteralPath $stage -Recurse -Filter '*.pdb' | Remove-Item -Force
    $required = @('app\EzvizLocalMonitor.exe', 'app\Models\yolov8n.onnx', 'app\coreclr.dll', 'app\OpenCvSharpExtern.dll', 'app\onnxruntime.dll', 'installer\vc_redist.x64.exe', 'app\scripts\Update-EzvizLocalMonitor.ps1')
    foreach ($name in $required) { if (-not (Test-Path -LiteralPath (Join-Path $stage $name) -PathType Leaf)) { throw "Missing package file: $name" } }
    $modelHash = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $app 'Models\yolov8n.onnx')).Hash.ToLowerInvariant()
    if ($modelHash -ne 'b2bc52f40e8e1c532427d5bde3575a5d5b571b739fab2c6df443733ed1589cbd') { throw 'Model hash mismatch.' }
    $productVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $app 'EzvizLocalMonitor.exe')).ProductVersion
    if ($productVersion -ne $Version) { throw "Packaged version mismatch: $productVersion" }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $name = "EZVIZ-Local-Monitor-Windows-x64-v$Version.zip"
    $archive = Join-Path $output $name
    $temporary = $archive + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $temporary)
    Move-Item -LiteralPath $temporary -Destination $archive -Force
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archive).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($archive + '.sha256', "$hash  $name`n", [Text.Encoding]::ASCII)
    "PASS: Windows self-contained package $archive"
    "SHA-256: $hash"
} finally {
    # Only remove the uniquely generated staging directory owned by this invocation.
    if (Test-Path -LiteralPath $stage -PathType Container) { Remove-Item -LiteralPath $stage -Recurse -Force }
}
