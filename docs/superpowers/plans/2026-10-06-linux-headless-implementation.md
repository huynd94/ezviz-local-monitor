# Linux Headless Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `executing-plans` to implement this plan task-by-task, or `subagent-driven-development` only if the user explicitly selects delegation. Steps use checkbox (`- [ ]`) syntax for tracking. Do not execute the plan merely because the spec was approved.

**Goal:** Bổ sung daemon Linux headless và CLI qua SSH cho Ubuntu 24.04 x64, giữ ứng dụng Windows hoạt động đúng.

**Architecture:** Tách model/service dùng chung sang `EzvizLocalMonitor.Core`. Windows giữ Avalonia, DPAPI và startup/updater; `EzvizLocalMonitor.Headless` dùng Linux protector, CLI và Generic Host chạy foreground dưới systemd. Một `MonitoringRuntime` quản lý lịch và vòng đời cho cả hai host.

**Tech Stack:** .NET 8; Avalonia 11.2.3 ở Windows; OpenCvSharp theo gate native Task 1; ONNX Runtime 1.20.1; Microsoft.Data.Sqlite 8.0.12; xUnit 2.9.2; Microsoft.Extensions.Hosting 8.0.1 cho daemon; Ubuntu 24.04/systemd.

**Spec:** `docs/superpowers/specs/2026-10-06-linux-headless-design.md`.

## Global Constraints

- Baseline: v1.8.6, commit `37bed9b`; 22 test hiện có phải tiếp tục đạt.
- Ubuntu Server 24.04 x64, CPU inference, tối đa bốn camera, CLI qua SSH.
- Không cần Avalonia, desktop, System Tray, X11/Wayland hoặc biến `DISPLAY` trên Linux.
- Giữ nguyên thuật toán YOLO, schema SQLite và định dạng `.ezviztransfer` chỉ để phục vụ refactor.
- Windows giữ DPAPI, entropy/file đã phát hành và đường dẫn LocalAppData hiện tại.
- Linux state mặc định `/var/lib/ezviz-local-monitor`; account `ezviz-monitor`; directory `0700`, private file `0600`, `UMask=0077`.
- `keys/master.key`: 32 byte; AES-256-GCM với nonce mới mỗi lần ghi; không regenerate key để che lỗi mất/hỏng key.
- Tick lịch hai giây; snapshot năm giây; stale sau 30 giây; runtime shutdown tối đa 20 giây; systemd `TimeoutStopSec=30`.
- Retention Linux mỗi 24 giờ, theo `RetentionDays`, mặc định 14; không tự `DeleteAll`, không xóa event chưa hoàn tất hoặc ảnh đang dùng.
- Exit codes: 0 thành công, 1 vận hành, 2 cấu hình/argument/input, 3 lock, 4 status stale/not-running.
- Linux checksum `.tar.gz.sha256sum`; Windows checksum `.zip.sha256`; chọn cặp theo tên, không theo thứ tự release assets.
- Không thêm web/API, ARM64, Docker distribution, GPU hoặc durable outbox trong đợt này.
- Native gate phải được chứng minh trên Ubuntu 24.04; WSL Ubuntu 22.04 hiện có không thay thế gate đó. systemd acceptance cần VM/máy thật có systemd.
- Chỉ commit/push/tag/release khi người dùng đã yêu cầu trong phiên thực thi. Các commit message ở từng task là checkpoint đề xuất, không phải quyền tự commit.

---

## A. Chuẩn bị và bản đồ file

### Trước khi thực thi

```powershell
git status --short --branch
git log --oneline -10
dotnet test "tests\EzvizLocalMonitor.Tests\EzvizLocalMonitor.Tests.csproj" -c Release --no-restore
```

Tạo workspace cô lập theo `using-git-worktrees` khi bắt đầu triển khai. Không đưa spec/plan chưa commit vào một lệnh reset/clean. Nếu dùng worktree, bảo đảm bản spec và kế hoạch này có trong workspace thực thi. Không tự cài Ubuntu mới hoặc thay cấu hình Docker/WSL để vượt gate; thống nhất runner/VM trước khi thực hiện các thao tác đó.

Đọc spec và các file baseline trước khi thay đổi: `MainWindow.axaml.cs`, `MonitorCoordinator.cs`, `CameraMonitor.cs`, `RtspSnapshotReader.cs`, `SettingsStore.cs`, `AppLogger.cs`, `EventStore.cs`, `EventLogMaintenanceService.cs` và `AppUpdateService.cs` trong `src/EzvizLocalMonitor`.

### File chuyển sang Core ở Task 2–5

Namespace model/service giữ `EzvizLocalMonitor.Models` / `EzvizLocalMonitor.Services` trong đợt tách đầu để hạn chế đổi tên không cần thiết. Folder là ranh giới trách nhiệm, không yêu cầu rename mọi namespace.

| File baseline | Đích / trách nhiệm |
|---|---|
| `Models/DomainModels.cs` | `src/EzvizLocalMonitor.Core/Models/DomainModels.cs` |
| `Services/SettingsStore.cs` | `Core/Storage/SettingsStore.cs`, `SettingsCodec.cs`; DPAPI backup chuyển sang Windows |
| `Services/MonitorScheduleService.cs` | `Core/Monitoring/MonitorScheduleService.cs` |
| `Services/MonitorCoordinator.cs` | `Core/Monitoring/MonitorCoordinator.cs` |
| `Services/CameraMonitor.cs` | `Core/Monitoring/CameraMonitor.cs` |
| `Services/OnvifEventListener.cs` | `Core/Monitoring/OnvifEventListener.cs` |
| `Services/RtspSnapshotReader.cs` | `Core/Monitoring/RtspSnapshotReader.cs` |
| `Services/YoloPersonDetector.cs` | `Core/Monitoring/YoloPersonDetector.cs` |
| `Services/LanCameraDiscovery.cs` | `Core/Monitoring/LanCameraDiscovery.cs` |
| `Services/EventStore.cs` | `Core/Storage/EventStore.cs` |
| `Services/EventLogMaintenanceService.cs` | `Core/Storage/EventLogMaintenanceService.cs`; giữ cleanup Windows thủ công |
| `Services/AlertDispatcher.cs` | `Core/Alerts/AlertDispatcher.cs` |
| `Services/AlertQueueService.cs` | `Core/Alerts/AlertQueueService.cs` |
| `Services/AlertMessagePolicy.cs` | `Core/Alerts/AlertMessagePolicy.cs` |
| `Services/ImageRelayService.cs` | `Core/Alerts/ImageRelayService.cs` |
| `Services/OpenAiCompatibleMovementAnalyzer.cs` | `Core/Alerts/OpenAiCompatibleMovementAnalyzer.cs` |
| `Services/AppLogger.cs` | `Core/Diagnostics/AppLogger.cs`; instance nhận paths, không dùng global path mutable |
| `Services/ZaloDiagnostics.cs` | `Core/Diagnostics/ZaloDiagnostics.cs`; instance dùng logger/paths được host cấp |

`AppLockService.cs`, `WindowsStartupService.cs`, `StartupDiagnostics.cs`, `AppUpdateService.cs`, các window và installer PowerShell giữ ở Windows. `Core/` trong bảng là `src/EzvizLocalMonitor.Core/`.

### File mới theo nhóm

| Nhóm | File chính |
|---|---|
| Native proof | `tests/EzvizLocalMonitor.NativeProbe.Tests/NativeSmokeTests.cs`, project và runner `scripts/linux/Run-NativeProbe.sh` |
| Core contracts | `Storage/AppPaths.cs`, `SettingsCodec.cs`, `ISettingsProtector.cs`, `AtomicFile.cs`, `IAppLogger.cs` |
| Windows protection | `src/EzvizLocalMonitor/Storage/WindowsSettingsProtector.cs`, `WindowsBackupService.cs` |
| Linux protection | `src/EzvizLocalMonitor.Headless/Storage/MasterKeyStore.cs`, `LinuxSettingsProtector.cs` |
| Runtime | `Core/Monitoring/TaskRegistry.cs`, `ActiveEventFiles.cs`, `IMonitoringSession.cs`, `MonitoringRuntime.cs`, `RuntimeSnapshot.cs` |
| CLI | `Headless/Program.cs`, `Cli/CommandDispatcher.cs`, `CommandRequest.cs`, `SecretInput.cs`, `ConfigurationCommands.cs`, `BackupCommands.cs`, `OperationalCommands.cs` |
| Daemon | `Headless/Hosting/MonitorWorker.cs`, `DaemonExitState.cs`, `StateDirectoryLock.cs`, `StatusSnapshot.cs`, `StatusFile.cs`, `ProcessIdentity.cs` |
| Retention | `Core/Storage/DeliveryStatusPolicy.cs`, `CompletedEventRetention.cs` |
| Release selection | `Core/Releases/ReleaseAssetSelector.cs` |
| Deployment | `installer/linux/install.sh`, `update.sh`, `uninstall.sh`, `ezviz-local-monitor.service`; `scripts/linux/Package-Headless.sh` |
| QA | `.github/workflows/linux-headless-ci.yml`, `docs/LINUX_HEADLESS.md`, `docs/qa/linux-headless-acceptance.md` |

### Thứ tự phụ thuộc

```text
1 Native proof
  → 2 Core/model/path/codec
  → 3 Windows storage boundary
  → 4 Linux key/protector
  → 5 Shared services + native integration
  → 6 Cancellation/task ownership
  → 7 Shared runtime + Windows adapter
  → 8 Safe Linux retention
  → 9 CLI + inter-process lock
  → 10 Status/doctor/diagnostics
  → 11 Foreground daemon
  → 12 systemd/install/update
  → 13 Multi-platform release selection
  → 14 CI/package/acceptance
```

Tasks 8 và 13 có thể được review độc lập sau các dependency tương ứng; không tách hai người sửa `MainWindow.axaml.cs` cùng lúc. Mỗi task phải build được và qua test liên quan trước khi chuyển sang task tiếp theo.

## Task 1: Native proof trên Ubuntu 24.04 và Windows

**Files:** Create `tests/EzvizLocalMonitor.NativeProbe.Tests/EzvizLocalMonitor.NativeProbe.Tests.csproj`, `NativeSmokeTests.cs`, `scripts/linux/Run-NativeProbe.sh`, `scripts/linux/Get-YoloModel.sh`, `docs/qa/native-gate-linux-x64.md`; produce `build/NativeRuntime.props` sau khi gate đạt.

**Consumes:** Model YOLO từ script baseline, SHA-256 `b2bc52f40e8e1c532427d5bde3575a5d5b571b739fab2c6df443733ed1589cbd`; .NET 8; video H.264 sinh bằng FFmpeg.

**Produces:** Một cặp managed/native đã thử nghiệm; `NativeRuntime.props` chứa `OpenCvManagedPackage`, `OpenCvVersion`, `OpenCvWindowsNativePackage`, `OpenCvLinuxNativePackage` để Task 5 import. Không ghi kết quả PASS khi chỉ restore/build thành công.

Metadata NuGet đã kiểm tra khi lập kế hoạch:

- Candidate A: `OpenCvSharp4` + `OpenCvSharp4.runtime.win` + `OpenCvSharp4.official.runtime.linux-x64`, version **4.10.0.20241108**.
- Candidate B: `OpenCvSharp5` + `OpenCvSharp5.runtime.win` + `OpenCvSharp5.official.runtime.linux-x64.headless`, version **5.0.0.20261003**.
- NuGet index `opencvsharp4.official.runtime.linux-x64.headless` trả 404. Không invent package này hoặc trộn managed 4 với native 5.
- Candidate A chỉ được chọn nếu dependency audit không có GTK/X11; nếu không đạt, thử B trong probe độc lập. Nếu B thất bại trên Linux hoặc Windows, dừng với bằng chứng và xin quyết định về custom native build; không tự mở thêm một dự án build OpenCV nguồn.

- [ ] **1. Tạo test project và fixture trước red run.** Dùng Test SDK 17.11.1, xUnit 2.9.2, runner 2.8.2 như baseline. Đầu tiên chỉ tham chiếu managed OpenCvSharp và ONNX Runtime 1.20.1, chưa có native OpenCvSharp. Runner tải model vào thư mục fixture qua file tạm + `sha256sum -c`, sinh video bằng:

```bash
ffmpeg -hide_banner -loglevel error -f lavfi \
  -i testsrc2=size=640x480:rate=5 -t 3 -c:v libx264 -pix_fmt yuv420p native-smoke.mp4
export EZVIZ_TEST_VIDEO="$PWD/native-smoke.mp4"
export EZVIZ_TEST_MODEL="$PWD/yolov8n.onnx"
```

Test thật cần có, không mock native:

```csharp
[Fact]
public void DecodeResizeAndJpegRoundTrip()
{
    using var capture = new VideoCapture(Environment.GetEnvironmentVariable("EZVIZ_TEST_VIDEO")!, VideoCaptureAPIs.FFMPEG);
    using var frame = new Mat();
    Assert.True(capture.IsOpened());
    Assert.True(capture.Read(frame));
    Assert.Equal(640, frame.Width);
    Assert.Equal(480, frame.Height);
    using var resized = new Mat();
    Cv2.Resize(frame, resized, new Size(320, 240));
    Assert.True(Cv2.ImEncode(".jpg", resized, out var bytes));
    using var decoded = Cv2.ImDecode(bytes, ImreadModes.Color);
    Assert.Equal(320, decoded.Width);
    Assert.Equal(240, decoded.Height);
}
```

Thêm test `OnnxCpuInference` tạo tensor float `[1,3,640,640]`, dùng `InferenceSession.Run` với model thật và assert output shape `[1,84,8400]`. Sinh video/model trước run để red không phải do thiếu fixture.

- [ ] **2. Run red trên Linux:** `dotnet test tests/EzvizLocalMonitor.NativeProbe.Tests -c Release -p:NativeCandidate=baseline`. Mong đợi native-load failure, không phải lỗi fixture/parser. Ghi loại exception đã quan sát.
- [ ] **3. Thêm native references có điều kiện theo candidate/platform.** Candidate A dùng version 4.10.0.20241108 cả ba package. Candidate B dùng 5.0.0.20261003 cả ba package. Không thêm reference vào project ứng dụng tại bước này. Giữ lời gọi OpenCV trong helper synchronous nếu API ref-struct mới không được phép đi qua `await`.
- [ ] **4. Run green và dependency audit trên Ubuntu 24.04 sạch.** `env -u DISPLAY dotnet test ... -p:NativeCandidate=baseline`; nếu A yêu cầu GUI, đổi sang `-p:NativeCandidate=headless5`. Audit `ldd` của `libOpenCvSharpExtern.so` và dependency con: không `not found`, GTK, X11, Wayland. Không cài GTK/X11 để làm test xanh. Lặp probe trên Windows với candidate được chọn; thêm clip camera thật bằng input cục bộ ngoài log để xác nhận RTSP codec.
- [ ] **5. Ghi evidence và properties được chọn.** Mẫu properties cho B nếu B thực sự đạt:

```xml
<Project><PropertyGroup>
  <OpenCvManagedPackage>OpenCvSharp5</OpenCvManagedPackage>
  <OpenCvVersion>5.0.0.20261003</OpenCvVersion>
  <OpenCvWindowsNativePackage>OpenCvSharp5.runtime.win</OpenCvWindowsNativePackage>
  <OpenCvLinuxNativePackage>OpenCvSharp5.official.runtime.linux-x64.headless</OpenCvLinuxNativePackage>
</PropertyGroup></Project>
```

Nếu A đạt, điền đúng bốn giá trị của A thay vì mẫu B. Evidence chứa OS/userland, CPU architecture, package version, ldd, JPEG/inference/video và kết quả Windows. Checkpoint đề xuất: `test: verify Linux headless native runtime compatibility`.

## Task 2: Core model, AppPaths, codec và test fixtures

**Files:** Create `src/EzvizLocalMonitor.Core/EzvizLocalMonitor.Core.csproj`, `Storage/AppPaths.cs`, `SettingsCodec.cs`, `ISettingsProtector.cs`, `AtomicFile.cs`; move `Models/DomainModels.cs` theo bảng; modify Windows project reference; create `tests/EzvizLocalMonitor.Tests/AppPathsTests.cs`, `SettingsCodecTests.cs`, `Support/TemporaryState.cs`, `Support/RejectingProtector.cs`.

**Interfaces:**

```csharp
public sealed class AppPaths
{
    public AppPaths(string stateRoot, string modelPath, string? logRoot = null);
    public string Root { get; }
    public string SettingsFile { get; }
    public string MasterKeyFile { get; }
    public string DatabaseFile { get; }
    public string EventImages { get; }
    public string StatusFile { get; }
    public string LockFile { get; }
    public string ModelPath { get; }
    public string LogsRoot { get; }
    public void EnsureDirectories();
}
public interface ISettingsProtector
{
    byte[] Protect(byte[] plain);
    byte[] Unprotect(byte[] protectedBytes);
}
public static class SettingsCodec
{
    public static byte[] Encode(AppSettings settings);
    public static AppSettings Decode(byte[] plain);
    public static AppSettings Clone(AppSettings settings);
}
public static class AtomicFile
{
    public static void Write(string destination, byte[] bytes, UnixFileMode? mode = null, bool overwrite = true);
}
```

- [ ] **1. Viết test paths và codec trước implementation.** `TemporaryState` là test-only IDisposable: tạo root GUID dưới temp, mode 0700 ở Linux, expose `Root` và `Paths`, dọn đúng root khi dispose. `RejectingProtector` implements cả hai method bằng cách throw `InvalidOperationException("Settings protection is not part of transfer backup")` để test transfer bắt lỗi dùng nhầm platform encryption.

```csharp
[Fact]
public void CustomStateRootIsUsedForEveryStateFile()
{
    using var temp = new TemporaryState();
    temp.Paths.EnsureDirectories();
    Assert.Equal(Path.Combine(temp.Root, "settings.protected"), temp.Paths.SettingsFile);
    Assert.Equal(Path.Combine(temp.Root, "keys", "master.key"), temp.Paths.MasterKeyFile);
    Assert.Equal(Path.Combine(temp.Root, "events.db"), temp.Paths.DatabaseFile);
    Assert.True(Directory.Exists(Path.Combine(temp.Root, "Events")));
}
```

Codec test dùng literal JSON `{"ThemeName":"Ocean","WatchdogEnabled":true}` và assert đúng các giá trị; giữ thêm full-section round-trip baseline.
- [ ] **2. Run red:** `dotnet test tests/EzvizLocalMonitor.Tests -c Release --filter 'FullyQualifiedName~AppPathsTests|FullyQualifiedName~SettingsCodecTests'`. Missing contracts là expected pre-implementation failure; không sửa expectation để làm xanh.
- [ ] **3. Implement boundary.** Core target net8.0, không RID/Avalonia/DPAPI/native package ở bước này. Move model, lấy nguyên Normalize/JsonOptions từ SettingsStore vào codec; namespace và JSON naming/number enums không đổi. AppPaths normalize absolute root, combine tên cố định; model path không phụ thuộc working directory. AtomicFile dùng file GUID `.tmp` cùng directory, `CreateNew`, flush/close rồi `File.Move(temporary, destination, overwrite)`, finally dọn riêng temp của mình; Unix mode đặt trước khi ghi payload. Gọi `overwrite:false` khi tạo key mới; không dùng thao tác ghi settings mặc định để thay key đã có.
- [ ] **4. Run green và baseline 22 test trên Windows.** Kiểm tra atomic overwrite thất bại không thay dữ liệu cũ; không tạo thêm model/state ở LocalAppData khi test dùng custom root.
- [ ] **5. Review file moves/reference graph.** Không giữ hai bản DomainModels cùng compile. Checkpoint: `refactor: introduce shared models and explicit application paths`.

## Task 3: Tách SettingsStore và giữ tương thích DPAPI Windows

**Files:** Move storage phần dùng chung sang `Core/Storage/SettingsStore.cs`; create `src/EzvizLocalMonitor/Storage/WindowsSettingsProtector.cs`, `WindowsBackupService.cs`, `WindowsAppPaths.cs`; modify `MainWindow.axaml.cs`, `Services/AppLockService.cs`, existing backup tests; create `tests/EzvizLocalMonitor.Desktop.Tests/WindowsProtectionTests.cs` và project Windows-only.

**Interfaces:** `SettingsStore(AppPaths paths, ISettingsProtector protector)`, `Load()`, `Save(AppSettings)`, `ExportTransferBackup(AppSettings,string,string)`, `ImportTransferBackup(string,string)`. `WindowsBackupService(AppPaths paths)` exposes `ExportBackup(AppSettings,string)` / `ImportBackup(string)`. `WindowsAppPaths.Create(string executableRoot)` giữ state và logs trong LocalAppData baseline.

- [ ] **1. Viết test tương thích bằng fixture độc lập.** Test Windows tạo old settings bytes bằng DPAPI với entropy literal, không dùng provider mới để tính expected:

```csharp
[Fact]
public void ReadsPreviouslyProtectedWindowsSettings()
{
    using var temp = new TemporaryState();
    var plain = Encoding.UTF8.GetBytes("{\"ThemeName\":\"Ocean\"}");
    var oldBytes = ProtectedData.Protect(plain, Encoding.UTF8.GetBytes("EZVIZ-Local-Monitor-v1"), DataProtectionScope.CurrentUser);
    File.WriteAllBytes(temp.Paths.SettingsFile, oldBytes);
    var store = new SettingsStore(temp.Paths, new WindowsSettingsProtector());
    Assert.Equal("Ocean", store.Load().ThemeName);
}
```

Thêm fixture old `.ezvizbackup` header `EZVIZ-LOCAL-BACKUP-V1\n`, entropy `EZVIZ-Local-Monitor-backup-v1`; kiểm tra cả đọc old và giải mã new bằng API DPAPI cũ. Test transfer dùng RejectingProtector để bảo đảm không phụ thuộc DPAPI.
- [ ] **2. Run red tại project Desktop.Tests trên Windows.** Lỗi provider/service chưa tồn tại hoặc hành vi không đọc fixture là expected. Desktop.Tests không được đưa vào Linux test job.
- [ ] **3. Implement.** WindowsSettingsProtector chỉ dùng entropy settings baseline. WindowsBackupService giữ header, entropy backup và semantics ghi file baseline; MainWindow gọi service này cho backup DPAPI. Core SettingsStore dùng codec/protector/AtomicFile và giữ nguyên thuật toán transfer: salt16, nonce12, tag16, key32, PBKDF2-SHA256 600000, header `EZVIZ-LOCAL-TRANSFER-V1\n`. Giữ xử lý IOException + UnauthorizedAccessException và finally zero key/plain.
- [ ] **4. Run green:** baseline backup tests, new Desktop.Tests, build Windows. MainWindow tạo paths/store ở bootstrap trước load; app lock nhận đường dẫn host, không đổi hash/PBKDF2/entropy app lock. Kiểm tra settings/backup/app-lock cũ trên user Windows bằng fixture riêng, không sửa state ứng dụng thật.
- [ ] **5. Review Core không chứa ProtectedData và Windows vẫn dùng đúng file cũ.** Checkpoint: `refactor: isolate Windows protection from shared configuration storage`.

## Task 4: Linux master key và settings protector

**Files:** Create `src/EzvizLocalMonitor.Headless/EzvizLocalMonitor.Headless.csproj` (library ở checkpoint này, thành executable ở Task 9), `Storage/MasterKeyStore.cs`, `LinuxSettingsProtector.cs`; create `tests/EzvizLocalMonitor.Headless.Tests` project, `LinuxProtectionTests.cs`, `MasterKeyTests.cs`; reference Core.

**Interfaces:** `MasterKeyStore(AppPaths paths)`, `byte[] InitializeForNewConfiguration()`, `byte[] ReadExisting()`. `LinuxSettingsProtector(byte[] key) : ISettingsProtector, IDisposable` clone/own key. Caller zero bản key của mình sau khi truyền vào ctor; Dispose zero bản clone.

Headless.Tests link test-only TemporaryState từ Task2 qua `<Compile Include="../EzvizLocalMonitor.Tests/Support/TemporaryState.cs" Link="Support/TemporaryState.cs" />`; không tham chiếu test assembly hoặc project Windows. Các snippet test thêm using của type tương ứng: Xunit, System.Text/System.Security.Cryptography khi cần, EzvizLocalMonitor.Models, EzvizLocalMonitor.Services và namespace Support. TestLogger/ControlledSession chỉ được link sau khi file/interface của Task5/7 đã có.

- [ ] **1. Viết test thật trên Linux.** Dùng TemporaryState thuộc user không phải root:

```csharp
[Fact]
public void RewritingSameSettingsUsesFreshNonceAndCanBeRead()
{
    using var temp = new TemporaryState();
    var keyStore = new MasterKeyStore(temp.Paths);
    var key = keyStore.InitializeForNewConfiguration();
    using var protector = new LinuxSettingsProtector(key);
    CryptographicOperations.ZeroMemory(key);
    var store = new SettingsStore(temp.Paths, protector);
    store.Save(new AppSettings { ThemeName = "Ocean" });
    var first = File.ReadAllBytes(temp.Paths.SettingsFile);
    store.Save(new AppSettings { ThemeName = "Ocean" });
    Assert.False(first.SequenceEqual(File.ReadAllBytes(temp.Paths.SettingsFile)));
    Assert.Equal("Ocean", store.Load().ThemeName);
    Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(temp.Paths.MasterKeyFile));
}
```

Thêm test đổi một byte ciphertext/tag, wrong key, wrong magic/version, missing key với settings đang có, key sai length, permission quá rộng và symlink key. Assert lỗi không regenerate/overwrite key hoặc settings.
- [ ] **2. Run red:** `dotnet test tests/EzvizLocalMonitor.Headless.Tests -c Release --filter 'FullyQualifiedName~LinuxProtectionTests|FullyQualifiedName~MasterKeyTests'` trên Ubuntu 24.04, không dùng test bị skip làm evidence.
- [ ] **3. Implement envelope:** ASCII header `EZVIZ-LINUX-SETTINGS-V1\n`, nonce12, tag16, ciphertext; dùng header làm AAD. Encryption cốt lõi:

```csharp
var nonce = RandomNumberGenerator.GetBytes(12);
var cipher = new byte[plain.Length];
var tag = new byte[16];
using var aes = new AesGcm(_key, 16);
aes.Encrypt(nonce, plain, cipher, tag, header);
return [.. header, .. nonce, .. tag, .. cipher];
```

Decrypt kiểm tra header/min length trước slicing; authentication failure không trả plaintext. Key creation dùng `AtomicFile.Write(path,key,UnixFileMode.UserRead | UnixFileMode.UserWrite,overwrite:false)` chỉ khi chưa có settings/key. Nếu key hợp lệ đã có nhưng chưa có settings, reuse key qua ReadExisting thay vì tạo lại. Read-existing không mutate filesystem. Validate mode/ownership theo service account; không tự chmod file không thuộc mình. Directory0700/file0600 được áp dụng cho file mới trong state root.
- [ ] **4. Run green và transfer interoperability.** Export trên Windows/import Linux, export Linux/import Windows với cùng test settings/mật khẩu fixture; sai mật khẩu giữ settings hiện tại. Linux từ chối DPAPI backup rõ ràng.
- [ ] **5. Review key lifecycle và finally zero buffers.** Checkpoint: `feat: add protected Linux settings storage`.

## Task 5: Chuyển các service dùng chung và tích hợp native đã chốt

**Files:** Move theo bảng A; modify Core/Windows/Headless project files để import `build/NativeRuntime.props`; modify toàn bộ constructor/call site dùng DataPaths/AppLogger; create `Core/Diagnostics/IAppLogger.cs`, `LogRedactor.cs`, `tests/EzvizLocalMonitor.Tests/ServiceIsolationTests.cs`, `LogRedactorTests.cs`, `Support/TestLogger.cs`; move app-lock enum/validation thuần sang `Core/Models/AppLockPolicy.cs`, Windows AppLockService delegate validation.

**Interfaces:** `IAppLogger.Info(LogChannel,string)`, `Error(LogChannel,string,Exception? = null)`; instance `AppLogger(AppPaths paths, Action<LogEntry>? consoleSink = null)` và `Configure(bool loggingEnabled,bool alertLoggingEnabled)`. `LogEntry(DateTimeOffset Timestamp,LogChannel Channel,string Level,string Message)`. `LogRedactor.Configure(AppSettings)` / `Redact(string?)` không log nội dung secret registry. `EventStore(AppPaths paths, string? databaseFile = null)`, `AlertDispatcher(AppPaths paths,IAppLogger logger)`, `MonitorCoordinator(AppPaths paths,EventStore store,AlertDispatcher alerts,IAppLogger logger)`.

- [ ] **1. Viết test state isolation/redaction.** Hai instance EventStore/logger dùng hai temp roots không ghi chéo. Redaction test literal `rtsp://admin:YOUR_RTSP_PASSWORD@192.168.1.20/stream` và `https://api.telegram.org/botYOUR_TELEGRAM_TOKEN/sendMessage`, cả exception chứa token thuần từ cấu hình; assert password/token/Chat ID không xuất hiện trong output. Không cần gửi request mạng.

Test helper dùng từ Task6, không nằm trong production:

```csharp
internal sealed class TestLogger : IAppLogger
{
    public List<string> Errors { get; } = new();
    public void Info(LogChannel channel, string message) { }
    public void Error(LogChannel channel, string message, Exception? exception = null)
    {
        lock (Errors) Errors.Add(message + ":" + exception?.GetType().Name);
    }
}
```

Đường dẫn log theo host được truyền tại ctor, không bằng dictionary static:

```csharp
var logger = new AppLogger(paths, entry => Console.Error.WriteLine(
    $"{entry.Timestamp:O}\t{entry.Level}\t{entry.Message}"));
logger.Configure(settings.LoggingEnabled, settings.AlertLoggingEnabled);
var store = new EventStore(paths);
var alerts = new AlertDispatcher(paths, logger);
```
- [ ] **2. Run red với constructor mới và cấu hình log riêng.** Expected thiếu constructor/không isolate hoặc secret vẫn lộ; không mock static logger.
- [ ] **3. Implement mechanical moves và injection.** Core references managed OpenCv package đã chốt, SQLite/ONNX; Windows references runtime win/DPAPI; Headless references runtime linux headless. Đổi Core static mutable logger/ZaloDiagnostics thành instance, truyền paths/logger qua service. Pure formatting/redaction helpers có thể static. Startup crash writer chung nhận paths/logger; Windows StartupDiagnostics không đưa user32 vào Core. Không giữ bản copy service cũ cùng compile.
- [ ] **4. Run green trên hai OS.** `EzvizLocalMonitor.Tests` chuyển ProjectReference sang Core; app-lock policy tests gọi Core policy; Windows protection tests ở Desktop.Tests. Baseline 22 case vẫn có cùng intent. Chạy native/Yolo smoke với source ứng dụng mới và Windows GUI smoke tray/Enter/preview. Nếu native candidate là 5, sửa chỉ API compiler xác định là breaking, theo migration guide upstream, không thay thuật toán detector.
- [ ] **5. Audit `.deps.json` Headless không có Avalonia/OpenCv runtime win/ProtectedData.** Build Windows và Linux, kiểm tra model CopyToPublishDirectory. Checkpoint: `refactor: share monitoring services across desktop and headless hosts`.

## Task 6: Ownership của task, cancellation và RTSP timeout

**Files:** Create `Core/Monitoring/TaskRegistry.cs`, `ActiveEventFiles.cs`; modify MonitorCoordinator, RtspSnapshotReader, CameraMonitor, OnvifEventListener, AlertQueueService; tests `TaskRegistryTests.cs`, `CoordinatorShutdownTests.cs`, `ActiveEventFilesTests.cs`.

**Interfaces:** `TaskRegistry(IAppLogger logger)`, `bool TryStart(Func<CancellationToken,Task> work)`, `Task<bool> CompleteAsync(TimeSpan timeout,CancellationToken ct = default)`; `ActiveEventFiles.Acquire(string path): IDisposable`, `TryBeginDelete(string path): IDisposable?`, `IsInUse(string path): bool`. Registry nhận cancellation riêng; task đã nhận phải được observe đến khi hoàn tất.

- [ ] **1. Viết test registry với task bị chặn có chủ đích.** Không dùng sleep:

```csharp
[Fact]
public async Task ShutdownWaitsForOwnedWorkAndClosesAdmission()
{
    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var registry = new TaskRegistry(new TestLogger());
    Assert.True(registry.TryStart(async ct => { entered.SetResult(); await release.Task; }));
    await entered.Task;
    var stop = registry.CompleteAsync(TimeSpan.FromSeconds(20));
    Assert.False(stop.IsCompleted);
    Assert.False(registry.TryStart(_ => Task.CompletedTask));
    release.SetResult();
    Assert.True(await stop);
}
```

`TestLogger` là test-only IAppLogger ghi sanitized LogEntry vào list để kiểm tra exception của registry, không assert mock call count. Thêm timeout test với CancellationTokenSource deadline điều khiển được, thử exception task được quan sát và file lease không được delete khi còn holder.
- [ ] **2. Run red:** `dotnet test tests/EzvizLocalMonitor.Tests -c Release --filter 'FullyQualifiedName~TaskRegistryTests|FullyQualifiedName~CoordinatorShutdownTests|FullyQualifiedName~ActiveEventFilesTests'`.
- [ ] **3. Implement lifecycle.** Thay fire-and-forget event/warm task bằng registry, đóng admission trước stop listeners. CompleteAsync normal shutdown đóng admission và chờ work đã nhận; chỉ hủy work khi deadline/cancellation của shutdown hết, không liên kết work token trực tiếp với ApplicationStopping làm mất cơ hội drain. Pass cancellation vào ONVIF StartAsync, AI HTTP và queue. Camera/snapshot FFmpeg open/read dùng timeout support của backend, cấu hình trước Open/Read; test backend thực ở native probe, không giả định CancelToken cắt được native blocking call. Warm reader không mở lại capture sau dispose. Chỉ dispose detector/reader khi registry + camera/inference worker đã drain.
- [ ] **4. Run green.** Coordinator deadline hết trả kết quả không hoàn tất, không báo đã dừng sạch và không dispose tài nguyên dưới task chưa xong. Verify bằng blocking session/real TaskRegistry và native reconnect/stop test. Linux host Task11 sẽ xử lý trường hợp này bằng exit1; desktop tiếp tục theo dõi cleanup thay vì mở coordinator mới chồng lên tài nguyên cũ.
- [ ] **5. Review Mat ownership/active image leases.** Lease phải được giữ từ trước ghi ảnh đến sau AI/alert/update DB; finally giải phóng dù task fail. Checkpoint: `fix: track monitoring work and shut down safely`.

## Task 7: Shared MonitoringRuntime và Windows adapter

**Files:** Create `Core/Monitoring/IMonitoringSession.cs`, `MonitoringRuntime.cs`, `RuntimeSnapshot.cs`; modify MonitorScheduleService và MonitorCoordinator; extract Windows monitoring adapter sang `src/EzvizLocalMonitor/MainWindow.Monitoring.cs`; modify MainWindow constructor, Start/Stop/ApplySchedule/Show/Hide; tests `MonitoringRuntimeTests.cs`, `ScheduleTimeZoneTests.cs`, `Support/ControlledSession.cs`.

**Interfaces:**

```csharp
public interface IMonitoringSession : IAsyncDisposable
{
    Task StartAsync(AppSettings settings, CancellationToken ct);
    void SetPreviewEnabled(bool enabled);
    event Action<Guid, string>? CameraStatusChanged;
    event Action<Guid, CameraRuntimeSnapshot>? CameraRuntimeChanged;
    event Action<Guid, Mat>? PreviewReady;
    event Action<DetectionEvent>? EventRecorded;
}
public sealed record ShutdownResult(bool Completed, string? Failure);
public sealed record RuntimeSnapshot(string State, bool IsMonitoring,
    bool ScheduleAllowed, int EffectiveProfile, long EventCount);
```

`MonitoringRuntime(Func<IMonitoringSession> factory,TimeProvider clock,TimeZoneInfo zone,IAppLogger logger)` produces `StartAsync(AppSettings,bool previewEnabled,CancellationToken)`, `EvaluateScheduleAsync(DateTimeOffset,CancellationToken)`, `ApplySettingsAsync(AppSettings,CancellationToken)`, `SetManualMonitoringAsync(bool,CancellationToken)`, `SetPreviewEnabled(bool)`, `StopAsync(TimeSpan,CancellationToken): Task<ShutdownResult>`, `RunMaintenanceAsync(Func<CancellationToken,Task> operation,CancellationToken)`, `Snapshot`, `SnapshotChanged` và forward camera/event/preview events. `EvaluateScheduleAsync` được gọi thật tại startup và periodic loop, không phải API chỉ để test. RunMaintenanceAsync giữ cùng transition semaphore với start/stop và được worker dùng tại Task8/11.

- [ ] **1. Viết test trạng thái runtime/lịch bằng session test-only, không mở RTSP.** Session helper implements đúng interface, start/stop có thể bị chặn bằng TaskCompletionSource; assert snapshot thật của runtime và kết quả shutdown, không assert số lần gọi mock:

```csharp
[Fact]
public async Task DisabledScheduleDoesNotStartMonitoring()
{
    var settings = new AppSettings { MonitorSchedules = [new() { IsEnabled = false }] };
    var runtime = new MonitoringRuntime(() => new ControlledSession(), TimeProvider.System,
        TimeZoneInfo.Utc, new TestLogger());
    await runtime.StartAsync(settings, false, CancellationToken.None);
    Assert.False(runtime.Snapshot.IsMonitoring);
    Assert.False(runtime.Snapshot.ScheduleAllowed);
    Assert.True((await runtime.StopAsync(TimeSpan.FromSeconds(20), CancellationToken.None)).Completed);
}
```

Thêm test timestamp literal qua nửa đêm/chuyển ngày, manual start/stop của Windows và profile hiệu lực không mutate settings đã lưu.

ControlledSession là helper test, expose constructor `ControlledSession(Task? disposeGate = null)`, implements toàn bộ events/interface. StartAsync kiểm tra token rồi complete; SetPreviewEnabled lưu flag; DisposeAsync chờ disposeGate nếu được cấp. Không thêm delay/test switch vào coordinator production. Runtime publish snapshot trước network startup và không khóa getter; periodic loop dùng:

```csharp
using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2), clock);
while (await timer.WaitForNextTickAsync(cancellationToken))
    await EvaluateScheduleAsync(clock.GetUtcNow(), cancellationToken);
```
- [ ] **2. Run red:** filter MonitoringRuntimeTests/ScheduleTimeZoneTests.
- [ ] **3. Implement runtime.** Serialize transitions bằng SemaphoreSlim; clone settings khi ApplySettings; dùng PeriodicTimer(TimeSpan.FromSeconds(2),clock), timezone explicit. Snapshot immutable getter không đợi semaphore đang hold trong network startup. Manual override Windows tồn tại đến lần transition lịch/profile tiếp theo như baseline; startup Headless theo lịch. Forward event handlers không được làm chết vòng camera. Trước StartAsync session, tính bản config hiệu lực riêng: FPS1–3, window1–5 và required confirmations1–window hiệu lực; không mutate settings đã lưu. Thêm test stored required10/window30 không làm Math.Clamp nhận min>max trong CameraMonitor; snapshot/doctor phải phân biệt stored với hiệu lực. Thuật toán nhận diện và định dạng backup không đổi.
- [ ] **4. Wire Windows và run green.** Metrics DispatcherTimer có thể giữ cho UI nhưng bỏ tick lịch UI thứ hai. Đổi mọi `_coordinator` call site sang runtime IsMonitoring/ApplySettings/preview; giữ khóa tray, debounce750ms và chỉ mở UI sau xác thực. Smoke Start/Stop, schedule, tray, idle lock, updater exit và camera edit/delete/restore restart.
- [ ] **5. Review shutdown timeout handling cùng Task6.** Checkpoint: `feat: share monitoring lifecycle between desktop and daemon`.

## Task 8: Retention Linux an toàn

**Files:** Create `Core/Storage/DeliveryStatusPolicy.cs`, `CompletedEventRetention.cs`; modify EventStore thêm query/delete-by-ID riêng cho retention, không dùng PurgeAll/PurgeBefore của manual Windows; tests `CompletedEventRetentionTests.cs`.

**Interfaces:** `DeliveryStatusPolicy.IsPending(string?)`; `CompletedEventRetention(AppPaths,EventStore,ActiveEventFiles,IAppLogger)`, `Task<CleanupResult> RunAsync(DateTimeOffset cutoff,CancellationToken ct)`. EventStore adds `Before(DateTimeOffset cutoff,int take): IReadOnlyList<DetectionEvent>`, `DeleteById(long id): bool`. Before dùng thời điểm UTC tương đương (SQLite `julianday`) thay vì so chuỗi offset khác nhau.

- [ ] **1. Viết test bảo vệ active/pending/ngoài root.** Tạo ảnh fixture dưới Events root và event thật trong SQLite. Acquire lease trước retention:

```csharp
using var lease = files.Acquire(imagePath);
await retention.RunAsync(DateTimeOffset.UtcNow.AddDays(-14), CancellationToken.None);
Assert.True(File.Exists(imagePath));
Assert.Contains(store.Recent(), row => row.Id == eventId);
```

Test thêm pending `Chưa gửi`/shutdown chưa hoàn tất, completed older/newer cutoff, symlink ra ngoài root, delete thất bại giữ row và offset timestamp khác nhau.
- [ ] **2. Run red:** filter CompletedEventRetentionTests.
- [ ] **3. Implement.** Pending policy có danh sách trạng thái chưa hoàn tất từ pipeline/queue, không suy luận bằng `Contains("đã gửi")`. Batch500 row, exclude pending; normalize và kiểm tra root/symlink; TryBeginDelete lease ngăn race với producer. Xóa image và `_before` thành công hoặc đã không còn mới delete row; failure giữ row/log để thử lại. Không scan xóa orphan, không tự sửa SQLite schema chỉ để có flag retention.
- [ ] **4. Run green.** Retention task do Linux worker chạy mỗi24h theo RetentionDays, serialized với transition start/stop, có cancellation. Ghi CleanupResult không secret; Desktop auto-retention không bật.
- [ ] **5. Review tests chứng minh ảnh đang dùng không mất.** Checkpoint: `feat: safely retain completed Linux monitoring events`.

## Task 9: CLI cấu hình/backup và khóa liên tiến trình

**Files:** Create `Headless/Program.cs`, `Cli/CommandDispatcher.cs`, `CommandRequest.cs`, `SecretInput.cs`, `ConfigurationCommands.cs`, `BackupCommands.cs`, `SettingsValidator.cs`, `Cli/ConfigurationException.cs`, `Hosting/StateDirectoryLock.cs`, `StateInUseException.cs`; change Headless project OutputType Exe/AssemblyName `ezviz-headless`; tests `CliConfigurationTests.cs`, `StateLockProcessTests.cs`, test-only `tests/EzvizLocalMonitor.TestChild/Program.cs` và csproj.

**Interfaces:** `CommandDispatcher.ExecuteAsync(string[] args,TextReader input,TextWriter output,TextWriter error,CancellationToken ct): Task<int>`; `StateDirectoryLock.Acquire(AppPaths): IDisposable` throws `StateInUseException`; `SecretInput.ReadAsync(bool confirm,CancellationToken): Task<string>`; `SettingsValidator.Validate(AppSettings)` throws `ConfigurationException`. Hai exception types là Headless-only subclasses của Exception, ctor `(string message,Exception? inner = null)`; không đưa message chứa secret vào ctor. Headless configuration factory trả SettingsStore/owned Linux protector đã kiểm tra key; run path dùng ReadExisting, configure lần đầu dùng InitializeForNewConfiguration. Linux key validation Task4 dùng InvalidDataException/CryptographicException; factory chuyển riêng các lỗi key/config thành ConfigurationException, không blanket-map mọi I/O/native failure thành exit2.

- [ ] **1. Viết process/in-process integration tests.** Command input đi stdin, không argv secret:

```csharp
[Fact]
public async Task RejectsInvalidJsonWithoutChangingProtectedSettings()
{
    using var temp = new TemporaryState();
    var output = new StringWriter();
    var error = new StringWriter();
    var code = await CommandDispatcher.ExecuteAsync(
        ["configure", "--stdin", "--data-dir", temp.Root],
        new StringReader("not-json"), output, error, CancellationToken.None);
    Assert.Equal(2, code);
    Assert.False(File.Exists(temp.Paths.SettingsFile));
    Assert.False(File.Exists(temp.Paths.MasterKeyFile));
}
```

TestChild `hold-lock PATH`: Acquire lock, print `LOCKED`, chờ một dòng stdin để release. Test cha đợi stdout readiness rồi chạy configure/run command thứ hai và assert code3; không dùng lock hai handle trong cùng PID vì Linux advisory lock semantics khác Windows.
- [ ] **2. Run red:** Headless.Tests filter CliConfigurationTests/StateLockProcessTests. Dựng TestChild là test-only console, không thêm `--test-hold` vào CLI production.
- [ ] **3. Implement parser/commands.** Support configure, config validate, backup import/export, version; unknown args exit2. `configure --stdin` đọc một JSON AppSettings tối đa1MiB, validate trước key creation/save; interactive wizard dùng prompt camera/channels/AI/lịch và không echo secret. Backup `--password-stdin` đọc một dòng import hoặc hai dòng export/confirmation; không nhận secret value trong argv. Validate ≤4 camera, RTSP/rtsps absolute URI, ONVIF http/https, time HH:mm, enabled channel có token/target. Numeric ranges giữ đúng codec baseline: confidence0.30–0.90, cooldown5–3600, presence0–30, ROI0–100 và right≥left/bottom≥top, RetentionDays1–3650, InferenceFps1–10, ConfirmationsRequired1–10, ConfirmationWindow1–30, profiles0–3. Không tự sửa giới hạn stored settings thành clamp1–3 của CameraMonitor; status báo hiệu lực thực tế. Không network trong validate. Linux `.ezvizbackup` bị từ chối rõ.
- [ ] **4. Run green.** Mutation giữ lock như daemon, báo stop service trước configure/import; export/validate chỉ đọc. TTY thiếu input/EOF không treo. Configure đang có key/cipher lỗi không tạo key mới. Import sai mật khẩu/cipher hoặc save failure giữ settings. Execute dưới service user; không tự gọi sudo hoặc thay owner.
- [ ] **5. Review stdout/stderr và command exit2/3.** Checkpoint: `feat: manage Linux configuration and transfer backups over CLI`.

## Task 10: Status, doctor và operational commands

**Files:** Create `Headless/Hosting/StatusSnapshot.cs`, `StatusFile.cs`, `ProcessIdentity.cs`, `Cli/OperationalCommands.cs`; tests `StatusTests.cs`, `DoctorTests.cs`, `CliSecretRedactionTests.cs`.

**Interfaces:** `ProcessIdentity.Capture(): ProcessIdentity` với PID/startUtc; `ProcessIdentity.IsAlive(ProcessIdentity): bool`; `StatusSnapshot(int SchemaVersion,string InstanceId,ProcessIdentity Process,DateTimeOffset UpdatedAtUtc,string TimeZone,string Version,string State,bool ScheduleAllowed,IReadOnlyList<CameraStatusView> Cameras,long EventCount)`; `CameraStatusView(Guid Id,string Name,CameraConnectionState State,string Message,int Reconnects)`; `StatusFile(AppPaths)`, `Write(StatusSnapshot)`, `Read(DateTimeOffset now): StatusReadResult`. `StatusReadResult(bool IsFresh,StatusSnapshot? Snapshot,string? Failure)`.

- [ ] **1. Viết test snapshot thật vào file.** PID là process test còn sống, không mock chỉ assert PID:

```csharp
[Fact]
public void SnapshotOlderThanThirtySecondsIsStale()
{
    using var temp = new TemporaryState();
    var now = DateTimeOffset.UtcNow;
    var snapshot = new StatusSnapshot(1, Guid.NewGuid().ToString("N"), ProcessIdentity.Capture(),
        now.AddSeconds(-31), "UTC", "1.9.0", "running", true, [], 0);
    var status = new StatusFile(temp.Paths);
    status.Write(snapshot);
    Assert.False(status.Read(now).IsFresh);
}
```

Thêm wrong PID start identity, corrupted JSON, stopped state, new snapshot atomic và JSON chứa camera message có dummy secret phải được scrub.
- [ ] **2. Run red:** filter StatusTests/DoctorTests/CliSecretRedactionTests.
- [ ] **3. Implement.** JSON schema1/camelCase, AtomicFile mode0600; freshness ≤30s + process start match, không dùng PID đơn thuần. Snapshot whitelist không serialize AppSettings/CameraDefinition. `status --json` in sanitized snapshot, exit4 nếu stale/no process. `doctor` kiểm tra architecture, paths/key/config, model hash, native libraries/codec/timezone; không tự gửi alert. `alerts test --channel telegram|zalo` chỉ gửi khi được gọi rõ, dùng cấu hình đã lưu và redact kết quả/errors.
- [ ] **4. Run green và process CLI.** Doctor missing model/native exit1, config/key lỗi exit2; camera offline chỉ degraded, không biến status thành exit4 khi daemon còn sống. Test dùng local HTTP endpoint cho alert request để không gửi tin thật; assert payload gửi đúng ở receiver, không assert mock invocation.
- [ ] **5. Review diagnostic ZIP loại master key/settings protected và redaction exceptions.** Checkpoint: `feat: expose safe headless status and diagnostics`.

## Task 11: Foreground daemon và Generic Host

**Files:** Create `Headless/Hosting/MonitorWorker.cs`, `DaemonExitState.cs`, `RunCommand.cs`; wire run vào Program/CommandDispatcher; add Microsoft.Extensions.Hosting 8.0.1; tests `DaemonProcessTests.cs`.

**Interfaces:** `DaemonExitState.Code` / `Fail(int code)` giữ lỗi khác0; `RunCommand.ExecuteAsync(AppPaths,CancellationToken): Task<int>`. MonitorWorker consumes SettingsStore, MonitoringRuntime, AppPaths, StatusFile, CompletedEventRetention, ProcessIdentity, IHostApplicationLifetime và exit state.

- [ ] **1. Viết test process thật với cấu hình idle hợp lệ.** Spawn `run --data-dir TEMP`, đợi snapshot fresh readiness thay sleep, gửi SIGTERM từ Linux, assert exit0 và instance stopped. Một process run thứ hai cùng root phải exit3; một run thiếu key/config exit2 mà không tạo key mới. Tests không mở camera, không có DISPLAY.
- [ ] **2. Run red:** Headless.Tests filter DaemonProcessTests.
- [ ] **3. Implement preflight và host.** Acquire lock trước SQLite/camera. Validate required settings/key, boot paths/timezone; root không được coi là cách né permissions. Generic Host chỉ cho run command, không đọc appsettings/environment secret ngoài cấu hình đã chỉ định. HostOptions.ShutdownTimeout30s; worker startup immediate schedule, heartbeat5s độc lập snapshot getter; retention24h serialized qua runtime transition gate. Mỗi process có GUID instance mới.

```csharp
builder.Services.Configure<HostOptions>(options =>
    options.ShutdownTimeout = TimeSpan.FromSeconds(30));
using var host = builder.Build();
await host.RunAsync(cancellationToken);
return exitState.Code;
```

Trong worker catch runtime fault: ghi sanitized lỗi, `exitState.Fail(1)`, `lifetime.StopApplication()`. Trong normal cancellation, không truyền token ApplicationStopping đã bị cancel vào drain: gọi `runtime.StopAsync(TimeSpan.FromSeconds(20), CancellationToken.None)` trong ngân sách Host30s; timeout đặt exit1, không in success hoặc dispose resource dưới task vẫn chạy. ConsoleLifetime nhận SIGTERM/SIGINT; không fork, không self-watchdog.
- [ ] **4. Run green và native process smoke.** Empty configured cameras → idle và heartbeat healthy; disabled schedule → outside-schedule; lost camera không làm process chết. Verify lock giữ cả vòng đời, release sau clean stop; snapshot starting vẫn cập nhật khi ONVIF startup chậm.
- [ ] **5. Review root cause exits không bị Generic Host nuốt thành code0.** Checkpoint: `feat: run Linux monitoring as a foreground service`.

## Task 12: systemd, cài đặt và rollback

**Files:** Create `installer/linux/install.sh`, `update.sh`, `uninstall.sh`, `ezviz-local-monitor.service`, `tests/linux/Systemd-Smoke.sh`; create/update `docs/LINUX_HEADLESS.md`.

**Contracts:** `install.sh --package-root PATH`, `update.sh --package-root PATH`, `uninstall.sh` giữ state; optional xóa state là command riêng yêu cầu rõ. Binary theo `/opt/ezviz-local-monitor/releases/VERSION`, symlink `current`; data root/key không nằm trong release directory.

- [ ] **1. Viết smoke test VM trước unit/script.** Script kiểm tra non-root service user, stop/start/reboot enabled state, config missing không restart loop, duplicate root exit3 và unit syntax; assert dữ liệu/key SHA trước/sau nâng cấp và rollback không đổi. Chạy trên VM Ubuntu24 có systemd thật; không dùng Docker-only test để đánh dấu mục này đạt.
- [ ] **2. Run red:** `systemd-analyze verify installer/linux/ezviz-local-monitor.service` và smoke trong VM chưa có service. Expected missing unit/binary, không lờ permission error hoặc chạy daemon root để làm xanh.
- [ ] **3. Implement unit:**

```ini
[Unit]
Description=EZVIZ Local Monitor headless
Wants=network-online.target
After=network-online.target
StartLimitIntervalSec=60
StartLimitBurst=5
[Service]
Type=simple
User=ezviz-monitor
UMask=0077
WorkingDirectory=/opt/ezviz-local-monitor/current
ExecStart=/opt/ezviz-local-monitor/current/ezviz-headless run --data-dir /var/lib/ezviz-local-monitor
Restart=on-failure
RestartSec=5
RestartPreventExitStatus=2 3
TimeoutStopSec=30
StandardOutput=journal
StandardError=journal
[Install]
WantedBy=multi-user.target
```

Install scripts `set -euo pipefail`, verify Ubuntu24/x64 và package version, kiểm tra prerequisites từ native gate; tạo service user nologin + state0700. Cài release mới vào directory riêng, tạo/switch current bằng rename nguyên tử; không overwrite key/settings. Install mới không tự enable/start trước configure/validate. Update lưu current/enabled/active trạng thái, stop service, switch, start nếu trước đó active và đã valid; nếu startup fail rollback symlink và trạng thái trước. Không xoá release đang dùng để rollback.
- [ ] **4. Run green:** syntax `bash -n`, `systemd-analyze verify`, VM test, reboot/logout và journalctl. Re-run install/update phải idempotent. Không apt cài desktop/GTK/X11. Uninstall giữ state mặc định; không tự remove master key.
- [ ] **5. Ghi commands người vận hành:** `sudo -u ezviz-monitor /opt/ezviz-local-monitor/current/ezviz-headless configure`, `config validate`, `sudo systemctl enable --now ezviz-local-monitor`, `journalctl -u ezviz-local-monitor -f`. Checkpoint: `feat: install and manage the Ubuntu headless service`.

## Task 13: Release selector ghép đúng platform và checksum

**Files:** Create `Core/Releases/ReleaseAssetSelector.cs`, tests `ReleaseAssetSelectorTests.cs`; modify Windows `Services/AppUpdateService.cs`, kiểm tra scripts updater hiện tại (Worker đã ghép exact name, giữ semantics đó).

**Interfaces:** `ReleaseTarget { WindowsX64, LinuxHeadlessX64 }`; `ReleaseAsset(string Name,string DownloadUrl)`; `ReleasePackage(ReleaseAsset Package,ReleaseAsset Checksum)`; `ReleaseAssetSelector.Select(IReadOnlyList<ReleaseAsset> assets,string releaseTag,ReleaseTarget target): ReleasePackage?`.

- [ ] **1. Viết test đảo thứ tự cả bốn assets:**

```csharp
[Fact]
public void WindowsGetsItsChecksumWhenLinuxAssetsComeFirst()
{
    ReleaseAsset[] assets = [
        new("EZVIZ-Local-Monitor-Linux-Headless-x64-v1.9.0.tar.gz.sha256sum", "https://example.test/linux-hash"),
        new("EZVIZ-Local-Monitor-Linux-Headless-x64-v1.9.0.tar.gz", "https://example.test/linux"),
        new("EZVIZ-Local-Monitor-Windows-x64-v1.9.0.zip.sha256", "https://example.test/win-hash"),
        new("EZVIZ-Local-Monitor-Windows-x64-v1.9.0.zip", "https://example.test/win") ];
    var pair = ReleaseAssetSelector.Select(assets, "v1.9.0", ReleaseTarget.WindowsX64);
    Assert.NotNull(pair);
    Assert.Equal("https://example.test/win-hash", pair!.Checksum.DownloadUrl);
}
```

Thêm Linux pair, duplicate/missing hash, wrong architecture, tag suffix prerelease và tên chứa path separator. Test legacy algorithm `FirstOrDefault(EndsWith(".sha256"))` với assets mới chỉ thấy đúng Windows checksum.
- [ ] **2. Run red:** filter ReleaseAssetSelectorTests.
- [ ] **3. Implement exact selection.** Validate tag dạng `v?MAJOR.MINOR.PATCH[-suffix]`; giữ suffix trong asset name. Windows `<base>.zip` + `.sha256`; Linux `<base>.tar.gz` + `.sha256sum`; đúng một package và checksum mới trả pair. AppUpdateService.CheckAsync dùng selector Windows; bỏ fallback first checksum, vẫn bỏ qua draft/prerelease của stable auto-update.

Phần chọn cặp sau khi đã validate tag:

```csharp
var version = releaseTag.TrimStart('v', 'V');
var name = target == ReleaseTarget.WindowsX64
    ? $"EZVIZ-Local-Monitor-Windows-x64-v{version}.zip"
    : $"EZVIZ-Local-Monitor-Linux-Headless-x64-v{version}.tar.gz";
var hashName = name + (target == ReleaseTarget.WindowsX64 ? ".sha256" : ".sha256sum");
var packages = assets.Where(a => a.Name == name).ToArray();
var checksums = assets.Where(a => a.Name == hashName).ToArray();
return packages.Length == 1 && checksums.Length == 1
    ? new ReleasePackage(packages[0], checksums[0]) : null;
```
- [ ] **4. Run green cùng test HTTP local release JSON.** Checksum missing/mismatch từ chối trước installer; native Linux checksum không được `.sha256`. Worker Windows đã dùng exact names ở baseline, không refactor script chỉ để đổi style.
- [ ] **5. Review bộ release không làm client Windows≤1.8.6 chọn nhầm.** Checkpoint: `fix: pair multi-platform release assets with the correct checksums`.

## Task 14: CI, package và acceptance trước prerelease/stable

**Files:** Create `.github/workflows/linux-headless-ci.yml`, `scripts/linux/Package-Headless.sh`, `tests/linux/Package-Smoke.sh`, `docs/qa/linux-headless-acceptance.md`; update `README.md`, `docs/QA_REPORT.md`, `docs/LINUX_HEADLESS.md`.

**Consumes:** Green gates Tasks1–13; native properties/model hash; actual Ubuntu VM + camera input cục bộ cho acceptance. **Produces:** Windows ZIP + Linux tar.gz/checksums, CI evidence, prerelease candidate; stable chỉ khi acceptance đạt.

- [ ] **1. Viết package smoke trước packager.** Extract vào temp directory mới, chạy executable `version` và `doctor` dưới Linux, assert model hash/files, `.deps.json` không GUI/DPAPI/runtime-win, installer scripts có executable mode, settings/key/database/ảnh thật không nằm trong archive. Trước doctor, tạo cấu hình idle trong một state temp tách khỏi archive: `printf '{"Cameras":[]}' | "$app/ezviz-headless" configure --stdin --data-dir "$state"`, rồi `"$app/ezviz-headless" doctor --data-dir "$state"`. Không coi doctor thiếu config/key là lỗi package. Verify package checksum độc lập bằng sha256sum, không dùng helper packager để tính expected.
- [ ] **2. Run red với archive chưa có/missing native, không đánh dấu thiếu artifact là runtime PASS.** Sau có artifact, test wrong checksum/tar path traversal phải từ chối cài trước switch release.
- [ ] **3. Implement packaging và CI.** Ubuntu `runs-on: ubuntu-24.04`, Windows `windows-latest`; test Core/baseline và đúng platform tests. Publish:

```bash
dotnet publish src/EzvizLocalMonitor.Headless/EzvizLocalMonitor.Headless.csproj \
  -c Release -r linux-x64 --self-contained true -o artifacts/linux/app
tar -czf "$archive" -C "$staging" app installer README.md
sha256sum "$archive" > "$archive.sha256sum"
```

Packager nhận version argument đã validate, tạo archive name đúng Task13, include Models + libs/prerequisites docs + installer. Không copy PDB hoặc state/key. CI `upload-artifact` cho candidate; không tự `gh release create` hoặc push tag khi chỉ test. Restore packages ở CI từ NuGet, không dùng cache Windows path trên Ubuntu. Model download kiểm tra hash trước publish.
- [ ] **4. Run green trong runner Ubuntu và Windows, rồi VM/camera acceptance.** Mẫu evidence bắt buộc trong `docs/qa/linux-headless-acceptance.md`: OS/RID/packages/build commit, commands/result, camera count và trạng thái input test (không credential), timestamps bắt đầu/kết thúc, CPU/RAM/disk/reconnect/error, shutdown/reboot/update/rollback. Soak tối thiểu24h/hai camera. Nếu chưa có camera/VM/credentials cục bộ, ghi BLOCKED cho gate tương ứng và giữ release prerelease; không ghi PASS theo suy đoán.
- [ ] **5. Review docs và distribution cuối.** Chỉ bump v1.9.0 prerelease/stable khi có yêu cầu phát hành riêng; artifact đóng gói lại theo đúng commit/tag như v1.8.6. Sau publish có chủ đích, kiểm tra server digest, updater Windows và đường tải/checksum Linux. Checkpoint: `ci: verify and package Windows and Ubuntu headless builds`.

## B. Spec coverage và review gates

| Spec | Tasks |
|---|---|
| 1–3 Môi trường/boundaries | 1,2,3,5 |
| 4 Native proof | 1,5,14 |
| 5 Lifecycle/schedule/shutdown | 6,7,11 |
| 6 Paths/key/storage | 2,3,4,9 |
| 7 CLI/contracts/exit codes | 9,10,11 |
| 8 Snapshot/log/retention | 5,6,8,10,11 |
| 9 systemd/installer | 11,12 |
| 10 Backup/interoperability | 3,4,9 |
| 11 Assets/updater/package | 13,14 |
| 12 Automated/real acceptance | Tests từng task,12,14 |
| 13 Checkpoints | Toàn bộ chuỗi1–14 |

### Self-review bắt buộc trước giao kế hoạch

- [x] Mỗi task có files, interfaces, test thật, red/green commands và checkpoint độc lập.
- [x] Types/signatures dùng ở task sau khớp contract task trước; helper test chỉ nằm trong Support/TestChild.
- [x] Không có placeholder thay cho dependency/version; package production phụ thuộc gate có kết quả, không có reference đến NuGet headless4 không tồn tại.
- [x] Linux không phụ thuộc project Windows; namespace giữ ổn định nhưng runtime/platform references đúng host.
- [x] Mỗi requirement trong spec có task/test tương ứng; không coi Ubuntu22 hoặc Docker-only là systemd acceptance Ubuntu24.
- [x] Không commit secrets, test credentials thật, master key hoặc state; không auto publish/commit chỉ vì có lệnh trong plan.

### Trạng thái bàn giao

Kế hoạch là tài liệu thực thi, không phải bằng chứng Linux đã chạy được. Bắt đầu với Task1 và dừng tại native gate nếu chưa có runner Ubuntu24 phù hợp. Các gate dữ liệu camera, VM reboot và soak chỉ được đánh dấu hoàn tất sau quan sát thực tế. Sau khi người dùng chọn cách thực thi, chạy từng task và báo checkpoint, không thực hiện cả refactor rồi mới kiểm thử.
