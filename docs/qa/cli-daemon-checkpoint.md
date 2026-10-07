# Checkpoint CLI, status và foreground daemon (Tasks 9–11)

Workspace: worktree `feature/linux-headless`; baseline v1.8.6. Dừng sau todo này, không phát hành hoặc triển khai systemd ở checkpoint này.

## Chức năng đã ghép

- Executable Linux x64 self-contained `ezviz-headless` có `--help`, `version`, `configure`, `config validate`, `backup import/export`, `status`, `doctor`, `alerts test`, `run`.
- Configure stdin đọc AppSettings JSON tối đa1MiB và validate trước tạo key/settings. Wizard giữ secret cũ mà không hiển thị, nhập URL/token/key bằng hidden prompt. Backup password chỉ qua hidden prompt hoặc `--password-stdin`, không qua argv.
- Cấu hình mới dùng key32/AES-GCM; key/cipher cũ bị lỗi không được che bằng regenerate. Mutation/config import giữ lock và bị từ chối nếu daemon đang chạy.
- Descriptor-relative Linux state access dùng flock, O_NOFOLLOW/statx, owner/mode và link count; không unlink lock khi release hoặc crash recovery.
- Status schema1/camelCase/whitelist/0600, sanitize write/read/export, heartbeat5s, stale30s. Starting/stopping/faulted không bị báo healthy; camera degraded không đồng nghĩa daemon chết.
- Process identity dùng kernel `/proc/<pid>/stat` field22 + `/proc/stat` btime và clock rate, không dùng ước lượng `Process.StartTime` của .NET. Test process thật đã phát hiện rồi xác nhận sửa sai lệch vài millisecond giữa hai reader.
- Doctor kiểm tra config/key, architecture, model SHA-256, JPEG/FFmpeg và ONNX CPU initialization; không tự liên hệ camera hoặc gửi alert.
- Alerts test gửi có chủ đích; integration test chuyển transport tới HTTP loopback và kiểm tra payload thật ở receiver, không dùng token thật hoặc gọi Telegram/Zalo thực.
- Generic Host chạy foreground, ConsoleLifetime nhận SIGTERM/SIGINT; lock giữ hết lifetime. Runtime/camera work được dừng trong budget20s, Host shutdown30s; failure/deadline trả exit1, config/key lỗi2, contention3, status unavailable4.
- Heartbeat và snapshot changes được ghi độc lập trong startup; retention được nối qua runtime maintenance gate với chu kỳ24h, cutoff theo RetentionDays. Không tự bật daemon/systemd hoặc watcher bên ngoài.

## Kiểm chứng tại checkpoint

| Suite/build | Kết quả |
|---|---|
| Ubuntu24.04 Headless suite | 124 passed, 0 failed/skipped |
| Windows Core/shared suite | 89 passed, 2 Linux-only skips, 0 failed |
| Windows Desktop suite | 28 passed, 0 failed/skipped |
| Linux TestChild + Headless build | 0 errors, 0 warnings |

Process tests chạy binary thật dưới non-root `ubuntu`, không DISPLAY: idle readiness/statusJSON, repeated start/new instance, disabled schedule, duplicate run/configure exit3, missing key/config exit2 không regenerate, SIGTERM exit0, final snapshot stopped và lock được release. TestChild kiểm tra lock trong cùng PID, process khác, release và crash recovery.

## Chạy thử từ WSL Ubuntu24.04

Đường dẫn từ worktree Windows hiện tại:

```bash
ROOT=/mnt/c/Users/Zero/AppData/Local/Temp/opencode/ezviz-linux-headless
APP="$ROOT/src/EzvizLocalMonitor.Headless/bin/Unix/Release/net8.0/linux-x64/ezviz-headless"
STATE="$HOME/.local/share/ezviz-headless-test"

"$APP" --help
printf '{"Cameras":[]}' | "$APP" configure --stdin --data-dir "$STATE"
"$APP" config validate --data-dir "$STATE"
"$APP" doctor --data-dir "$STATE"
"$APP" run --data-dir "$STATE"
```

Trong cửa sổ SSH/WSL thứ hai: `"$APP" status --json --data-dir "$STATE"`. Ctrl+C hoặc SIGTERM để dừng. State nằm trên Linux filesystem, không dùng `/mnt/c` hoặc `/mnt/d` cho key/state vì không bảo đảm owner/mode POSIX yêu cầu.

Chạy suite Linux và chuẩn bị đúng fixture:

```bash
bash "$ROOT/scripts/linux/Test-Headless.sh"
```

## Giới hạn / todo tiếp theo

- Chưa có service unit/install/update/uninstall Linux, distribution archive, multi-platform release selector hoặc CI hoàn chỉnh; đó là Tasks12–14.
- Chưa kiểm thử với camera/token thật, UI tray/preview end-to-end, reboot/systemd hoặc soak24h. Chu kỳ retention24h đã nối trong worker nhưng chưa có bằng chứng wall-clock24h.
- Process identity ổn định đã kiểm qua process thực; chưa thử thay đổi system clock/NTP lớn khi daemon đang sống.
- Hạn mức subagent đã chặn một lượt CLI; primary đã tích hợp/test các file backend còn lại. Subagent status/lock đã hoàn tất và cung cấp red/green.
- Còn warning Windows DPAPI và test event stub; source main nguyên vẹn, chưa commit/push/merge/tag/release.
