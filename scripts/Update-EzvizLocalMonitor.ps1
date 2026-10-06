[CmdletBinding()]
param(
    [string]$InstallDir = "D:\EZVIZ-Local-Monitor",
    [string]$Repository = "huynd94/ezviz-local-monitor",
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

function Quote-ProcessArgument([string]$value) {
    if ($value.Contains('"')) { throw "Đường dẫn/tham số chứa dấu ngoặc kép không hợp lệ: $value" }
    return '"' + $value + '"'
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
            throw "Không truy cập được kênh phát hành public. Kiểm tra kết nối Internet hoặc thử lại sau; GitHub token chỉ cần khi bạn dùng repository riêng."
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
$form.MaximizeBox = $true
$form.AutoScaleMode = [System.Windows.Forms.AutoScaleMode]::Dpi
$form.AutoScroll = $true
$form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::Sizable
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
$statusLabel.Anchor = [System.Windows.Forms.AnchorStyles]::Top -bor [System.Windows.Forms.AnchorStyles]::Left -bor [System.Windows.Forms.AnchorStyles]::Right
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
$checkButton.Anchor = [System.Windows.Forms.AnchorStyles]::Bottom -bor [System.Windows.Forms.AnchorStyles]::Right
$form.Controls.Add($checkButton)

$updateButton = New-Object System.Windows.Forms.Button
$updateButton.Text = "Cập nhật ngay"
$updateButton.Location = New-Object System.Drawing.Point(380, 350)
$updateButton.Size = New-Object System.Drawing.Size(120, 32)
$updateButton.Anchor = [System.Windows.Forms.AnchorStyles]::Bottom -bor [System.Windows.Forms.AnchorStyles]::Right
$updateButton.Enabled = $false
$form.Controls.Add($updateButton)

$closeButton = New-Object System.Windows.Forms.Button
$closeButton.Text = "Đóng"
$closeButton.Location = New-Object System.Drawing.Point(510, 350)
$closeButton.Size = New-Object System.Drawing.Size(120, 32)
$closeButton.Anchor = [System.Windows.Forms.AnchorStyles]::Bottom -bor [System.Windows.Forms.AnchorStyles]::Right
$closeButton.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
$form.Controls.Add($closeButton)
$form.CancelButton = $closeButton

function Set-ActionButtonStyle([System.Windows.Forms.Button]$button, [System.Drawing.Color]$backColor, [System.Drawing.Color]$foreColor) {
    $button.BackColor = $backColor
    $button.ForeColor = $foreColor
    $button.FlatStyle = [System.Windows.Forms.FlatStyle]::Flat
    $button.FlatAppearance.BorderColor = [System.Drawing.Color]::FromArgb(4, 45, 65)
    $button.FlatAppearance.BorderSize = 1
    $button.Font = New-Object System.Drawing.Font("Segoe UI Semibold", 9)
    $button.UseVisualStyleBackColor = $false
    $button.Cursor = [System.Windows.Forms.Cursors]::Hand
}

$primaryButtonColor = [System.Drawing.Color]::FromArgb(7, 89, 133)
$primaryButtonHoverColor = [System.Drawing.Color]::FromArgb(14, 116, 144)
$secondaryButtonColor = [System.Drawing.Color]::FromArgb(31, 41, 55)
$disabledButtonColor = [System.Drawing.Color]::FromArgb(148, 163, 184)
$buttonTextColor = [System.Drawing.Color]::White
Set-ActionButtonStyle $checkButton $primaryButtonColor $buttonTextColor
Set-ActionButtonStyle $updateButton $primaryButtonColor $buttonTextColor
Set-ActionButtonStyle $closeButton $secondaryButtonColor $buttonTextColor
$updateButton.BackColor = $disabledButtonColor
$updateButton.ForeColor = [System.Drawing.Color]::FromArgb(55, 65, 81)

$checkButton.Add_MouseEnter({ if ($checkButton.Enabled) { $checkButton.BackColor = $primaryButtonHoverColor } })
$checkButton.Add_MouseLeave({ if ($checkButton.Enabled) { $checkButton.BackColor = $primaryButtonColor } })
$updateButton.Add_MouseEnter({ if ($updateButton.Enabled) { $updateButton.BackColor = $primaryButtonHoverColor } })
$updateButton.Add_MouseLeave({ if ($updateButton.Enabled) { $updateButton.BackColor = $primaryButtonColor } })
$closeButton.Add_MouseEnter({ if ($closeButton.Enabled) { $closeButton.BackColor = [System.Drawing.Color]::FromArgb(55, 65, 81) } })
$closeButton.Add_MouseLeave({ if ($closeButton.Enabled) { $closeButton.BackColor = $secondaryButtonColor } })

$state = [hashtable]::Synchronized(@{ Busy = $false; CheckOnly = [bool]$CheckOnly; Process = $null; TempRoot = $null; ProgressFile = $null; ResultFile = $null })
$updateTimer = New-Object System.Windows.Forms.Timer
$updateTimer.Interval = 250

function Clear-LegacyUpdaterFolders {
    $tempPath = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\\')
    if (-not (Test-Path -LiteralPath $tempPath -PathType Container)) { return }
    try {
        Get-ChildItem -LiteralPath $tempPath -Directory -Filter "EZVIZ-AutoUpdater-*" -ErrorAction SilentlyContinue |
            Where-Object { $_.Parent.FullName.TrimEnd('\\') -eq $tempPath } |
            ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }
    } catch { }
}

function Clear-WorkerFiles {
    if ($state.TempRoot -and (Test-Path -LiteralPath $state.TempRoot)) { Remove-Item -LiteralPath $state.TempRoot -Recurse -Force -ErrorAction SilentlyContinue }
    Clear-LegacyUpdaterFolders
}

function Finish-Worker {
    $updateTimer.Stop()
    $state.Busy = $false
    $checkButton.Enabled = $true
    $closeButton.Enabled = $true
    $result = $null
    if (Test-Path -LiteralPath $state.ResultFile) {
        try { $result = Get-Content -LiteralPath $state.ResultFile -Raw | ConvertFrom-Json } catch { }
    }
    if ($state.Process.ExitCode -ne 0 -or $null -eq $result -or $result.status -eq "error") {
        $message = if ($null -ne $result -and $result.message) { [string]$result.message } else { "Tiến trình updater kết thúc với mã $($state.Process.ExitCode)." }
        $progress.Value = 0
        $statusLabel.Text = "Lỗi: $message"
        $versionLabel.Text = "Không hoàn tất. Kiểm tra quyền GitHub và thư mục cài đặt."
        Clear-WorkerFiles
        [System.Windows.Forms.MessageBox]::Show($form, $message, "Cập nhật không thành công", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
        return
    }
    if ($result.action -eq "check") {
        $installedText = if ([string]::IsNullOrWhiteSpace([string]$result.installed)) { "không tìm thấy" } else { [string]$result.installed }
        $versionLabel.Text = "Đang cài: $installedText    |    Mới nhất: $($result.latest)"
        if ([bool]$result.isNewer) {
            $updateButton.Enabled = -not $state.CheckOnly
            if ($updateButton.Enabled) { $updateButton.BackColor = $primaryButtonColor; $updateButton.ForeColor = $buttonTextColor }
            $statusLabel.Text = "Có bản mới. Nhấn Cập nhật ngay để bắt đầu."
            $progress.Value = 0
        } else {
            $statusLabel.Text = "Bạn đang dùng phiên bản mới nhất."
            $progress.Value = 100
        }
    } else {
        $progress.Value = 100
        $versionLabel.Text = "Đã cập nhật: $($result.updated)"
        $statusLabel.Text = "Cập nhật thành công."
        $updateButton.Enabled = $false
        $updateButton.BackColor = $disabledButtonColor
        $updateButton.ForeColor = [System.Drawing.Color]::FromArgb(55, 65, 81)
        [System.Windows.Forms.MessageBox]::Show($form, "Đã cập nhật lên phiên bản $($result.updated).", "Hoàn tất", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null
    }
    Clear-WorkerFiles
}

$updateTimer.Add_Tick({
    if (Test-Path -LiteralPath $state.ProgressFile) {
        $parts = (Get-Content -LiteralPath $state.ProgressFile -Raw).Trim() -split '\|', 2
        if ($parts.Count -eq 2) {
            $number = 0
            if ([int]::TryParse($parts[0], [ref]$number)) { $progress.Value = [Math]::Max(0, [Math]::Min(100, $number)) }
            $statusLabel.Text = $parts[1]
        }
    }
    if ($state.Process.HasExited) { Finish-Worker }
})

function Start-Check([bool]$doUpdate) {
    if ($state.Busy) { return }
    $state.Busy = $true
    $checkButton.Enabled = $false
    $updateButton.Enabled = $false
    $updateButton.BackColor = $disabledButtonColor
    $updateButton.ForeColor = [System.Drawing.Color]::FromArgb(55, 65, 81)
    $closeButton.Enabled = $false
    $progress.Value = 0
    $temp = Join-Path ([System.IO.Path]::GetTempPath()) ("EZVIZ-GUI-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $temp -Force | Out-Null
    $state.TempRoot = $temp
    $state.ProgressFile = Join-Path $temp "progress.txt"
    $state.ResultFile = Join-Path $temp "result.json"
    $workerScript = Join-Path $PSScriptRoot "Update-EzvizLocalMonitor-Worker.ps1"
    $mode = if ($doUpdate) { "Update" } else { "Check" }
    $workerArgs = @(
        '-NoLogo', '-NoProfile', '-ExecutionPolicy', 'Bypass',
        '-File', (Quote-ProcessArgument $workerScript),
        '-InstallDir', (Quote-ProcessArgument $installBox.Text.Trim()),
        '-Repository', (Quote-ProcessArgument $repoBox.Text.Trim()),
        '-Mode', $mode,
        '-ProgressFile', (Quote-ProcessArgument $state.ProgressFile),
        '-ResultFile', (Quote-ProcessArgument $state.ResultFile)
    )
    if (-not [string]::IsNullOrWhiteSpace($tokenBox.Text)) { $env:EZVIZ_GITHUB_TOKEN = $tokenBox.Text.Trim() }
    if ($startCheck.Checked -and $doUpdate) { $workerArgs += '-StartAfter' }
    $state.Process = Start-Process -FilePath "powershell.exe" -ArgumentList $workerArgs -WindowStyle Hidden -PassThru
    $updateTimer.Start()
}

$checkButton.Add_Click({ Start-Check $false })
$updateButton.Add_Click({
    $answer = [System.Windows.Forms.MessageBox]::Show($form, "Cập nhật phần mềm trong thư mục:`n$($installBox.Text)`n`nTiếp tục?", "Xác nhận cập nhật", [System.Windows.Forms.MessageBoxButtons]::YesNo, [System.Windows.Forms.MessageBoxIcon]::Question)
    if ($answer -eq [System.Windows.Forms.DialogResult]::Yes) { Start-Check $true }
})
$form.Add_Shown({
    if ($Force) {
        $statusLabel.Text = "Đã xác nhận từ ứng dụng. Bắt đầu cập nhật..."
        Start-Check $true
    } else {
        Start-Check $false
    }
})
[void]$form.ShowDialog()
