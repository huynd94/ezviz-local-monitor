# Checkpoint Core và lưu cấu hình theo nền tảng

Baseline: v1.8.6 (`37bed9b`). Workspace: worktree `feature/linux-headless`.

## Phạm vi checkpoint

- Model, codec, đường dẫn, storage, camera và alert services dùng chung nằm trong `EzvizLocalMonitor.Core`.
- Core không tham chiếu Avalonia, DPAPI hoặc project Windows; host cấp AppPaths/logger/protector.
- Windows giữ DPAPI settings/local backup và entropy/header cũ; file app lock và đường dẫn LocalAppData được giữ.
- Linux dùng master key riêng32 byte và AES-GCM, file0600/directory0700; không thay key để che lỗi cấu hình cũ.
- `.ezviztransfer` giữ wire format V1/PBKDF2 parameters. Fixture độc lập Python và decoder dùng offsets literal được kiểm tra trên cả hai hệ điều hành.
- Windows/Unix có obj/bin riêng. Optional analyzer OpenCvSharp yêu cầu Roslyn mới hơn SDK8 được loại riêng tại CoreCompile; không suppress runtime/compiler diagnostics.

## Kết quả sau tích hợp hai subagent

| Môi trường / suite | Kết quả runner |
|---|---|
| Windows, Core/shared tests | 34 passed, 0 failed |
| Windows, Desktop storage tests | 26 passed, 0 failed |
| Ubuntu24.04 WSL, Core/shared tests | 34 passed, 0 failed |
| Ubuntu24.04 WSL, Headless storage/native tests | 44 passed, 0 failed |

Hai theory cases file khóa/chỉ đọc trong shared suite chỉ thực thi assertions trên Windows; trên Linux trả về sớm và runner vẫn đếm passed. Không cộng các con số trên thành số test unique hoặc bằng chứng kiểm tra Windows filesystem trên Linux.

Desktop tests cũng build ứng dụng Windows và Core thành công. Linux Headless suite có42 storage cases và hai native cases: detector YOLO production và timeout khi peer RTSP loopback im lặng. Đây chưa phải kiểm thử với camera thực.

## Sửa lỗi phát hiện ở checkpoint

1. Linux initialization từ chối root/keys permissions không an toàn trước khi tạo các thư mục state khác. Hai test đỏ trước fix, sau fix đạt.
2. Windows backup export không yêu cầu application state directory writable, đúng baseline. Test đỏ trước fix, sau fix đạt.

Các test bổ sung phủ header/envelope hỏng, sai key/length, mode, symlink/dangling symlink, reuse key trước settings, dữ liệu bị sửa, buffers owned/zeroed, failed overwrite và định dạng backup cũ.

## Giới hạn và trạng thái dừng

- Còn CA1416 trong app-lock Windows và CS0067 ở test session stub; không có CS9057 trong lần build/test cuối sau loại optional analyzer.
- UID ownership, race symlink/TOCTOU, wrong-user DPAPI và GUI tray/preview end-to-end chưa được chứng minh tại checkpoint này.
- Code runtime/lifecycle và Windows adapter từ lượt triển khai trước vẫn ở worktree, có một số test smoke; chưa coi Tasks6–8 hoặc feature headless hoàn tất.
- Chưa có CLI executable, daemon/systemd/installer Linux, CI/package/release hoàn chỉnh.
- Dừng sau một todo theo yêu cầu người dùng. Chưa commit/push/merge; main chưa nhận thay đổi source.
