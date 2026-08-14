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
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

function Convert-ToVersion([string]$value) {
    $clean = ($value -replace '^v', '') -replace '[^0-9\.].*$', ''
    try { return [version]$clean } catch { return [version]'0.0.0' }
}

function Get-InstalledVersion {
    $exe = Join-Path $InstallDir "EzvizLocalMonitor.exe"
    if (-not (Test-Path -LiteralPath $exe)) { return $null }
    return Convert-ToVersion $((Get-Item -LiteralPath $exe).VersionInfo.ProductVersion)
}

function Get-ApiHeaders([string]$uiToken) {
    $headers = @{ "Accept" = "application/vnd.github+json"; "User-Agent" = "EZVIZ-Local-Monitor-Updater" }
    $token = $uiToken
    if ([string]::IsNullOrWhiteSpace($token)) { $token = $env:EZVIZ_GITHUB_TOKEN }
    if ([string]::IsNullOrWhiteSpace($token) -and (Get-Command gh -ErrorAction SilentlyContinue)) {
        try { $token = (& gh auth token 2>$null).Trim() } catch { $token = $null }
    }
    if (-not [string]::IsNullOrWhiteSpace($token)) { $headers["Authorization"] = "Bearer $token" }
    return $headers
}

function Get-LatestRelease([string]$uiToken) {
    $headers = Get-ApiHeaders $uiToken
    try {
        return Invoke-RestMethod -Uri "https://api.github.com/repos/$Repository/releases/latest" -Headers $headers -Method Get
    } catch {
        $code = $null
        try { $code = $_.Exception.Response.StatusCode.value__ } catch { }
        if ($code -eq 401 -or $code -eq 403 -or $code -eq 404) {
            throw "Không truy cập được repository private. Hãy chạy 'gh auth login' hoặc nhập GitHub token có quyền Contents: ReadOnly vào ô Token."
        }
        throw "Không thể kiểm tra GitHub Release: $($_.Exception.Message)"
    }
}

function Download-Asset($asset, [string]$destination, [string]$uiToken) {
    $headers = Get-ApiHeaders $uiToken
    Invoke-WebRequest -Uri $asset.browser_download_url -Headers $headers -OutFile $destination -UseBasicParsing
}

function Get-HashFromFile([string]$path) {
    $line = Get-Content -LiteralPath $path -Raw
    $match = [regex]::Match($line, '(?i)\b[0-9a-f]{64}\b')
    if (-not $match.Success) { throw "File SHA-256 không có giá trị hợp lệ." }
    return $match.Value.ToLowerInvariant()
}

$form = New-Object System.Windows.Forms.Form
$form.Text = "EZVIZ Local Monitor — Cập nhật"
$form.StartPosition = "CenterScreen"
$form.Size = New-Object System.Drawing.Size(680, 430)
$form.MinimumSize = New-Object System.Drawing.Size(680, 430)
$form.MaximizeBox = $false
$form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::FixedDialog
$form.Font = New-Object System.Drawing.Font("Segoe UI", 9)

$title = New-Object System.Windows.Forms.Label
$title.Text = "Cập nhật EZVIZ Local Monitor"
$title.Font = New-Object System.Drawing.Font("Segoe UI Semibold", 16)
$title.ForeColor = [System.Drawing.Color]::FromArgb(9,59,90)
$title.Location = New-Object System.Drawing.Point(24, 18)
$title.AutoSize = $true
$form.Controls.Add($title)

$info = New-Object System.Windows.Forms.Label
$info.Text = "Kiểm tra bản mới nhất từ GitHub, xác minh checksum rồi cập nhật an toàn."
$info.Location = New-Object System.Drawing.Point(26, 52)
$info.AutoSize = $true
$form.Controls.Add($info)

function Add-Label([string]$text, [int]$x, [int]$y) {
    $label = New-Object System.Windows.Forms.Label
    $label.Text = $text
    $label.Location = New-Object System.Drawing.Point($x, $y)
    $label.AutoSize = $true
    $form.Controls.Add($label)
    return $label
}

Add-Label "Thư mục cài đặt:" 26 88 | Out-Null
$installBox = New-Object System.Windows.Forms.TextBox
$installBox.Text = $InstallDir
$installBox.Location = New-Object System.Drawing.Point(150, 85)
$installBox.Size = New-Object System.Drawing.Size(480, 24)
$form.Controls.Add($installBox)

Add-Label "Repository:" 26 124 | Out-Null
$repoBox = New-Object System.Windows.Forms.TextBox
$repoBox.Text = $Repository
$repoBox.Location = New-Object System.Drawing.Point(150, 121)
$repoBox.Size = New-Object System.Drawing.Size(480, 24)
$form.Controls.Add($repoBox)

Add-Label "GitHub token (tùy chọn):" 26 160 | Out-Null
$tokenBox = New-Object System.Windows.Forms.TextBox
$tokenBox.UseSystemPasswordChar = $true
$tokenBox.Location = New-Object System.Drawing.Point(150, 157)
$tokenBox.Size = New-Object System.Drawing.Size(480, 24)
$form.Controls.Add($tokenBox)

$versionLabel = Add-Label "Đang đọc phiên bản..." 26 198
$progress = New-Object System.Windows.Forms.ProgressBar
$progress.Location = New-Object System.Drawing.Point(26, 228)
$progress.Size = New-Object System.Drawing.Size(604, 22)
$progress.Minimum = 0
$progress.Maximum = 100
$form.Controls.Add($progress)

$statusLabel = New-Object System.Windows.Forms.Label
$statusLabel.Text = "Sẵn sàng."
$statusLabel.Location = New-Object System.Drawing.Point(26, 264)
$statusLabel.Size = New-Object System.Drawing.Size(604, 42)
$statusLabel.AutoEllipsis = $true
$form.Controls.Add($statusLabel)

$startCheck = New-Object System.Windows.Forms.CheckBox
$startCheck.Text = "Mở ứng dụng sau khi cập nhật"
$startCheck.Checked = -not $NoLaunch
$startCheck.Location = New-Object System.Drawing.Point(26, 318)
$startCheck.AutoSize = $true
$form.Controls.Add($startCheck)

$checkButton = New-Object System.Windows.Forms.Button
$checkButton.Text = "Kiểm tra bản mới"
$checkButton.Location = New-Object System.Drawing.Point(250, 350)
$checkButton.Size = New-Object System.Drawing.Size(120, 32)
$form.Controls.Add($checkButton)

$updateButton = New-Object System.Windows.Forms.Button
$updateButton.Text = "Cập nhật ngay"
$updateButton.Location = New-Object System.Drawing.Point(380, 350)
$updateButton.Size = New-Object System.Drawing.Size(120, 32)
$updateButton.Enabled = $false
$form.Controls.Add($updateButton)

$closeButton = New-Object System.Windows.Forms.Button
$closeButton.Text = "Đóng"
$closeButton.Location = New-Object System.Drawing.Point(510, 350)
$closeButton.Size = New-Object System.Drawing.Size(120, 32)
$closeButton.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
$form.Controls.Add($closeButton)
$form.CancelButton = $closeButton

$state = [hashtable]::Synchronized(@{ Release = $null; Installed = $null; Latest = $null; Busy = $false; CheckOnly = [bool]$CheckOnly })
$worker = New-Object System.ComponentModel.BackgroundWorker
$worker.WorkerReportsProgress = $true

$worker.add_ProgressChanged({
    param($sender, $event)
    $progress.Value = [Math]::Max(0, [Math]::Min(100, $event.ProgressPercentage))
    $statusLabel.Text = [string]$event.UserState
})

$worker.add_RunWorkerCompleted({
    param($sender, $event)
    $state.Busy = $false
    $checkButton.Enabled = $true
    $closeButton.Enabled = $true
    if ($event.Error) {
        $progress.Value = 0
        $statusLabel.Text = "Lỗi: " + $event.Error.Message
        $versionLabel.Text = "Không hoàn tất. Kiểm tra quyền GitHub và thư mục cài đặt."
        [System.Windows.Forms.MessageBox]::Show($form, $event.Error.Message, "Cập nhật không thành công", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
        return
    }
    $result = $event.Result
    if ($result.Action -eq "check") {
        $state.Release = $result.Release
        $state.Installed = $result.Installed
        $state.Latest = $result.Latest
        $versionLabel.Text = "Đang cài: " + ($(if ($null -eq $result.Installed) { "không tìm thấy" } else { $result.Installed.ToString() })) + "    |    Mới nhất: " + $result.Latest.ToString()
        if ($result.IsNewer) {
            $updateButton.Enabled = -not $state.CheckOnly
            $statusLabel.Text = "Có bản mới. Nhấn Cập nhật ngay để bắt đầu."
            $progress.Value = 0
        } else {
            $statusLabel.Text = "Bạn đang dùng phiên bản mới nhất."
            $progress.Value = 100
        }
        return
    }
    $progress.Value = 100
    $state.Installed = $result.Updated
    $state.Latest = $result.Latest
    $versionLabel.Text = "Đã cập nhật: " + $result.Updated.ToString()
    $statusLabel.Text = "Cập nhật thành công."
    $updateButton.Enabled = $false
    [System.Windows.Forms.MessageBox]::Show($form, "Đã cập nhật lên phiên bản $($result.Updated).", "Hoàn tất", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null
})

$worker.add_DoWork({
    param($sender, $event)
    $args = $event.Argument
    $sender.ReportProgress(5, "Đang kiểm tra file thực thi hiện tại...")
    $install = $args.InstallDir
    $repo = $args.Repository
    $token = $args.Token
    $installedExe = Join-Path $install "EzvizLocalMonitor.exe"
    $installed = $null
    if (Test-Path -LiteralPath $installedExe) { $installed = Convert-ToVersion $((Get-Item -LiteralPath $installedExe).VersionInfo.ProductVersion) }
    $sender.ReportProgress(15, "Đang kết nối GitHub Releases...")
    $oldRepository = $script:Repository
    $script:Repository = $repo
    try { $release = Get-LatestRelease $token } finally { $script:Repository = $oldRepository }
    $latest = Convert-ToVersion $release.tag_name
    $isNewer = ($null -eq $installed -or $latest -gt $installed -or $Force)
    if (-not $isNewer -or $args.CheckOnly) {
        $event.Result = @{ Action = "check"; Release = $release; Installed = $installed; Latest = $latest; IsNewer = $isNewer }
        return
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
        $sender.ReportProgress(25, "Đang tải gói $zipName...")
        Download-Asset $zipAsset $zipPath $token
        $sender.ReportProgress(55, "Đang tải file kiểm tra SHA-256...")
        Download-Asset $hashAsset $hashPath $token
        $sender.ReportProgress(65, "Đang xác minh checksum, chưa chạy bộ cài...")
        $expected = Get-HashFromFile $hashPath
        $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash.ToLowerInvariant()
        if ($actual -ne $expected) { throw "Checksum không khớp; bộ cài bị từ chối." }
        $sender.ReportProgress(72, "Checksum hợp lệ. Đang giải nén bộ cài...")
        $extractRoot = Join-Path $tempRoot "package"
        Expand-Archive -LiteralPath $zipPath -DestinationPath $extractRoot -Force
        $installer = Get-ChildItem -Path $extractRoot -Filter "Install-EzvizLocalMonitor.ps1" -Recurse | Select-Object -First 1
        if ($null -eq $installer) { throw "Không tìm thấy bộ cài trong ZIP." }
        $sender.ReportProgress(80, "Đang đóng ứng dụng cũ và cập nhật file...")
        Get-Process -Name "EzvizLocalMonitor" -ErrorAction SilentlyContinue | Stop-Process -Force
        $installerArgs = "-NoLogo -NoProfile -ExecutionPolicy Bypass -File `"$($installer.FullName)`" -InstallDir `"$install`" -NoShortcut -NoLaunch -ForceUpdate"
        $process = Start-Process -FilePath "powershell.exe" -ArgumentList $installerArgs -Wait -PassThru -WindowStyle Hidden
        if ($process.ExitCode -ne 0) { throw "Bộ cài trả mã lỗi $($process.ExitCode)." }
        $sender.ReportProgress(95, "Đang xác minh phiên bản sau cập nhật...")
        $updated = Convert-ToVersion $((Get-Item -LiteralPath $installedExe).VersionInfo.ProductVersion)
        if ($updated -lt $latest) { throw "Phiên bản sau cập nhật là $updated, chưa đạt $latest." }
        if ($args.StartAfter) { Start-Process $installedExe }
        $event.Result = @{ Action = "update"; Updated = $updated; Latest = $latest }
    } finally {
        if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue }
    }
})

function Start-Check([bool]$doUpdate) {
    if ($state.Busy) { return }
    $state.Busy = $true
    $checkButton.Enabled = $false
    $updateButton.Enabled = $false
    $closeButton.Enabled = $false
    $progress.Value = 0
    $worker.RunWorkerAsync(@{
        InstallDir = $installBox.Text.Trim()
        Repository = $repoBox.Text.Trim()
        Token = $tokenBox.Text.Trim()
        CheckOnly = (-not $doUpdate)
        StartAfter = $startCheck.Checked
    })
}

$checkButton.Add_Click({ Start-Check $false })
$updateButton.Add_Click({
    $answer = [System.Windows.Forms.MessageBox]::Show($form, "Cập nhật phần mềm trong thư mục:`n$($installBox.Text)`n`nTiếp tục?", "Xác nhận cập nhật", [System.Windows.Forms.MessageBoxButtons]::YesNo, [System.Windows.Forms.MessageBoxIcon]::Question)
    if ($answer -eq [System.Windows.Forms.DialogResult]::Yes) { Start-Check $true }
})
$form.Add_Shown({ Start-Check $false })
[void]$form.ShowDialog()
