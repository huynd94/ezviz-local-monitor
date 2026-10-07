# EZVIZ Local Monitor v1.9.0-preview.5

**Prerelease: hỗ trợ Linux headless trên Ubuntu24.04 x64.** Bản stable hiện tại vẫn là v1.8.6; updater tự động không chọn prerelease. Tải và cài bản này thủ công để thử nghiệm.

## Chức năng

- Tách Core giám sát dùng chung cho Windows desktop và Linux headless.
- Linux CLI qua SSH: cấu hình, validate, backup import/export, status JSON, doctor, gửi thử cảnh báo và daemon foreground.
- Linux dùng AES-GCM/master key riêng với quyền POSIX riêng tư; Windows giữ DPAPI và đọc được settings/backup cũ. Backup `.ezviztransfer` tương thích hai nền tảng.
- Runtime/lifecycle có task ownership, cancellation, shutdown có deadline và retention bảo vệ event/ảnh đang dùng.
- Thêm service systemd, installer, cập nhật release directory và rollback; giữ nguyên key/config/database.
- Ghép package/checksum chính xác theo platform, không theo thứ tự asset.
- Thêm CI Windows/Ubuntu24.04 và script đóng gói self-contained kèm model/runtime/installer.

## Bộ cài

### Windows x64

Tải `EZVIZ-Local-Monitor-Windows-x64-v1.9.0-preview.5.zip` và `.zip.sha256`, giải nén toàn bộ, chạy `installer\Setup.cmd`. Khi nâng cấp chọn thư mục đã cài; dữ liệu LocalAppData được giữ. Ứng dụng khởi động trong System Tray, hỗ trợ Enter để xác nhận mật khẩu/PIN.

### Ubuntu24.04 x64

Tải `EZVIZ-Local-Monitor-Linux-Headless-x64-v1.9.0-preview.5.tar.gz` và `.tar.gz.sha256sum`. Kiểm tra SHA-256 và nội dung archive trước khi giải nén/cài với quyền root. Linux verifier và hướng dẫn nằm trong [docs/LINUX_HEADLESS.md](https://github.com/huynd94/ezviz-local-monitor/blob/main/docs/LINUX_HEADLESS.md).

Installer tạo account `ezviz-monitor`, prefix `/opt/ezviz-local-monitor`, state `/var/lib/ezviz-local-monitor` và unit `ezviz-local-monitor.service`; không tự enable/start hoặc tạo camera config. Configure/import dưới service user, validate/doctor rồi chủ động enable/start service. Không chạy daemon bằng root, không dùng Windows-mounted `/mnt/c` làm state/key root.

## Kiểm thử và giới hạn

- Ubuntu Core:128 passed; Headless:124 passed.
- Windows Core:120 passed,2 Linux-only skips; Desktop:39 passed.
- Installer fixtures:21 checks; transaction/rollback:12 cases; archive security:5 tests.
- Systemd thật trong WSL đã qua idle install/start/stop, missing-config exit2 không restart-loop, chạy non-root, duplicate lock, idempotent reinstall, upgrade/explicit rollback và automatic rollback khi native doctor lỗi. Key/config/database giữ nguyên.
- Linux/Windows packages đã kiểm model, version, required payload và SHA-256. Optional .NET LTTng plugin ABI0 được loại khỏi Linux package cho Ubuntu24.04; file/journald/EventPipe logging vẫn có.
- Camera thật, host reboot và soak24h/two-camera chưa nghiệm thu. CI đã soạn/parse local; không tuyên bố remote GitHub run đã đạt khi chưa có kết quả.
- Còn warning Windows DPAPI platform analysis và event stub trong test; không có lỗi test/build đã kiểm.

## Quay lại bản trước

Windows: thoát hoàn toàn và cài bộ v1.8.6 vào cùng thư mục, giữ LocalAppData.

Linux: dùng verified old package với `installer/linux/update.sh --package-root OLD_PACKAGE` để chuyển về release trước, hoặc uninstall chương trình/unit và giữ state/key mặc định. Không xóa key nếu còn cần đọc cấu hình local.
