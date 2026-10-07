# EZVIZ Local Monitor v1.9.0

**Stable release cho Windows x64 desktop và Ubuntu 24.04 x64 headless.** Người dùng đã kiểm thử thành công và xác nhận ứng dụng hoạt động trên Ubuntu 24.04; bản này thay thế v1.9.0-preview.5 để phát hành stable/latest.

## Chức năng

- Core giám sát dùng chung: ONVIF/RTSP, YOLO, lịch giám sát, SQLite, ảnh sự kiện và cảnh báo Telegram/Zalo/AI theo cấu hình.
- Linux headless quản trị CLI qua SSH: configure/validate, backup import/export, status JSON, doctor, alerts test và daemon foreground/systemd.
- Linux lưu cấu hình AES-GCM/master key riêng với quyền POSIX riêng tư; Windows giữ DPAPI và dữ liệu cũ. Backup `.ezviztransfer` tương thích hai nền tảng.
- Vòng đời có task ownership, cancellation, shutdown có deadline và retention bảo vệ các event/ảnh đang được sử dụng.
- Linux installer giữ key/config/database; nâng cấp theo release directory và rollback nếu health check lỗi.
- Package và checksum được ghép theo tên/platform. Windows vẫn khởi động trong System Tray và hỗ trợ Enter để xác nhận mật khẩu/PIN.

## Tải và cài đặt

### Windows x64

Tải `EZVIZ-Local-Monitor-Windows-x64-v1.9.0.zip` và `.zip.sha256`, giải nén toàn bộ và chạy `installer\Setup.cmd`. Khi nâng cấp chọn cùng thư mục đã cài; dữ liệu LocalAppData được giữ. Windows v1.8.5 trở lên có thể nhận bản stable bằng updater ở kênh `huynd94/ezviz-local-monitor`.

### Ubuntu 24.04 x64

Tải `EZVIZ-Local-Monitor-Linux-Headless-x64-v1.9.0.tar.gz` và `.tar.gz.sha256sum`. Kiểm tra checksum/archive trước khi cài bằng quyền root. Hướng dẫn và verifier: [docs/LINUX_HEADLESS.md](https://github.com/huynd94/ezviz-local-monitor/blob/main/docs/LINUX_HEADLESS.md).

Installer tạo user `ezviz-monitor`, prefix `/opt/ezviz-local-monitor`, state `/var/lib/ezviz-local-monitor` và unit `ezviz-local-monitor.service`; cài mới không tự enable/start hoặc tạo camera config. Configure/import dưới service user, validate/doctor rồi chủ động enable service. Nếu đã cài preview, dùng `installer/linux/update.sh --package-root NEW_PACKAGE` để giữ state/key và trạng thái service.

```bash
APP=/opt/ezviz-local-monitor/current/app/ezviz-headless
sudo -u ezviz-monitor "$APP" configure
sudo -u ezviz-monitor "$APP" config validate
sudo -u ezviz-monitor "$APP" doctor
sudo systemctl enable --now ezviz-local-monitor
sudo -u ezviz-monitor "$APP" status --json
```

## Kiểm thử

- Nghiệm thu người dùng: kiểm thử thành công và ứng dụng hoạt động trên Ubuntu 24.04.
- Các regression suites: Ubuntu Core128, Headless124; Windows Core120 đạt/2 Linux-only skips, Desktop39 đạt.
- Installer fixtures21 checks, transaction/rollback12 cases, archive security5 tests; systemd thật trong WSL đã xác minh idle install/start/stop, duplicate lock, idempotent reinstall, upgrade và rollback giữ key/config/database.
- Hai package self-contained chứa model/runtime/installer cần thiết và checksum SHA-256 riêng. `app/BUILD_COMMIT` ghi commit dùng để build.
- Chi tiết reboot/soak24h hoặc số camera của thử nghiệm người dùng chưa được mô tả; không ghi thành kết quả đã đo. GitHub Actions trước đó không khởi chạy do account billing lock; xem trạng thái workflow mới trên GitHub.

## Quay lại bản trước

Windows: thoát ứng dụng hoàn toàn rồi cài lại bộ trước vào cùng thư mục, giữ LocalAppData.

Linux: dùng verified old package với `update.sh --package-root OLD_PACKAGE` để chuyển release trước, giữ key/state. Uninstaller mặc định giữ state và account; không xóa key nếu còn cần giải mã cấu hình local.
