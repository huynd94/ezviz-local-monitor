[CmdletBinding()]
param(
    [string]$InstallDir = "D:\EZVIZ-Local-Monitor",
    [string]$Repository = "huyavm/ezviz-local-monitor",
    [switch]$CheckOnly,
    [switch]$Force,
    [switch]$NoLaunch
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
$utf8 = New-Object System.Text.UTF8Encoding($false)
[Console]::InputEncoding = $utf8
[Console]::OutputEncoding = $utf8
$OutputEncoding = $utf8
try { chcp 65001 | Out-Null } catch { }

function Get-ApiHeaders {
    $headers = @{ "Accept" = "application/vnd.github+json"; "User-Agent" = "EZVIZ-Local-Monitor-Updater" }
    $token = $env:EZVIZ_GITHUB_TOKEN
    if ([string]::IsNullOrWhiteSpace($token) -and (Get-Command gh -ErrorAction SilentlyContinue)) {
        try { $token = (& gh auth token 2>$null).Trim() } catch { $token = $null }
    }
    if (-not [string]::IsNullOrWhiteSpace($token)) { $headers["Authorization"] = "Bearer $token" }
    return $headers
}

function Convert-ToVersion([string]$value) {
    $clean = ($value -replace '^v', '') -replace '[^0-9\.].*$', ''
    try { return [version]$clean } catch { return [version]'0.0.0' }
}

function Get-InstalledVersion {
    $exe = Join-Path $InstallDir "EzvizLocalMonitor.exe"
    if (-not (Test-Path -LiteralPath $exe)) { return $null }
    return Convert-ToVersion $((Get-Item -LiteralPath $exe).VersionInfo.ProductVersion)
}

function Get-LatestRelease {
    $headers = Get-ApiHeaders
    $url = "https://api.github.com/repos/$Repository/releases/latest"
    try {
        return Invoke-RestMethod -Uri $url -Headers $headers -Method Get
    } catch {
        if ($_.Exception.Response.StatusCode.value__ -eq 401 -or $_.Exception.Response.StatusCode.value__ -eq 404) {
            throw "Repository GitHub là private. Hãy cài GitHub CLI và đăng nhập, hoặc đặt biến môi trường EZVIZ_GITHUB_TOKEN có quyền Contents: ReadOnly rồi chạy lại."
        }
        throw "Không thể kiểm tra GitHub Release: $($_.Exception.Message)"
    }
}

function Download-ReleaseAsset($release, $asset, [string]$destination) {
    $headers = Get-ApiHeaders
    Invoke-WebRequest -Uri $asset.browser_download_url -Headers $headers -OutFile $destination -UseBasicParsing
}

Write-Host "EZVIZ Local Monitor updater" -ForegroundColor Cyan
$installed = Get-InstalledVersion
if ($null -eq $installed) { Write-Host "Chưa tìm thấy ứng dụng tại $InstallDir" -ForegroundColor Yellow }
else { Write-Host "Đang cài: $installed" }

$release = Get-LatestRelease
$latest = Convert-ToVersion $release.tag_name
Write-Host "Mới nhất: $latest ($($release.html_url))" -ForegroundColor Cyan

if ($null -ne $installed -and $latest -le $installed -and -not $Force) {
    Write-Host "Ứng dụng đã ở phiên bản mới nhất." -ForegroundColor Green
    exit 0
}
if ($CheckOnly) {
    Write-Host "Chế độ kiểm tra: chưa thực hiện cập nhật." -ForegroundColor Yellow
    exit 0
}

$zipAsset = $release.assets | Where-Object { $_.name -eq "EZVIZ-Local-Monitor-Windows-x64-v$latest.zip" } | Select-Object -First 1
$hashAsset = $release.assets | Where-Object { $_.name -eq "EZVIZ-Local-Monitor-Windows-x64-v$latest.zip.sha256" } | Select-Object -First 1
if ($null -eq $zipAsset -or $null -eq $hashAsset) { throw "Release $($release.tag_name) thiếu ZIP hoặc file SHA-256 tương ứng." }

$answer = Read-Host "Cập nhật từ $installed lên $latest vào $InstallDir? [Y/n]"
if ($answer -and $answer -notmatch '^(y|yes)$') { Write-Host "Đã hủy." -ForegroundColor Yellow; exit 0 }

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("EZVIZ-Update-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
try {
    $zipPath = Join-Path $tempRoot $zipAsset.name
    $hashPath = Join-Path $tempRoot $hashAsset.name
    Write-Host "Đang tải gói cập nhật..." -ForegroundColor Cyan
    Download-ReleaseAsset $release $zipAsset $zipPath
    Download-ReleaseAsset $release $hashAsset $hashPath

    $expected = ((Get-Content -LiteralPath $hashPath -Raw).Split([char]32, [System.StringSplitOptions]::RemoveEmptyEntries))[0].Trim().ToLowerInvariant()
    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash.ToLowerInvariant()
    if ($actual -ne $expected) { throw "Checksum không khớp; không chạy bộ cài." }
    Write-Host "Checksum hợp lệ." -ForegroundColor Green

    $extractRoot = Join-Path $tempRoot "package"
    Expand-Archive -LiteralPath $zipPath -DestinationPath $extractRoot -Force
    $installer = Get-ChildItem -Path $extractRoot -Filter "Install-EzvizLocalMonitor.ps1" -Recurse | Select-Object -First 1
    if ($null -eq $installer) { throw "Không tìm thấy Install-EzvizLocalMonitor.ps1 trong gói đã kiểm tra." }

    Get-Process -Name "EzvizLocalMonitor" -ErrorAction SilentlyContinue | Stop-Process -Force
    & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $installer.FullName -InstallDir $InstallDir -NoShortcut
    if ($LASTEXITCODE -ne 0) { throw "Bộ cài trả mã lỗi $LASTEXITCODE." }
    if (-not $NoLaunch) { Start-Process (Join-Path $InstallDir "EzvizLocalMonitor.exe") }

    $updated = Get-InstalledVersion
    if ($null -eq $updated -or $updated -lt $latest) { throw "Đã chạy bộ cài nhưng phiên bản sau cập nhật không đạt $latest." }
    Write-Host "Cập nhật thành công: $updated" -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue }
}
