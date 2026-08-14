[CmdletBinding()]
param(
    [string]$InstallDir = "D:\EZVIZ-Local-Monitor",
    [string]$Repository = "huyavm/ezviz-local-monitor-releases",
    [ValidateSet("Check","Update")][string]$Mode = "Check",
    [string]$ProgressFile,
    [string]$ResultFile,
    [switch]$StartAfter
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
try { chcp 65001 | Out-Null } catch { }

function Write-ProgressState([int]$percent, [string]$message) {
    if ([string]::IsNullOrWhiteSpace($ProgressFile)) { return }
    "$percent|$message" | Set-Content -LiteralPath $ProgressFile -Encoding UTF8 -Force
}

function Write-Result($object) {
    if (-not [string]::IsNullOrWhiteSpace($ResultFile)) {
        $object | ConvertTo-Json -Compress | Set-Content -LiteralPath $ResultFile -Encoding UTF8 -Force
    }
}

function Convert-ToVersion([string]$value) {
    $clean = ($value -replace '^v', '') -replace '[^0-9\.].*$', ''
    try { return [version]$clean } catch { return [version]'0.0.0' }
}

function Get-ApiHeaders {
    $headers = @{ "Accept" = "application/vnd.github+json"; "User-Agent" = "EZVIZ-Local-Monitor-Updater" }
    $token = $env:EZVIZ_GITHUB_TOKEN
    if ([string]::IsNullOrWhiteSpace($token) -and (Get-Command gh -ErrorAction SilentlyContinue)) {
        try { $token = (& gh auth token 2>$null).Trim() } catch { $token = $null }
    }
    if (-not [string]::IsNullOrWhiteSpace($token)) { $headers["Authorization"] = "Bearer $token" }
    return $headers
}

function Get-LatestRelease {
    $headers = Get-ApiHeaders
    try {
        return Invoke-RestMethod -Uri "https://api.github.com/repos/$Repository/releases/latest" -Headers $headers -Method Get
    } catch {
        $code = $null
        try { $code = $_.Exception.Response.StatusCode.value__ } catch { }
        if ($code -eq 401 -or $code -eq 403 -or $code -eq 404) {
            throw "Không truy cập được repository private. Hãy chạy 'gh auth login' hoặc nhập GitHub token có quyền Contents: ReadOnly."
        }
        throw "Không thể kiểm tra GitHub Release: $($_.Exception.Message)"
    }
}

function Download-Asset($asset, [string]$destination) {
    Invoke-WebRequest -Uri $asset.browser_download_url -Headers (Get-ApiHeaders) -OutFile $destination -UseBasicParsing
}

function Get-HashFromFile([string]$path) {
    $match = [regex]::Match((Get-Content -LiteralPath $path -Raw), '(?i)\b[0-9a-f]{64}\b')
    if (-not $match.Success) { throw "File SHA-256 không có giá trị hợp lệ." }
    return $match.Value.ToLowerInvariant()
}

try {
    Write-ProgressState 5 "Đang kiểm tra file thực thi hiện tại..."
    $installedExe = Join-Path $InstallDir "EzvizLocalMonitor.exe"
    $installed = $null
    if (Test-Path -LiteralPath $installedExe) { $installed = Convert-ToVersion $((Get-Item -LiteralPath $installedExe).VersionInfo.ProductVersion) }

    Write-ProgressState 15 "Đang kết nối GitHub Releases..."
    $release = Get-LatestRelease
    $latest = Convert-ToVersion $release.tag_name
    $isNewer = ($null -eq $installed -or $latest -gt $installed)
    if ($Mode -eq "Check" -or -not $isNewer) {
        Write-ProgressState 100 ($(if ($isNewer) { "Có bản mới." } else { "Bạn đang dùng phiên bản mới nhất." }))
        Write-Result @{ status = "ok"; action = "check"; installed = [string]$installed; latest = [string]$latest; isNewer = $isNewer }
        exit 0
    }

    $zipName = "EZVIZ-Local-Monitor-Windows-x64-v$latest.zip"
    $hashName = "$zipName.sha256"
    $zipAsset = $release.assets | Where-Object { $_.name -eq $zipName } | Select-Object -First 1
    $hashAsset = $release.assets | Where-Object { $_.name -eq $hashName } | Select-Object -First 1
    if ($null -eq $zipAsset -or $null -eq $hashAsset) { throw "Release $($release.tag_name) thiếu ZIP hoặc SHA-256: $zipName" }

    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("EZVIZ-Update-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    try {
        $zipPath = Join-Path $tempRoot $zipName
        $hashPath = Join-Path $tempRoot $hashName
        Write-ProgressState 25 "Đang tải gói $zipName..."
        Download-Asset $zipAsset $zipPath
        Write-ProgressState 55 "Đang tải file kiểm tra SHA-256..."
        Download-Asset $hashAsset $hashPath
        Write-ProgressState 65 "Đang xác minh checksum, chưa chạy bộ cài..."
        if ((Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash.ToLowerInvariant() -ne (Get-HashFromFile $hashPath)) { throw "Checksum không khớp; bộ cài bị từ chối." }
        Write-ProgressState 72 "Checksum hợp lệ. Đang giải nén bộ cài..."
        $extractRoot = Join-Path $tempRoot "package"
        Expand-Archive -LiteralPath $zipPath -DestinationPath $extractRoot -Force
        $installer = Get-ChildItem -Path $extractRoot -Filter "Install-EzvizLocalMonitor.ps1" -Recurse | Select-Object -First 1
        if ($null -eq $installer) { throw "Không tìm thấy bộ cài trong ZIP." }
        Write-ProgressState 80 "Đang đóng ứng dụng cũ và cập nhật file..."
        Get-Process -Name "EzvizLocalMonitor" -ErrorAction SilentlyContinue | Stop-Process -Force
        $installerArgs = "-NoLogo -NoProfile -ExecutionPolicy Bypass -File `"$($installer.FullName)`" -InstallDir `"$InstallDir`" -NoShortcut -NoLaunch -ForceUpdate"
        $process = Start-Process -FilePath "powershell.exe" -ArgumentList $installerArgs -Wait -PassThru -WindowStyle Hidden
        if ($process.ExitCode -ne 0) { throw "Bộ cài trả mã lỗi $($process.ExitCode)." }
        Write-ProgressState 95 "Đang xác minh phiên bản sau cập nhật..."
        $updated = Convert-ToVersion $((Get-Item -LiteralPath $installedExe).VersionInfo.ProductVersion)
        if ($updated -lt $latest) { throw "Phiên bản sau cập nhật là $updated, chưa đạt $latest." }
        if ($StartAfter) { Start-Process $installedExe }
        Write-ProgressState 100 "Cập nhật thành công."
        Write-Result @{ status = "ok"; action = "update"; installed = [string]$installed; latest = [string]$latest; updated = [string]$updated }
    } finally {
        if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue }
    }
} catch {
    Write-ProgressState 0 ("Lỗi: " + $_.Exception.Message)
    Write-Result @{ status = "error"; message = $_.Exception.Message }
    exit 1
}
