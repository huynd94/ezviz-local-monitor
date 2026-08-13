# Báo cáo kiểm thử phát hành v0.1.0

## Phạm vi đã kiểm tra

Bản dựng được biên dịch thành công ở cấu hình `Release` cho kiến trúc `win-x64`. Gói phát hành là ứng dụng .NET tự chứa, do đó người dùng Windows 10 không cần cài .NET runtime riêng. Tệp khởi động được kiểm tra là PE32+ GUI x64 dành cho Windows.

| Hạng mục | Kết quả | Ghi chú |
|---|---|---|
| Biên dịch mã nguồn | Đạt | `dotnet build -c Release` thành công, không có lỗi biên dịch. |
| Xuất bản win-x64 tự chứa | Đạt | `dotnet publish -c Release -r win-x64 --self-contained true` thành công. |
| Thành phần RTSP | Đạt ở mức gói | Có `OpenCvSharpExtern.dll` trong `app/`; cần kiểm tra stream thật trên Windows. |
| Thành phần suy luận | Đạt ở mức gói | Có `onnxruntime.dll` và `Models/yolov8n.onnx` trong `app/`. |
| Bộ cài mặc định ổ D | Đạt ở mức mã/đóng gói | `Install-EzvizLocalMonitor.ps1` mặc định `D:\EZVIZ-Local-Monitor`, có `-ChooseLocation`. |
| Kiểm tra toàn vẹn gói | Đạt | SHA-256: `7f5d8fafda63c74e06085ca2d6eae43e78af81bd9dad81b868c079e811939519`. |

## Hạng mục cần kiểm tra khi cài thật

Các giá trị RTSP, Bot Token, Chat ID và camera thật không được cung cấp và không nên đưa vào gói kiểm thử. Vì vậy, các kiểm tra dưới đây phải được thực hiện sau khi cài trên PC Windows 10 cùng mạng với C6N/H8C.

| Bước | Kết quả mong đợi |
|---|---|
| Chạy `Setup.cmd` và chấp nhận đường dẫn mặc định | Ứng dụng được đặt trong `D:\EZVIZ-Local-Monitor` và mở được. |
| Chạy lại `Setup.cmd`, chọn `n`, rồi chọn thư mục khác | Ứng dụng được cài vào thư mục được chọn. |
| Thêm C6N và bấm **Kiểm tra RTSP** | Luồng hiển thị hoặc trạng thái lỗi hữu ích để điều chỉnh RTSP/Local Service. |
| Thêm H8C và bấm **Kiểm tra RTSP** | Xác nhận firmware H8C hiện tại cho phép ứng dụng đọc RTSP. |
| Bấm **Gửi thử** Telegram và Zalo Bot | Tin nhắn văn bản kiểm thử đi đến đúng `chat_id`. |
| Đi qua vùng quan sát | Sau 2/3 lần nhận diện, nhận ảnh kèm caption; không lặp trong khoảng im lặng 30 giây. |
| Ngắt Wi-Fi một camera rồi kết nối lại | Trạng thái lỗi hiển thị, sau đó camera tự kết nối lại; camera còn lại tiếp tục chạy. |

> Không kết luận về độ chính xác hoặc mức sử dụng CPU cho đến khi hoàn thành kiểm thử với ánh sáng, vị trí lắp và độ ổn định Wi-Fi thực tế của hai camera.

## Bổ sung v0.2 — Tự tìm camera LAN

| Hạng mục | Kết quả | Ghi chú |
|---|---|---|
| Biên dịch dịch vụ WS-Discovery/RTSP scan | Đạt | Bản `Release` biên dịch thành công sau khi thêm dịch vụ khám phá LAN. |
| Giới hạn phạm vi quét | Đạt theo thiết kế | Chỉ WS-Discovery multicast và cổng RTSP 554 trong các subnet IPv4 riêng `/24` đang hoạt động. |
| Nhập mã xác thực | Đạt theo mã nguồn | Người dùng chọn IP, nhập mã; ứng dụng tạo RTSP với `admin` và chỉ lưu sau khi đọc thử khung hình. |
| Quét C6N/H8C thật | Chưa thực hiện trong môi trường dựng | Cần chạy trên PC Windows cùng LAN với camera, vì môi trường dựng không có camera và không dùng thông tin nội bộ. |

Khi nghiệm thu trên máy thực tế, bấm **Tìm camera trong LAN**, chọn ứng viên C6N/H8C, nhập mã xác thực sáu ký tự trên nhãn và kiểm tra ứng dụng tự tạo RTSP rồi thêm camera. Nếu không có kết quả, thử mở Local Service/RTSP trong EZVIZ và dùng RTSP thủ công như phương án dự phòng.

## Bản vá 0.2.1 — Hiển thị tiếng Việt khi cài đặt

Bản 0.2.1 khắc phục lỗi thông báo tiếng Việt bị sai dấu trong bộ cài. Nguyên nhân là tập lệnh PowerShell trước đó dùng UTF-8 không BOM, trong khi Windows PowerShell 5.1 thường cần BOM để nhận diện chính xác tệp có ký tự tiếng Việt. Bản vá đặt UTF-8 BOM ở đầu `Install-EzvizLocalMonitor.ps1`, thiết lập `Console.InputEncoding`, `Console.OutputEncoding` và `$OutputEncoding` sang UTF-8, đồng thời chuyển mã trang console của `Setup.cmd` sang 65001 trước khi mở PowerShell.

| Kiểm tra | Kết quả |
|---|---|
| Byte đầu PowerShell installer | `EF BB BF` (UTF-8 BOM) |
| Nội dung installer | Xác nhận là UTF-8 hợp lệ có chuỗi tiếng Việt |
| Setup.cmd | Có `chcp 65001 >nul` trước khi gọi PowerShell |
| Biên dịch Release x64 | Thành công, không có lỗi biên dịch |

