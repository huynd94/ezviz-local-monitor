# Checkpoint runtime, lifecycle và retention (Tasks 6–8)

Workspace: worktree `feature/linux-headless`, baseline v1.8.6. Dừng sau todo này theo yêu cầu người dùng.

## Nội dung hoàn tất trong Core/Windows adapter

- Registry sở hữu event/warm work; đóng admission trước shutdown, drain trước cancel, quan sát lỗi.
- Coordinator/listener/camera đồng bộ startup/fallback/preview/dispose; dispose join startup, reader, inference và event work trước khi giải phóng model/reader.
- Queue giữ quyền sở hữu sender và image lease đến khi sender thoát; shutdown quá deadline không báo clean completion.
- Runtime có một transition gate và một scheduler; cấu hình hiệu lực là bản clone, timezone explicit, hỗ trợ overnight theo ngày bắt đầu lịch.
- Lifetime token của session được giữ sau startup. Startup/restart lỗi được cleanup; retry không tạo session chồng lên disposal chưa hoàn tất.
- Snapshot chuyển sang stopping/faulted thay vì báo running trong transition không hoàn tất; late startup sau cancel không hồi sinh trạng thái healthy.
- Coordinator attests CleanupCompleted sau actual releases, để runtime detach ownership dù late cleanup kết thúc bằng lỗi deadline.
- Observer lỗi được cô lập; event count dedup/publish ngay; preview Mat có hợp đồng single-consumer ownership.
- Scheduler không biến mất sau periodic fault: tạm dừng automatic retry, tiếp tục tick sau thao tác phục hồi thành công.
- DesktopMonitoringController serialize cả intent Start/Pause/Apply; Stop mới hơn không bị continuation của Start cũ ghi đè. Terminal shutdown bypass UI gate để hủy startup.
- Retention dùng batch500/keyset, UTC-equivalent SQLite julianday, policy pending bảo thủ, leases cả cặp ảnh, kiểm tra root/ancestors/symlink và shared references. Xóa thất bại giữ row để retry; không quét/xóa orphan hoặc DeleteAll.

## Bằng chứng kiểm thử cuối sau tích hợp

| Suite | Kết quả |
|---|---|
| Windows Core/shared | 89 passed, 2 skipped, 0 failed (91 total) |
| Windows Desktop | 28 passed, 0 skipped/failed |
| Ubuntu24.04 WSL Core/shared | 97 passed, 0 skipped/failed |
| Ubuntu24.04 WSL Headless storage/native | 44 passed, 0 skipped/failed |

Hai skip Windows là Linux-only permission và symlink theory fixtures (theory collapse thành một skipped test case); các case được thực thi đầy đủ trên Ubuntu. Hai backup locked/read-only theory cases baseline chỉ assert trên Windows, runner Linux vẫn tính passed khi return sớm. Không cộng các bảng thành số test unique.

Đã có red/green cho bảy lỗi runtime ban đầu, bốn lỗi transition/cleanup được review thêm, periodic scheduler recovery và controller UI intent. Subagent lifecycle thêm chín test; subagent retention triển khai 41 case với SQLite/filesystem thật. Test native Headless chạy detector production và silent RTSP peer timeout, không có DISPLAY.

## Giới hạn còn lại

- Auto-retention mỗi24 giờ và việc lấy RetentionDays sẽ được nối trong Linux worker ở Task11; checkpoint này cung cấp core operation cùng RunMaintenanceAsync gate/cancellation, không giả định daemon đã chạy.
- Retention leases bảo vệ các producer hợp tác trong process. External symlink/hard-link TOCTOU và atomicity giữa filesystem/SQLite không được cam kết; state root phải thuộc quyền service user.
- Shared image reference check còn bảo thủ và quét các references theo candidate; chưa có benchmark lớn/soak24h.
- Chưa kiểm thử camera thực hoặc tray/preview GUI end-to-end. Controller tests dùng session điều khiển để chứng minh thứ tự command/cancellation, không chứng minh camera performance.
- Còn CA1416 trong Windows app lock và CS0067 của test event stub. Không có lỗi build/test cuối.
- Chưa triển khai CLI/daemon/systemd mới, chưa commit/push/merge. Main chưa nhận thay đổi source.
