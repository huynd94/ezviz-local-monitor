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


## Bản cập nhật AI 0.3.0

| Hạng mục | Kết quả | Ghi chú |
|---|---|---|
| Biên dịch endpoint AI | Đạt | Dịch vụ Chat Completions tương thích OpenAI, màn hình cấu hình và migration SQLite biên dịch thành công. |
| Dữ liệu gửi AI | Đạt theo mã nguồn | Tối đa hai ảnh JPEG (trước/sau sự kiện), không gửi RTSP/video liên tục. |
| Bảo vệ API key | Đạt theo kiến trúc | API key là một phần của `AppSettings` được mã hóa bằng Windows DPAPI. |
| Endpoint thật | Chưa gọi trong môi trường dựng | Không có Base URL/API key của người dùng; cần kiểm tra với endpoint vision tương thích OpenAI thực tế. |
| Hành vi dự phòng | Đạt theo mã nguồn | Nếu AI lỗi/timeout, cảnh báo cục bộ vẫn được gửi; nếu AI không xác nhận, chỉ chặn khi người dùng bật chế độ yêu cầu AI xác nhận. |

Khi nghiệm thu, cấu hình endpoint trong tab **Cảnh báo**, tạo một sự kiện có người, rồi kiểm tra nhật ký có trạng thái `AI hoàn tất`, mô tả ngắn và caption Telegram/Zalo có phần `AI:`. Sau đó thử sai API key hoặc tắt endpoint để xác nhận cảnh báo cục bộ vẫn không bị ngừng.

## Bản cập nhật ONVIF Events 0.4.0

| Hạng mục | Kết quả | Ghi chú |
|---|---|---|
| ONVIF GetCapabilities(Events) | Đạt theo mã nguồn | Listener lấy Event XAddr từ dịch vụ Device. |
| CreatePullPointSubscription/PullMessages | Đạt theo mã nguồn | Dùng WS-Security UsernameToken với `admin` và mã xác thực; PullMessages có timeout dài. |
| Event phân loại người/chuyển động | Đạt theo mã nguồn | Topic/value có từ khóa human/person được đánh dấu người; motion/field/region được đánh dấu chuyển động. |
| RTSP xác minh sau event | Đạt theo mã nguồn | Event phải đọc được một khung RTSP trước khi đi vào pipeline cảnh báo. |
| Fallback YOLO | Đạt theo mã nguồn | ONVIF không hỗ trợ, lỗi xác thực hoặc mất PullPoint sẽ bật CameraMonitor/YOLO. |
| C6N/H8C thật | Chưa thực hiện trong môi trường dựng | Cần chạy trên Windows cùng LAN, bật Alarm Notification/nhận diện người, sau đó quan sát trạng thái `ONVIF Events đang hoạt động` hoặc `fallback YOLO cục bộ`. |

Cảnh báo: ONVIF Events và topic `Human shape detection` phụ thuộc model/firmware. Bản dựng không coi việc ONVIF không expose event là lỗi nghiêm trọng; fallback YOLO vẫn là đường chạy hợp lệ.

## Tối ưu độ trễ cảnh báo 0.5.0

| Thay đổi | Mục tiêu |
|---|---|
| Gửi Telegram tin chữ nhỏ trước ảnh | Người dùng nhận tín hiệu đầu tiên không phải chờ upload ảnh |
| Telegram và Zalo chạy song song | Kênh chậm không chặn kênh còn lại |
| AI chạy hậu kỳ ở chế độ mặc định | Không để endpoint AI làm chậm cảnh báo tức thời |
| RTSP reader giữ kết nối và buffer size thấp | Không mở lại kết nối từ đầu cho từng ONVIF event |
| Ảnh cảnh báo tối đa 1280 px | Giảm kích thước payload upload |
| Lỗi kênh độc lập | Telegram vẫn được thử nếu Zalo lỗi và ngược lại |

Môi trường dựng đã biên dịch thành công. Độ trễ end-to-end Telegram vẫn cần đo trên PC Windows cùng camera và mạng thật, vì còn phụ thuộc firmware camera, router/Wi-Fi, máy chủ Telegram và kích thước ảnh. Khi kiểm tra, ghi thời điểm sự kiện trên nhật ký camera, thời điểm ứng dụng ghi `DetectedAt`, thời điểm Telegram nhận tin chữ và thời điểm nhận ảnh. Chế độ **Chỉ gửi cảnh báo khi AI xác nhận** phải tắt nếu ưu tiên độ trễ thấp nhất.

## Bản vá 0.5.1 — Xác minh cài đặt

Bản 0.5.1 thay nhãn `Bản mẫu v0.1` bằng phiên bản đọc động từ assembly. Theo script cài đặt, nội dung của thư mục `app` trong gói được chép trực tiếp vào thư mục đích, nên file thực thi đúng là `D:\EZVIZ-Local-Monitor\EzvizLocalMonitor.exe` khi dùng thư mục mặc định. Lệnh PowerShell xác minh phải trỏ tới file này, không phải `D:\EZVIZ-Local-Monitor\app\EzvizLocalMonitor.exe`.

## Script cập nhật GitHub 0.5.2

| Kiểm tra | Thiết kế |
|---|---|
| Phát hiện phiên bản hiện tại | Đọc `D:\EZVIZ-Local-Monitor\EzvizLocalMonitor.exe` và `ProductVersion`. |
| Kiểm tra bản mới | Gọi GitHub Releases API của repository private `huyavm/ezviz-local-monitor`. |
| Xác thực nguồn tải | Tải ZIP và SHA-256, so sánh trước khi giải nén/chạy installer. |
| Cập nhật | Dừng process, chạy installer với đúng InstallDir và không mở ứng dụng hai lần. |
| Hậu kiểm | Đọc lại ProductVersion; nếu chưa đạt phiên bản release thì báo lỗi. |
| Không có quyền GitHub | Báo hướng dùng GitHub CLI đã đăng nhập hoặc `EZVIZ_GITHUB_TOKEN` quyền đọc. |

Môi trường dựng đã kiểm tra mã nguồn và build. Việc gọi GitHub private release và cập nhật trực tiếp cần thực hiện trên máy Windows của người dùng, nơi có quyền truy cập repository.

## Bản cập nhật giao diện 0.6.0

| Hạng mục | Kết quả |
|---|---|
| Icon ICO đa kích thước 16–256 px | Đã tạo và tích hợp vào executable/shortcut/title bar |
| Bố cục 1 màn hình | Đã triển khai; hiển thị camera đầu tiên |
| Bố cục 2 màn hình | Đã triển khai; hiển thị hai camera đầu tiên |
| Bố cục 4 màn hình | Đã triển khai; hiển thị tối đa bốn camera |
| Camera chưa cấu hình | Hiển thị ô chờ, không gây lỗi preview |
| Lưu lựa chọn bố cục | Đã lưu trong `DashboardLayoutMode` của cấu hình DPAPI |
| Camera 3–4 | Có thể thêm trong danh sách; cần đo CPU thực tế trước khi chạy bốn luồng YOLO trên i7-7500U |

Bản dựng đã biên dịch thành công trong môi trường dựng. Cần xác minh trực quan trên Windows 10 rằng icon xuất hiện trong shortcut/title bar và chuyển đổi bố cục không làm mất preview camera đang chạy.

## Updater GUI 0.6.1

| Hạng mục | Kết quả thiết kế |
|---|---|
| Cửa sổ tiến trình WinForms | Hiển thị trạng thái và phần trăm cho kiểm tra, tải ZIP, tải SHA-256, xác minh, giải nén, cài đặt và hậu kiểm. |
| Repository private | Hỗ trợ GitHub CLI đã đăng nhập, `EZVIZ_GITHUB_TOKEN` hoặc token nhập trực tiếp trong giao diện. |
| Cập nhật đè | Dùng `ForceUpdate`, không dừng ở hộp thoại thư mục đã tồn tại. |
| Không chạy nhầm package | Chỉ chạy installer sau khi ZIP và SHA-256 hợp lệ. |
| Hậu kiểm phiên bản | Đọc lại `D:\EZVIZ-Local-Monitor\EzvizLocalMonitor.exe` và báo lỗi nếu chưa đạt release. |
| Chạy nhanh | Có `Update-EzvizLocalMonitor.cmd` để mở giao diện bằng double-click. |

Bản GUI cần được kiểm tra trực tiếp trên Windows 10 vì môi trường dựng không có Windows PowerShell/WinForms để chạy giao diện thật.
