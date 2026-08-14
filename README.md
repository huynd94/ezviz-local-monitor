# EZVIZ Local Monitor for Windows

Bản 0.6.7 có icon ứng dụng riêng, Auto-updater kiểm tra bản mới khi mở ứng dụng, updater GUI đã sửa lỗi Runspace và trang **Tổng quan** với bố cục linh động **1, 2 hoặc 4 màn hình**. Chọn bố cục ở hàng nút phía trên lưới camera; lựa chọn được lưu trong cấu hình Windows và giữ lại ở lần mở sau. Bố cục 1 màn hình hiển thị camera đầu tiên, bố cục 2 hiển thị hai camera đầu tiên, còn bố cục 4 hiển thị tối đa bốn camera; các ô chưa cấu hình vẫn hiện trạng thái chờ.

**Phiên bản:** 0.6.7  
**Mục đích:** Giám sát cục bộ tối đa bốn camera EZVIZ, phát hiện **người** tại máy Windows và gửi ảnh kèm văn bản qua Telegram cùng Zalo Bot Platform. Với i7-7500U/RAM 8 GB, nên bắt đầu với hai camera rồi đo CPU trước khi bật bốn luồng YOLO đồng thời.

## Script cập nhật nhanh từ GitHub

Gói hiện tại có updater dạng cửa sổ tại `scripts\Update-EzvizLocalMonitor.ps1` và file chạy nhanh `scripts\Update-EzvizLocalMonitor.cmd`. Double-click file `.cmd` để mở giao diện. Cửa sổ hiển thị phiên bản hiện tại, phiên bản mới nhất, trạng thái kết nối GitHub, tiến trình tải, xác minh SHA-256, giải nén và cài đặt.

Mã nguồn vẫn nằm trong repository private. Các gói phát hành được phân phối qua repository public `huyavm/ezviz-local-monitor-releases`, vì vậy updater mặc định không cần GitHub token. Trường Repository chỉ cần thay đổi nếu bạn có kênh phát hành riêng.

Khi mở ứng dụng, Auto-updater sẽ kiểm tra release public ở chế độ nền với thời gian chờ ngắn để không làm chậm giao diện. Nếu có bản mới, ứng dụng hiển thị lựa chọn **Cập nhật ngay**, **Để sau** hoặc **Mở trang release**. Chọn cập nhật sẽ mở updater GUI riêng, dừng giám sát an toàn, tải gói, xác minh SHA-256 và tự mở lại ứng dụng sau khi hoàn tất. Nếu không có mạng hoặc GitHub tạm thời không phản hồi, ứng dụng vẫn mở và giám sát bình thường.

Trong updater GUI, bấm **Kiểm tra bản mới** để chỉ kiểm tra. Khi có bản mới, bấm **Cập nhật ngay**, xác nhận hộp thoại, rồi chờ đến trạng thái **Cập nhật thành công**. Có thể bỏ chọn **Mở ứng dụng sau khi cập nhật** nếu cần.

Có thể chạy bằng PowerShell nếu cần:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Update-EzvizLocalMonitor.ps1
```

## Bố cục Tổng quan và tự động giám sát

Trong tab **Tổng quan**, bố cục **1 màn hình** dùng toàn bộ vùng lưới cho camera thứ nhất; bố cục **2 màn hình** chia đều thành hai cột; bố cục **4 màn hình** chia thành lưới 2×2. Khi cửa sổ mở, ứng dụng tự động bắt đầu giám sát nếu đã có ít nhất một camera bật và có RTSP URL hợp lệ. Nút **Dừng** tạm dừng giám sát, còn **Bắt đầu giám sát** khởi động lại khi người dùng muốn.

## Bản vá xác minh phiên bản 0.5.1

Bản 0.5.1 thay nhãn phiên bản cứng trong giao diện bằng phiên bản assembly thực tế. File `EzvizLocalMonitor.exe` được cài trực tiếp vào thư mục đích, ví dụ `D:\EZVIZ-Local-Monitor\EzvizLocalMonitor.exe`, không nằm trong thư mục con `app`.

Sau khi cài, xác minh bằng PowerShell:

```powershell
(Get-Item 'D:\EZVIZ-Local-Monitor\EzvizLocalMonitor.exe').VersionInfo | Select-Object ProductVersion,FileVersion
```

Kết quả của bản này phải là `0.5.1`.

## Tối ưu cảnh báo thời gian thực 0.5.0

Bản 0.5.0 ưu tiên cảnh báo Telegram gần thời gian thực: gửi tin chữ nhỏ trước, upload ảnh ngay sau đó, chạy Telegram và Zalo song song, và không chờ AI khi AI chỉ dùng để bổ sung mô tả. Ảnh cảnh báo được thu nhỏ tối đa 1280 px để giảm thời gian upload. Telegram có thể hiển thị hai tin liên tiếp cho cùng một sự kiện: tin chữ đến trước và tin ảnh đến sau.

## Bản cập nhật ONVIF Events 0.4.0

Bản 0.4.0 ưu tiên nhận sự kiện chuyển động/người tích hợp sẵn qua **ONVIF Events/PullPoint** trong LAN. Khi camera có event phù hợp, ứng dụng chỉ lấy một khung RTSP để xác minh và không chạy YOLO liên tục cho camera đó. Nếu ONVIF không hỗ trợ, xác thực thất bại hoặc mất kết nối, ứng dụng tự chuyển sang **YOLO cục bộ**.

## Bản cập nhật AI 0.3.0

Bản 0.3.0 bổ sung endpoint **OpenAI-compatible Chat Completions** để đánh giá ảnh sự kiện. AI nhận tối đa hai ảnh JPEG (khung ngay trước sự kiện và khung sự kiện), trả về `motion_detected`, `person_present`, độ tin cậy và mô tả ngắn. Phát hiện RTSP/người cục bộ tiếp tục chạy trong LAN; AI tắt theo mặc định.

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
| AI OpenAI-compatible | Base URL, model vision, API key và timeout | Bật AI sau khi xác nhận endpoint chấp nhận ảnh qua Chat Completions. |

Zalo Bot có thể trả HTTP 200 nhưng vẫn báo lỗi nghiệp vụ trong JSON với `ok: false`. Từ v0.6.6, ứng dụng hiển thị thêm `description` và `error_code`, ví dụ lỗi token, `chat_id` hoặc quyền gửi, thay vì chỉ báo chung là `HTTP 200`. Theo tài liệu Zalo Bot, response thành công phải có `ok: true`; khi lỗi cần xem `description` và `error_code`. [6]

Khi AI được bật, endpoint nhận tối đa **hai ảnh JPEG của mỗi sự kiện**; video RTSP không được gửi. Chế độ mặc định chỉ thêm mô tả AI vào hậu kỳ sau khi cảnh báo đã được gửi. Nếu bật **Chỉ gửi cảnh báo khi AI xác nhận**, cảnh báo sẽ chờ AI và độ trễ có thể tăng đáng kể. Nếu endpoint lỗi hoặc hết timeout, ứng dụng vẫn gửi cảnh báo cục bộ theo quy tắc ban đầu và ghi lỗi AI vào nhật ký.

Telegram có API HTTPS với các phương thức gửi ảnh và văn bản. [4] Bot ZApps công bố endpoint `sendPhoto` theo dạng `https://bot-api.zaloplatforms.com/bot<BOT_TOKEN>/sendPhoto`, yêu cầu `chat_id` và `photo`, với `caption` tùy chọn. [3] Ứng dụng gửi ảnh JPEG sự kiện và caption gồm tên camera, thời điểm, độ tin cậy.

## ONVIF Events và fallback

Camera được tự tìm từ LAN cần có `OnvifServiceUrl` và RTSP URL chứa mã xác thực. Khi bấm **Bắt đầu giám sát**, ứng dụng thử ONVIF Events bằng `admin` và mã xác thực đã lưu. Nếu thành công, trạng thái camera sẽ ghi **ONVIF Events đang hoạt động**; nếu không, trạng thái sẽ ghi **fallback YOLO cục bộ**. Event `Human shape detection` được coi là phát hiện người; `Motion alarm` được coi là chuyển động tổng quát và vẫn cần khung RTSP xác minh.

Để kiểm tra, bật Alarm Notification/nhận diện người trên EZVIZ App, đi qua vùng quan sát và xem trạng thái camera cùng nhật ký sự kiện. Không phải mọi firmware C6N/H8C đều expose cùng topic ONVIF; vì vậy trạng thái fallback là hành vi bình thường, không phải lỗi cài đặt.

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

[6] [Zalo Bot Platform — Cách gọi API và định dạng response](https://bot.zapps.me/docs/call-api/)
