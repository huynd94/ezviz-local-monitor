# EZVIZ Local Monitor v1.8.6

## Sửa lỗi backup chuyển máy

- Xử lý cả lỗi I/O và lỗi quyền ghi khi xuất `.ezviztransfer`, gồm tạo thư mục, ghi file tạm và thay thế file đích.
- Khi file backup đang mở/bị khóa, chỉ đọc hoặc không ghi được, hiển thị hướng dẫn đóng file, chọn tên mới hoặc kiểm tra quyền ghi thay vì thông báo chung "Access to the path is denied".
- Giữ exception gốc để chẩn đoán; thông báo lỗi không chứa mật khẩu hay nội dung cấu hình.
- Test hồi quy xác nhận backup cũ không đổi byte và vẫn giải mã được sau lần xuất thất bại, file tạm được dọn sạch, xuất lại thành công sau khi bỏ khóa/thuộc tính chỉ đọc.
- Giữ nguyên định dạng backup và khả năng nhập file được tạo bởi phiên bản trước.

Các tính năng chạy nền trong System Tray và xác nhận mật khẩu/PIN bằng Enter từ v1.8.5 được giữ nguyên.

## Tải và cài đặt

1. Tải `EZVIZ-Local-Monitor-Windows-x64-v1.8.6.zip` và file `.sha256` đi kèm.
2. Giải nén toàn bộ gói rồi chạy `installer\Setup.cmd`; khi nâng cấp, chọn thư mục đã cài.
3. Mở giao diện từ biểu tượng System Tray. Khi xuất backup, đóng file backup đang mở hoặc chọn tên file mới nếu ứng dụng báo lỗi ghi.

Gói gồm ứng dụng Windows x64 self-contained, model YOLO, Visual C++ x64 Runtime, installer và updater. Nâng cấp vào cùng thư mục không xóa cấu hình, mật khẩu/PIN hay dữ liệu sự kiện tại LocalAppData.

Từ v1.8.5, updater dùng kênh public `huynd94/ezviz-local-monitor`. Với v1.8.4 trở xuống, tải/cài bản mới thủ công một lần hoặc đổi trường Repository trong updater sang kênh này.

## Kiểm tra và giới hạn

- Bộ test Release đạt **22/22**, gồm **7/7** test backup chuyển máy trên Windows.
- Kiểm tra file bị khóa/chỉ đọc, bảo toàn backup cũ, dọn file tạm, thử xuất lại, lỗi tạo thư mục, tạo thư mục còn thiếu và kiểm tra đầu vào.
- Build/publish Windows x64 self-contained; còn cảnh báo CA1416 về Windows DPAPI khi biên dịch lại.
- ZIP qua kiểm tra CRC, thành phần bắt buộc, hash model và có SHA-256 đi kèm. Updater giữ UTF-8 BOM tương thích PowerShell 5.1.
- Chưa kiểm thử end-to-end qua file picker, đăng nhập Windows, watchdog hoặc camera thực.

Nếu cần quay lại phiên bản trước, thoát ứng dụng hoàn toàn rồi cài bộ v1.8.5 vào cùng thư mục; giữ dữ liệu LocalAppData.
