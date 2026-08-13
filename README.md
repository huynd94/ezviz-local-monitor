# EZVIZ Local Monitor for Windows

**Phiên bản:** 0.2.1  
**Mục đích:** Giám sát cục bộ tối đa hai camera EZVIZ C6N/H8C, phát hiện **người** tại máy Windows và gửi ảnh kèm văn bản qua Telegram cùng Zalo Bot Platform.

## Bản vá 0.2.1

Bộ cài đã được chuyển sang **UTF-8 có BOM** và thiết lập console UTF-8 trước khi chạy PowerShell. Thông báo tiếng Việt trong cửa sổ cài đặt, hộp xác nhận và phần ghi ra terminal sẽ không còn bị lỗi dấu trên Windows PowerShell 5.1/Windows 10.

## Tóm tắt vận hành

Ứng dụng chạy trên Windows 10 x64 và dùng máy cục bộ để lấy luồng RTSP, suy luận mô hình ONNX, ghi nhật ký SQLite và lưu ảnh sự kiện. Video thô không đi qua dịch vụ AI bên ngoài. Khi bạn bật Telegram hoặc Zalo Bot, **ảnh sự kiện** và phần mô tả mới được gửi qua HTTPS đến các kênh này.

| Hạng mục | Bản v0.2 |
|---|---|
| Số camera tối đa | 2, tối ưu theo i7-7500U/RAM 8 GB |
| Thêm camera | Tự tìm ONVIF/RTSP trong LAN rồi nhập mã xác thực trên nhãn |
| Camera mục tiêu | EZVIZ C6N và H8C trong cùng LAN |
| Tốc độ suy luận khởi đầu | 1 lần/giây/camera |
| Điều kiện sự kiện | Có người trong 2 trên 3 lần suy luận gần nhất |
| Chống gửi lặp | 30 giây/camera, cấu hình được |
| Lưu ảnh mặc định | 14 ngày trong `%LOCALAPPDATA%\EZVIZ Local Monitor\Events` |
| Bảo vệ token/cấu hình | Windows DPAPI gắn tài khoản Windows hiện tại |

## Cài đặt

Giải nén thư mục phát hành `EZVIZ-Local-Monitor-Windows-x64.zip` vào vị trí tạm thời. Có hai cách chạy bộ cài.

| Nhu cầu | Thao tác |
|---|---|
| Cài vào vị trí mặc định | Mở PowerShell trong thư mục bộ cài và chạy `powershell -ExecutionPolicy Bypass -File .\Install-EzvizLocalMonitor.ps1`. Thư mục mặc định là `D:\EZVIZ-Local-Monitor`. |
| Chọn thư mục khác | Chạy `powershell -ExecutionPolicy Bypass -File .\Install-EzvizLocalMonitor.ps1 -ChooseLocation` để mở hộp chọn thư mục. |
| Không tạo shortcut Desktop | Thêm tham số `-NoShortcut`. |

Bộ cài đặt ứng dụng trong thư mục bạn chọn. Dữ liệu sự kiện, cơ sở dữ liệu và cấu hình bí mật **không** đặt trong thư mục cài; chúng nằm tại `%LOCALAPPDATA%\EZVIZ Local Monitor`. Vì vậy, nâng cấp ứng dụng không làm mất nhật ký hoặc cấu hình.

> Khi cập nhật, đóng ứng dụng trước rồi chạy lại bộ cài vào cùng thư mục. Để gỡ, đóng ứng dụng và chạy `Uninstall-EzvizLocalMonitor.ps1` trong thư mục cài. Hành động gỡ không tự xóa dữ liệu sự kiện dưới LocalAppData; người dùng cần xóa thủ công nếu muốn xóa toàn bộ lịch sử.

## Chuẩn bị camera

Camera và máy tính Windows phải ở cùng mạng LAN. Tài liệu EZVIZ hướng dẫn C6N dùng RTSP theo mẫu sau, trong đó mã xác thực là sáu ký tự in hoa trên nhãn camera: [1]

```text
rtsp://admin:<VERIFICATION_CODE>@<CAMERA_IP>:554/ch1/main
```

Ví dụ minh họa:

```text
rtsp://admin:ABCDEF@192.168.1.50:554/ch1/main
```

Trong ứng dụng EZVIZ, bật Local View/Local Service hoặc RTSP nếu firmware hiển thị tùy chọn đó. H8C cần được kiểm tra bằng nút **Kiểm tra RTSP** trong ứng dụng, dù Local View hoạt động, vì Local View và RTSP là hai cơ chế khác nhau.

Không mở cổng RTSP 554 ra Internet. Nên giữ camera và PC bằng IP tĩnh hoặc DHCP reservation, đồng thời dùng mạng IoT/VLAN riêng nếu router có hỗ trợ.

## Thiết lập trong ứng dụng

Mở **EZVIZ Local Monitor**, vào tab **Camera** và bấm **Tìm camera trong LAN**. Ứng dụng ưu tiên nhận phản hồi ONVIF (nếu firmware hỗ trợ), sau đó chỉ dò cổng RTSP 554 trong các mạng `/24` riêng đang kết nối với PC. Chọn ứng viên trong danh sách, nhập **mã xác thực** trên nhãn camera và bấm **Thêm camera đã chọn**. Ứng dụng tự tạo URL RTSP, đọc thử một khung hình và chỉ lưu camera khi xác thực thành công.

Nếu camera không xuất hiện, điều đó không khẳng định camera hỏng: ONVIF/RTSP có thể chưa bật, firmware không hỗ trợ ONVIF, hoặc camera ở VLAN khác. Khi đó vẫn có thể nhập RTSP URL thủ công và bấm **Kiểm tra RTSP**. Chỉ khi kiểm tra thành công mới nên bấm **Bắt đầu giám sát**. Chọn ngưỡng 55% và khoảng im lặng 30 giây làm mốc ban đầu; tăng ngưỡng nếu cảnh báo nhầm, hoặc giảm nhẹ nếu bỏ sót người ở xa.

Ở tab **Cảnh báo**, chỉ nhập trực tiếp trên máy Windows các giá trị dưới đây. Không gửi token hoặc mã xác thực qua email/chat.

| Kênh | Giá trị cần có | Kiểm tra |
|---|---|---|
| Telegram | Bot token và `chat_id` | Bấm **Gửi thử**. Bot/nhóm phải cho phép bot gửi tin. |
| Zalo Bot Platform | `BOT_TOKEN` và `chat_id` của Bot ZApps | Bấm **Gửi thử**. API dùng Bot Token và hỗ trợ `sendMessage`/`sendPhoto`. [2] [3] |

Telegram có API HTTPS với các phương thức gửi ảnh và văn bản. [4] Bot ZApps công bố endpoint `sendPhoto` theo dạng `https://bot-api.zaloplatforms.com/bot<BOT_TOKEN>/sendPhoto`, yêu cầu `chat_id` và `photo`, với `caption` tùy chọn. [3] Ứng dụng gửi ảnh JPEG sự kiện và caption gồm tên camera, thời điểm, độ tin cậy.

## Giới hạn và kiểm thử cần thực hiện

Đây là bản mẫu hoạt động theo chính sách nhận diện **sự hiện diện của người**, không nhận dạng khuôn mặt, danh tính hay biển số. Không sử dụng nó như biện pháp duy nhất cho an ninh hoặc phản ứng khẩn cấp.

Trước khi vận hành thường xuyên, hãy đi qua vùng quan sát của mỗi camera vào ban ngày và ban đêm, sau đó kiểm tra: ảnh nhận được có đúng camera không, một người đứng lâu không gây spam, camera mất Wi-Fi có tự kết nối lại không, và CPU của máy vẫn phản hồi tốt. Trên i7-7500U/RAM 8 GB, giữ mặc định 1 lần suy luận/giây/camera; chỉ tăng sau khi đo thực tế.

Mô hình `yolov8n.onnx` được đóng gói để chạy hoàn toàn cục bộ. Ultralytics công bố ONNX là định dạng triển khai tương thích cho mô hình YOLO; cần đánh giá điều khoản của mô hình nếu sử dụng cho mục đích thương mại. [5]

## Lưu ý về tự tìm camera

Việc quét chỉ xảy ra khi bạn bấm nút và không gửi kết quả ra ngoài LAN. Danh sách chỉ hiển thị IP, cổng và nguồn phát hiện; mã xác thực không được hiển thị lại, và RTSP URL chứa mã được lưu trong cấu hình DPAPI của tài khoản Windows. Tài liệu EZVIZ mô tả LAN Live View theo cùng nguyên tắc username `admin` và mã xác thực thiết bị. [1]

## Làm việc từ mã nguồn

Repository chỉ chứa mã nguồn và tài liệu; không đưa gói cài đặt hoặc mô hình ONNX nhị phân vào lịch sử Git. Sau khi clone repository, chạy `powershell -ExecutionPolicy Bypass -File .\scripts\Get-YoloModel.ps1` để tải mô hình cục bộ, rồi dùng `dotnet publish .\src\EzvizLocalMonitor\EzvizLocalMonitor.csproj -c Release -r win-x64 --self-contained true` để tạo bản phát hành Windows.

## Cấu trúc gói phát hành

```text
EZVIZ-Local-Monitor-Windows-x64/
├── Install-EzvizLocalMonitor.ps1    # Bộ cài mặc định D:\EZVIZ-Local-Monitor
├── app/                              # Ứng dụng .NET self-contained x64
└── README.md                         # Hướng dẫn này
```

## Tài liệu tham khảo

[1] [EZVIZ Support — How to set up C6N/TY1/TY2 as a webcam](https://support.ezviz.com/faq/article/How-to-set-up-C6N-TY1-TY2-as-a-webcam)

[2] [Zalo Bot Platform — Xác thực](https://bot.zapps.me/docs/authorize/)

[3] [Zalo Bot Platform — sendPhoto](https://bot.zapps.me/docs/apis/sendPhoto/)

[4] [Telegram — Bot API](https://core.telegram.org/bots/api)

[5] [Ultralytics — Model Export](https://docs.ultralytics.com/modes/export/)
