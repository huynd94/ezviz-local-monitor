# Thiết kế Linux headless cho EZVIZ Local Monitor

- Ngày: 2026-10-06
- Baseline: v1.8.6 (`37bed9b`)
- Mốc dự kiến: v1.9.0, kiểm thử prerelease trước khi phát hành ổn định
- Trạng thái: thiết kế và spec đã được người dùng duyệt; lập kế hoạch triển khai trước khi thực thi.

## 1. Mục tiêu và môi trường đã chốt

- Ubuntu Server 24.04, CPU Intel/AMD x64, giám sát bằng CPU.
- Quản trị CLI qua SSH; tiến trình chạy foreground dưới `systemd`.
- Không cần Avalonia, desktop, System Tray, X11/Wayland hoặc biến `DISPLAY`.
- Dùng chung logic camera, nhận diện, lịch, dữ liệu và cảnh báo với ứng dụng Windows.
- Giữ chức năng ONVIF/RTSP, YOLO fallback, SQLite, ảnh sự kiện, Telegram/Zalo, AI tùy cấu hình và tối đa bốn camera như baseline.
- Bản đầu phát hành Linux native dạng `.tar.gz`; web dashboard, HTTP API, Docker và ARM64 thuộc các đợt mở rộng riêng.

## 2. Hiện trạng và các điểm cần tách

| Vị trí hiện tại | Trách nhiệm cần xử lý |
|---|---|
| `MainWindow.axaml.cs` | Khởi tạo/dừng coordinator, tick lịch, thay profile và timeout đang gắn với UI. |
| `Services/MonitorCoordinator.cs` | Logic giám sát độc lập Avalonia; đang dùng DataPaths tĩnh và có tác vụ sự kiện `Task.Run` chưa được theo dõi khi dispose. |
| `Services/SettingsStore.cs` | Serialization, chuẩn hóa cấu hình và backup chuyển máy dùng chung; lưu cấu hình/backup DPAPI phụ thuộc Windows. |
| `Services/AppLockService.cs` | Khóa GUI bằng DPAPI, giữ ở phía Windows. |
| `Services/AppLogger.cs`, `EventStore.cs`, `EventLogMaintenanceService.cs` | Phụ thuộc đường dẫn tĩnh; một số chức năng còn tham chiếu `StartupDiagnostics`. |
| `Services/WindowsStartupService.cs` | Task Scheduler và watchdog Windows, không đưa vào daemon Linux. |
| `Services/AppUpdateService.cs` | Giới hạn Windows; chọn ZIP và SHA-256 đầu tiên, chưa ghép theo tên. |
| `EzvizLocalMonitor.csproj` | RID `win-x64`, Avalonia, OpenCvSharp native Windows và DPAPI cùng nằm trong một executable. |

Việc tách Core không thay định dạng sự kiện SQLite, thuật toán YOLO, quy tắc cảnh báo hoặc định dạng `.ezviztransfer` chỉ để phục vụ refactor.

## 3. Cấu trúc project và ranh giới

```text
src/
  EzvizLocalMonitor.Core/
    Models/
    Monitoring/
    Storage/
    Alerts/
    Diagnostics/
  EzvizLocalMonitor/                 # Windows desktop hiện tại
  EzvizLocalMonitor.Headless/        # CLI và Linux daemon
tests/
  EzvizLocalMonitor.Tests/           # Các test dùng chung hiện có
  EzvizLocalMonitor.Headless.Tests/  # CLI, Linux storage và host lifecycle
installer/linux/
```

### Core

- Thư viện .NET 8 dùng chung; không tham chiếu Avalonia, PowerShell, Task Scheduler hoặc Windows DPAPI.
- Chứa model, ONVIF/RTSP/YOLO, coordinator, lịch, SQLite, alert queue/dispatcher, phân tích AI và backup chuyển máy.
- Chứa `MonitoringRuntime` điều phối vòng đời; không biết tray, widget, SSH hoặc `systemctl`.
- Có đối tượng `AppPaths` cho state/config/database/images/logs/status; host truyền đường dẫn trước khi tạo các service.
- Các thao tác bảo vệ cấu hình đi qua `ISettingsProtector`; thuật toán theo nền tảng nằm ở host.
- Không tạo một framework plugin chung: chỉ bổ sung ranh giới thực sự cần cho hai host.

### Windows desktop

- Giữ project/executable hiện tại, UI, tray, app lock, Task Scheduler và updater Windows.
- Tham chiếu Core và runtime native Windows; giữ đường dẫn LocalAppData hiện tại.
- UI gọi runtime dùng chung và đăng ký các event cần hiển thị; không sở hữu một vòng tick lịch thứ hai.
- DPAPI giữ nguyên entropy/purpose và định dạng các file đã phát hành, không bắt người dùng cấu hình lại.

### Headless

- Console executable `ezviz-headless`; Generic Host quản lý daemon và cancellation.
- Không tham chiếu project Windows; không nạp GUI hoặc thư viện native Windows.
- Có CLI, cấu hình host, Linux settings protector, báo cáo trạng thái và tích hợp `systemd`.
- Các setting thuần GUI được bảo toàn khi nhập/xuất backup nhưng không có tác dụng vận hành Linux.

## 4. Gate kiểm chứng native trước refactor lớn

Chốt cặp OpenCvSharp managed/native bằng một thử nghiệm độc lập trên Ubuntu 24.04 sạch. Version được khóa theo kết quả thử nghiệm, không tự chọn `latest` hoặc giả định package mới tương thích với OpenCvSharp 4.10 hiện tại.

Thử nghiệm phải đạt:

1. Nạp `libOpenCvSharpExtern.so` và tạo/giải phóng Mat khi không có `DISPLAY`.
2. Đọc video qua `VideoCapture` với FFmpeg; xác nhận codec dùng bởi camera RTSP mục tiêu.
3. Resize và ghi/đọc JPEG.
4. Nạp model `yolov8n.onnx`, kiểm tra SHA-256 và chạy inference CPU qua ONNX Runtime.
5. Liệt kê các dependency native; không thiếu thư viện trên image Ubuntu Server đã cài prerequisite công bố.

Ưu tiên runtime headless còn `videoio`; không chọn slim nếu module này bị loại bỏ. Nếu cần nâng OpenCvSharp managed để có native Linux phù hợp, thay đổi đó phải có smoke test lại Windows trước khi tách runtime.

Tham chiếu upstream: <https://github.com/shimat/opencvsharp>. Tài liệu hiện tại mô tả profile headless và slim khác nhau về module; khả năng của package/version thực tế phải được xác nhận qua gate trên.

## 5. Vòng đời runtime

### Khởi động

1. Đọc option host và xác định đường dẫn.
2. Kiểm tra quyền state directory, cấu hình mã hóa, key và timezone.
3. Giữ khóa liên tiến trình cho state directory trước khi mở SQLite hoặc khởi tạo camera.
4. Khởi tạo log và database; công bố snapshot `starting`.
5. Kiểm tra model/native cần cho YOLO và cấu hình camera.
6. Đánh giá lịch ngay, sau đó dùng một vòng tick không chồng lấn với chu kỳ hai giây như baseline.
7. Bật coordinator khi được phép; Headless đặt preview disabled trước khi bắt đầu camera.

Một cấu hình hợp lệ không có camera bật được coi là trạng thái `idle`, không phải crash. Camera mất mạng tạo trạng thái degraded/reconnecting; không tự làm daemon thoát.

### Lịch và profile

- Dùng timezone local của host; trên Linux là timezone của hệ thống hoặc override `TZ` hợp lệ trong service.
- `doctor` và `status` báo timezone đang dùng. Không mặc định lịch server giống timezone của máy Windows đã xuất backup.
- Không mutate setting đã lưu chỉ để biểu diễn profile hiệu lực của lịch.
- Chỉ một transition start/stop/profile được thực thi tại một thời điểm.
- Có test lịch qua nửa đêm và chuyển ngày; quy tắc lịch dùng chung phải đúng trên cả hai host.

### Dừng

- `SIGTERM`, `SIGINT` và stop từ host cùng dùng một đường shutdown.
- Dừng tick lịch/retention, ngừng nhận event mới, hủy reader ONVIF/RTSP.
- Theo dõi các task snapshot/AI/ghi sự kiện/cảnh báo; chờ trong ngân sách tổng tối đa 20 giây rồi hủy phần còn lại.
- Không dispose model, snapshot reader hoặc tài nguyên dùng chung khi task còn sử dụng chúng. Nếu không thể hoàn tất trong ngân sách, host ghi lỗi và thoát để hệ điều hành giải phóng tài nguyên.
- Task lỗi phải được quan sát và log đã che bí mật; không để lỗi nền âm thầm mất dấu.
- Snapshot cuối phản ánh stopped hoặc shutdown không hoàn tất; camera và lock được giải phóng nếu shutdown bình thường.

Giữ retry/dedup trong phiên của baseline. Hàng đợi hiện nằm trong RAM; bản đầu không cam kết exactly-once hoặc replay cảnh báo qua crash. Sự kiện chưa gửi phải giữ trạng thái chưa hoàn tất trong SQLite để chẩn đoán, không tự đánh dấu đã gửi. Durable outbox là thay đổi riêng.

## 6. Đường dẫn và bí mật

### Mặc định Linux

```text
/opt/ezviz-local-monitor/                  executable, native libraries, Models/
/var/lib/ezviz-local-monitor/
  keys/master.key                         khóa Linux, không xuất vào backup
  settings.protected                      cấu hình Linux mã hóa
  events.db                               SQLite và sidecar
  Events/                                 ảnh sự kiện
  logs/                                   log đã che bí mật và file xoay
  status.json                             snapshot đã che bí mật
  daemon.lock                             khóa liên tiến trình
```

- Tài khoản dịch vụ: `ezviz-monitor`, không cần login shell hoặc quyền root để chạy daemon.
- State directory và thư mục keys thuộc service user, mode `0700`; key và settings mode `0600`; service dùng `UMask=0077`.
- Cho phép `--data-dir` trên CLI để kiểm thử/chạy foreground ở thư mục riêng; đây là option host, không phải nội dung backup camera.
- Tất cả thao tác đọc/ghi state dùng đường dẫn được host cấp, không trộn state Linux với LocalAppData hoặc working directory.

### Linux settings protector

- AES-256-GCM, key ngẫu nhiên 32 byte, nonce mới cho mỗi lần mã hóa; envelope có magic/version/purpose và authentication tag.
- Khóa được tạo trong lần `configure` đầu tiên, chỉ khi chưa có cấu hình; tạo mới nguyên tử, không overwrite key đang có.
- Daemon không tự tạo key thay thế nếu key mất hoặc không đọc được. Cấu hình/key lỗi là lỗi cấu hình và không sửa dữ liệu cũ.
- File cấu hình mới được ghi tạm trong cùng filesystem rồi thay thế nguyên tử sau khi validation/encryption thành công.
- Không đổi quyền sở hữu hoặc permissions của file tùy ý ngoài state root được chỉ định.
- File key không xuất vào `.ezviztransfer`, diagnostic ZIP, status hoặc stdout.
- Key và dữ liệu cùng thuộc service user; cơ chế này không nhằm bảo vệ trước root hoặc người đã chiếm tài khoản dịch vụ. Sao lưu toàn bộ state để phục hồi máy phải bảo vệ cả key.

Windows vẫn dùng DPAPI và file app lock đã có; Linux không áp dụng PIN/idle lock của GUI cho daemon. Quyền SSH/sudo và quyền tài khoản dịch vụ kiểm soát quản trị CLI.

## 7. CLI và quy tắc áp dụng cấu hình

Tên executable: `ezviz-headless`. Command parser ưu tiên nhỏ, không thêm dependency chỉ để có vài subcommand.

| Lệnh | Hợp đồng |
|---|---|
| `run [--data-dir PATH]` | Chạy foreground; không daemonize/fork hoặc hỏi nhập liệu. |
| `configure [--data-dir PATH]` | Wizard camera/channels/AI/lịch; xem giá trị cũ đã che bí mật, thay đổi secret qua prompt. |
| `config validate [--data-dir PATH]` | Kiểm tra key/envelope, schema và giới hạn cấu hình; không kết nối camera hoặc gửi tin. |
| `backup import PATH [--data-dir PATH]` | Nhập `.ezviztransfer`, hỏi mật khẩu ẩn; validation thành công mới ghi lại bằng Linux protector. |
| `backup export PATH [--data-dir PATH]` | Xuất `.ezviztransfer`, hỏi và xác nhận mật khẩu; giữ định dạng tương thích Windows. |
| `status [--json] [--data-dir PATH]` | Đọc snapshot, báo freshness/liveness, camera và counters đã che bí mật. |
| `doctor [--data-dir PATH]` | Kiểm tra filesystem, version, timezone, model/native và cấu hình; không tự gửi alert. |
| `alerts test --channel telegram\|zalo [--data-dir PATH]` | Gửi thử có chủ đích bằng cấu hình đã lưu, không khởi chạy camera. |
| `version` | Hiển thị version, RID và build commit. |

- Secret/mật khẩu không truyền qua `--password`, token argument hoặc argv. Khi có TTY dùng prompt không echo; khi automation dùng stdin với hợp đồng đầu vào rõ, không ghi stdin vào log.
- Không xuất JSON chứa token hoặc URL RTSP đầy đủ để người dùng sửa bằng editor trong bản đầu.
- Configure/import phải giữ cùng lock độc quyền với `run`; nếu daemon đang chạy, từ chối thay đổi và hướng dẫn stop trước. Export/validate/status là thao tác đọc, không thay đổi cấu hình daemon.
- CLI quản trị mặc định chạy dưới service user bằng sudo; root chỉ cần cho cài đặt và `systemctl`.
- Dùng `systemctl start/stop/restart`; không xây IPC/API chỉ để gửi các lệnh này.
- Nếu không có TTY và thiếu input cho command tương tác, trả lỗi thay vì chờ vô hạn.

### Exit code

| Code | Ý nghĩa |
|---|---|
| 0 | Command/shutdown thành công; validation/doctor không có lỗi bắt buộc. |
| 1 | Lỗi vận hành bất ngờ: native/model không nạp, I/O hoặc lỗi service. |
| 2 | Sai argument/cấu hình/key hoặc command yêu cầu nhập liệu mà không có input. |
| 3 | State đang bị daemon/command khác giữ lock. |
| 4 | Status không có daemon đang sống, snapshot stale hoặc snapshot không hợp lệ. |

Camera mất mạng trong một daemon đang sống không đổi `status` thành code 4; báo trạng thái camera degraded trong nội dung. Chỉ PID không đủ xác định liveness vì PID có thể tái sử dụng: status kèm process start identity/instance ID và heartbeat.

## 8. Snapshot, log và retention

- Snapshot ghi nguyên tử mỗi năm giây và sau thay đổi trạng thái quan trọng; schema version 1.
- Bao gồm app/build version, instance ID, PID/start time, timestamps UTC, timezone, trạng thái runtime/lịch, camera ID/tên/state và số sự kiện/cảnh báo.
- Snapshot quá 30 giây không cập nhật hoặc process identity không khớp được coi là stale. Quá trình dừng không được trình bày như healthy.
- Không chứa secret, key, Chat ID đầy đủ, Authorization hoặc userinfo trong URL RTSP.
- Log đến console để journald thu nhận và các file xoay theo setting baseline; diagnostics loại key/settings mã hóa và che bí mật trong log.
- Redaction phải xử lý cả URL RTSP có userinfo, Telegram bot URL và message/exception có secret; chỉ regex `token=...` hiện tại là chưa đủ.
- Linux retention đánh giá mỗi 24 giờ theo `RetentionDays` (mặc định 14), chỉ xóa sự kiện/ảnh đã xử lý hoàn tất và cũ hơn cutoff.
- Không tự gọi `DeleteAll` hoặc quét/xóa mọi orphan file. Không xóa ảnh đang được task snapshot/AI/alert sử dụng, không theo symlink ra ngoài Events root.
- Chạy retention qua cùng runtime gate; ghi số lượng xóa/thất bại. Lỗi dọn dữ liệu không làm dừng camera.
- Giữ thao tác cleanup thủ công hiện tại của Windows; auto-retention Linux không âm thầm bật deletion mới trên desktop.

## 9. Dịch vụ systemd và installer

- Unit `ezviz-local-monitor.service`, `Type=simple`, `User=ezviz-monitor`, `UMask=0077`.
- `ExecStart` gọi foreground `run`; `WorkingDirectory` là thư mục cài chương trình, đường dẫn data truyền rõ ràng.
- `Restart=on-failure`, `RestartSec=5`, restart rate limiting; không restart với exit 2 hoặc 3.
- `TimeoutStopSec=30`, lớn hơn ngân sách shutdown 20 giây của ứng dụng.
- Console stdout/stderr được journald thu nhận. `journalctl -u ezviz-local-monitor` là đường xem log chuẩn.
- `network-online.target` là thứ tự khởi động, không được coi là bảo đảm camera/Internet đã sẵn sàng; reader phải reconnect.
- Unit không dùng watchdog Windows. Service không chạy root, không bật HTTP listener.

Installer shell chạy với quyền quản trị để kiểm tra Ubuntu/architecture/prerequisite, tạo account và thư mục, cài binary/unit. Không tự tạo cấu hình camera rỗng rồi bật giám sát. Sau cài mới, hướng dẫn chạy configure/import dưới service user và bật service khi validate đạt. Nâng cấp giữ state directory/key và trạng thái enable của service.

Uninstaller dừng/xóa service và binary nhưng giữ state mặc định. Xóa state là thao tác riêng phải được yêu cầu rõ ràng. Script không cài desktop/GTK/X11 để né thiếu runtime headless.

## 10. Backup và tương thích

- `.ezviztransfer` giữ nguyên header, PBKDF2 parameters và AES-GCM hiện có, tương thích Windows/Linux hai chiều.
- Backup DPAPI `.ezvizbackup` chỉ có trên Windows; Linux từ chối rõ định dạng này, không thử đọc như JSON/plaintext.
- Nhập cấu hình không tự mang app-lock Windows sang Linux; bản backup hiện tại cũng không chứa app lock.
- Nhập file lỗi/sai mật khẩu/không ghi được không thay settings hiện có hoặc master key.
- Tất cả quy tắc bảo toàn backup cũ và dọn file tạm của v1.8.6 phải tiếp tục có test.
- Giữ schema SQLite hiện có; backup chuyển máy không bao gồm SQLite/ảnh, đúng baseline.

## 11. Release assets và updater

Một release mới gồm:

```text
EZVIZ-Local-Monitor-Windows-x64-vX.Y.Z.zip
EZVIZ-Local-Monitor-Windows-x64-vX.Y.Z.zip.sha256
EZVIZ-Local-Monitor-Linux-Headless-x64-vX.Y.Z.tar.gz
EZVIZ-Local-Monitor-Linux-Headless-x64-vX.Y.Z.tar.gz.sha256sum
```

Checksum Linux dùng `.sha256sum` là chủ ý tương thích: Windows v1.8.6 trở xuống chọn file kết thúc `.sha256` đầu tiên. Linux tarball không kết thúc `.zip` và Linux checksum không kết thúc `.sha256`, nên client cũ vẫn chỉ thấy đúng cặp Windows.

- Client mới chọn đúng OS/architecture/package name và checksum có tên bằng package name cộng hậu tố tương ứng; không dựa vào thứ tự API assets.
- Nếu thiếu hoặc mơ hồ một trong hai file, từ chối cập nhật; không thử checksum của platform khác và không bỏ kiểm tra hash.
- Có regression test đảo thứ tự assets, hai platform, missing checksum và wrong architecture.
- Linux bản đầu cài/nâng cấp có chủ đích qua installer, tải release và xác minh SHA-256; daemon không tự thay executable.
- Linux package là self-contained nhưng vẫn công bố/cài prerequisite native của Ubuntu nếu cần; không tuyên bố self-contained đồng nghĩa không có dependency hệ thống.
- Gói Linux không chứa Windows runtime, DPAPI, GUI, key, token, state SQLite/ảnh hoặc cấu hình thực.
- Chỉ chuyển từ prerelease sang stable sau gate test Linux native và kiểm thử camera thực; build chéo trên Windows không thay cho test Linux.

## 12. Kiểm thử và điều kiện nghiệm thu

### Tự động

- Giữ toàn bộ 22 test baseline, bổ sung test Core paths, lịch qua nửa đêm, serialization và Linux protector.
- Test key mất/sai, ciphertext bị sửa, nonce/envelope/purpose, file mode, cấu hình nguyên tử và nhập/xuất backup hai nền tảng.
- CLI integration tests bằng process thật: exit code, stdin/TTY thiếu input, lock, config lỗi, status stale và redaction; không dùng camera/token thật trong fixture.
- Lifecycle tests: concurrent start/stop, stop khi camera reconnect/AI/alert đang chạy, timeout và không use-after-dispose.
- Retention tests: ảnh đang dùng, pending event, cutoff, root containment và symlink.
- GitHub Actions Windows + Ubuntu 24.04: build/test/publish, native smoke, package contents/checksum. Core/Headless dependency graph không có Avalonia hoặc native Windows.
- Unit/systemd validation và smoke start/stop trên VM Ubuntu có systemd thật. Container test đơn thuần không chứng minh systemd hoạt động.

### Trên máy/VM Ubuntu và camera thực

1. Cài mới không có desktop hoặc `DISPLAY`, configure/import và start bằng systemd.
2. SSH logout vẫn giám sát; reboot tự khởi động service đã enable.
3. Kiểm tra ONVIF, RTSP, JPEG và YOLO fallback với camera mục tiêu.
4. Mất mạng camera/Internet rồi phục hồi; quan sát reconnect, retry và trạng thái.
5. Gửi thử/nhận ảnh Telegram và Zalo theo giới hạn relay hiện có; AI optional hoạt động theo setting.
6. Chuyển lịch/profile đúng timezone, stop/restart khi có task đang chạy.
7. Chạy liên tục tối thiểu 24 giờ với hai camera, ghi CPU/RAM, dung lượng, số reconnect và lỗi; điều tra tăng tài nguyên không ổn định trước stable.
8. Nâng cấp và rollback binary, giữ key/cấu hình/SQLite/ảnh.
9. Windows smoke: silent tray startup, PIN/password + Enter, idle lock, preview resume, backup và updater.

Các kiểm thử cần credential hoặc camera thực được thực hiện bằng dữ liệu cục bộ của người vận hành, không đưa vào repo, command history, CI log hoặc release.

## 13. Trình tự triển khai và checkpoint

1. **Native proof:** chốt cặp library và prerequisite Ubuntu; đạt gate mục 4 mới tiếp tục.
2. **Core extraction:** đưa model/service dùng chung sang Core; Windows vẫn build/test/chạy đúng.
3. **Runtime lifecycle:** dùng chung start/stop/schedule, theo dõi task và đường dẫn host.
4. **Linux storage + CLI:** cấu hình/secret/backup, command contracts và integration tests.
5. **systemd + operations:** installer/unit, status, log và retention có test.
6. **Distribution:** multi-platform asset selection, Ubuntu/Windows CI và gói prerelease.
7. **Acceptance:** VM/camera thực, soak test, nâng cấp/rollback, cập nhật QA rồi quyết định stable v1.9.0.

Không nâng version, thay package hoặc phát hành Linux chỉ vì đã tạo spec. Sau khi bản spec được duyệt, kế hoạch chi tiết phải liệt kê file/project thay đổi, test đỏ trước fix, dependency giữa đầu việc và bằng chứng cần tại từng checkpoint.
