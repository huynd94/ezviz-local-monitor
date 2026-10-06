# EZVIZ Local Monitor v1.8.5

## Thay đổi

- Luôn khởi động trực tiếp trong System Tray, kể cả khi mở bằng executable hoặc shortcut.
- Không tự bật cửa sổ chính, yêu cầu mật khẩu/PIN, onboarding hoặc popup cập nhật lúc khởi động.
- Tiếp tục giám sát theo lịch và gửi cảnh báo camera khi chạy nền; live view chỉ bật sau khi mở giao diện.
- Chỉ yêu cầu mở khóa khi người dùng chủ động mở giao diện từ tray. Hủy hoặc sai hết ba lần vẫn giữ ứng dụng chạy nền.
- Hỗ trợ nhấn Enter để xác nhận mật khẩu/PIN và tự focus ô nhập. Áp dụng cả hộp thoại đặt/đổi khóa và backup chuyển máy.
- Giữ tự khóa khi không thao tác hoạt động sau khi mở từ tray và bảo đảm ứng dụng thoát khi chuyển sang updater.
- Chuyển kênh updater mặc định sang `huynd94/ezviz-local-monitor`.

## Tải và cài đặt

1. Tải `EZVIZ-Local-Monitor-Windows-x64-v1.8.5.zip` và file `.sha256` đi kèm.
2. Giải nén toàn bộ gói, chạy `installer\Setup.cmd`, chọn thư mục đã cài nếu nâng cấp.
3. Sau khi mở ứng dụng, dùng biểu tượng System Tray để mở giao diện; nhập mật khẩu/PIN rồi nhấn Enter nếu đã bật khóa.

Gói gồm ứng dụng Windows x64 self-contained, model YOLO, Visual C++ x64 Runtime, installer và updater. Nâng cấp vào cùng thư mục không xóa cấu hình và dữ liệu tại LocalAppData.

**Chuyển kênh cập nhật:** v1.8.4 trở xuống vẫn trỏ tới kênh cũ. Hãy tải/cài v1.8.5 thủ công một lần hoặc đổi trường Repository trong updater sang `huynd94/ezviz-local-monitor`.

## Kiểm tra và giới hạn

- Publish `Release`, `win-x64`, self-contained thành công. Build còn các cảnh báo CA1416 cho Windows DPAPI.
- Khởi tạo model YOLO bằng ONNX Runtime và thư viện native OpenCV thành công trên Windows.
- ZIP qua kiểm tra CRC, đủ model/runtime/installer/updater và có SHA-256 đi kèm. Các script installer/updater qua parser PowerShell 5.1.
- Hộp thoại Avalonia thực đã được kiểm tra focus, Enter và từ chối đầu vào không hợp lệ với mật khẩu, PIN và nhập lại mật khẩu.
- Chưa kiểm thử end-to-end đăng nhập Windows, watchdog và giám sát với camera thực.
- Bộ test Release: 15/16 đạt. Một test backup hiện có trên Windows yêu cầu `IOException` khi file bị khóa nhưng hệ điều hành trả `UnauthorizedAccessException`; vấn đề này không thuộc thay đổi tray/Enter.

Nếu cần quay lại phiên bản trước, đóng ứng dụng hoàn toàn rồi cài bộ v1.8.4 vào cùng thư mục; giữ dữ liệu LocalAppData.
