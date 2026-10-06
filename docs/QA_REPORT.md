# Báo cáo kiểm thử phát hành

## v1.8.6 — lỗi xuất backup chuyển máy

Đã tái hiện ba lỗi trước khi sửa: ghi đè file bị khóa và file chỉ đọc trả `UnauthorizedAccessException`, còn lỗi tạo thư mục đích chưa có thông báo hướng dẫn. Bản sửa đưa việc tạo thư mục vào cùng khối xử lý ghi file, bắt riêng lỗi I/O/quyền ghi và trả `IOException` có thông báo tiếng Việt cùng exception gốc.

| Hạng mục | Kết quả | Ghi chú |
|---|---|---|
| File đích bị khóa/chỉ đọc trên Windows | Đạt | Bắt lỗi, giữ nguyên byte backup cũ, nhập lại được cấu hình cũ và không để lại file tạm. |
| Xuất lại sau khi bỏ khóa/chỉ đọc | Đạt | Ghi đè thành công, nhập lại được cấu hình mới. |
| Lỗi tạo thư mục đích | Đạt | Thông báo hướng dẫn xử lý; file chắn đường dẫn vẫn nguyên vẹn. |
| Tạo thư mục còn thiếu | Đạt | Tạo thư mục lồng nhau và xuất/nhập backup thành công. |
| Đường dẫn trống / mật khẩu quá ngắn | Đạt | Giữ `ArgumentException`, không tạo file. |
| Xuất/nhập và mật khẩu sai | Đạt | Round-trip đúng cấu hình; sai mật khẩu bị từ chối. |
| Thông báo lỗi | Đạt ở mức service | Có hướng dẫn chọn file mới/kiểm tra quyền ghi, có InnerException, không chứa mật khẩu/API key thử nghiệm. Giao diện hiện sử dụng `ex.Message`; chưa thao tác end-to-end qua file picker. |
| Bộ test Release | Đạt | `TransferBackupTests`: 7/7; toàn bộ: 22/22, không có test bị bỏ qua trên máy Windows kiểm thử. |
| Publish Windows x64 self-contained | Đạt | `dotnet publish -c Release -r win-x64 --self-contained true --no-restore`; còn cảnh báo CA1416 về Windows DPAPI khi biên dịch lại. |
| Bộ cài ZIP | Đạt ở mức đóng gói | Đủ runtime, model YOLO, installer, VC++ Runtime và updater; kiểm tra CRC, hash model, UTF-8 BOM của updater và tạo SHA-256 cho gói cuối cùng. |

## v1.8.5 — tray mặc định, Enter và kênh phát hành mới

| Hạng mục | Kết quả | Ghi chú |
|---|---|---|
| Publish Windows x64 self-contained | Đạt | `dotnet publish -c Release -r win-x64 --self-contained true --no-restore`; còn cảnh báo CA1416 về Windows DPAPI. |
| Mật khẩu/PIN và xác nhận bằng Enter | Đạt | Hộp thoại Avalonia thực: focus ô nhập, từ chối dữ liệu sai định dạng/độ dài, từ chối nhập lại không khớp và nhận kết quả khi Enter. |
| ONNX Runtime / YOLO / OpenCV | Đạt | Nạp model YOLO và tạo Mat OpenCV thành công trên Windows. |
| Installer và updater | Đạt ở mức parser/gói | Parser PowerShell 5.1 không có lỗi; updater giữ UTF-8 BOM và dùng `huynd94/ezviz-local-monitor`. |
| ZIP và SHA-256 | Đạt ở mức đóng gói | Kiểm tra CRC, danh sách thành phần bắt buộc và hash model; file `.sha256` được tạo cho ZIP cuối cùng. |
| Bộ test Release | 15/16 đạt | Test backup file bị khóa chờ `IOException` nhưng Windows trả `UnauthorizedAccessException`. |
| Tray, Windows logon, watchdog và camera thật | Chưa kiểm thử end-to-end | Đã rà soát luồng lifetime/khởi tạo nền; cần xác minh hành vi trên máy sử dụng thực tế. |

Repository mới đã chuyển sang public theo quyết định của chủ repository. Quét 376 Git blobs lịch sử, gồm nội dung ZIP, không phát hiện mẫu credential đã kiểm tra. Chi tiết thay đổi và hướng dẫn chuyển kênh cập nhật nằm trong `docs/RELEASE_v1.8.5.md`.

## Lịch sử v0.1.0

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

## Bản vá Runspace updater 0.6.2

Lỗi `There is no Runspace available to run scripts in this thread` phát sinh do `BackgroundWorker` thực thi scriptblock PowerShell trên thread phụ không có Runspace mặc định trong Windows PowerShell 5.1. Bản 0.6.2 loại bỏ `BackgroundWorker`; giao diện WinForms khởi chạy `Update-EzvizLocalMonitor-Worker.ps1` bằng một process PowerShell riêng, đọc tiến trình qua file trạng thái và cập nhật giao diện bằng Timer trên UI thread.

| Kiểm tra | Kết quả |
|---|---|
| Không còn `BackgroundWorker`/`RunWorkerAsync` trong GUI updater | Đã kiểm tra tĩnh |
| Worker riêng có trạng thái progress/result | Đã triển khai |
| Repository private và SHA-256 | Giữ nguyên |
| ForceUpdate và hậu kiểm phiên bản | Giữ nguyên |
| Chạy GUI thực tế trên Windows PowerShell 5.1 | Cần xác minh trên máy Windows của người dùng |

## Kênh phát hành public 0.6.3

Repository public phát hành: `huyavm/ezviz-local-monitor-releases`. Repository này chỉ chứa ZIP tự chứa Windows và SHA-256 của release; mã nguồn, model phát triển, cấu hình camera, token và dữ liệu sự kiện vẫn nằm ngoài repository public. Updater mặc định trỏ tới kênh public nên không yêu cầu GitHub token.

## Auto-updater khi khởi động — v0.6.4

Ứng dụng kiểm tra release public `huyavm/ezviz-local-monitor-releases` sau khi cửa sổ chính đã mở, dùng HTTP timeout ngắn và không yêu cầu token. Nếu không có mạng, GitHub không phản hồi hoặc release không hợp lệ, lỗi được bỏ qua để không ngăn ứng dụng giám sát cục bộ khởi động. Khi phát hiện phiên bản mới, người dùng có thể chọn cập nhật ngay, để sau hoặc mở trang release. Lựa chọn cập nhật dừng coordinator, mở updater GUI bằng tham số xác nhận tự động, rồi updater tiếp tục xác minh SHA-256 và khởi động lại ứng dụng sau khi cài.

## Bố cục Tổng quan và tự động giám sát — v0.6.7

Logic lưới đã được điều chỉnh: chế độ 1 dùng một cột duy nhất để camera chiếm toàn bộ vùng hiển thị; chế độ 2 dùng hai cột chia đều; chế độ 4 dùng hai hàng và hai cột. Sau khi cửa sổ mở, ứng dụng tự động gọi cùng pipeline giám sát của nút **Bắt đầu giám sát** nếu có camera bật và RTSP URL hợp lệ. Nút **Dừng** vẫn hủy coordinator và nút **Bắt đầu giám sát** có thể khởi động lại pipeline.

## Sửa sendPhoto Zalo — v0.6.8

Kiểm thử nút **Gửi thử** thành công xác nhận Bot Token và Chat ID hợp lệ, nhưng cảnh báo thực tế trả `HTTP 200; error_code=400; The photo must not be empty`. Nguyên nhân nằm ở đường gửi ảnh `sendPhoto`, không phải `sendMessage`. Bản vá đổi nội dung ảnh từ stream file sang `ByteArrayContent` có độ dài xác định, kiểm tra file JPEG tồn tại và không rỗng trước khi tạo multipart request; đồng thời `Cv2.ImWrite` cũng phải trả thành công và tạo ra tệp có kích thước lớn hơn 0.

## Tray icon và tối ưu preview — v0.6.9

Khi người dùng đóng cửa sổ, `Window_Closing` hủy thao tác đóng mặc định và ẩn cửa sổ; coordinator vẫn chạy, vì vậy camera, YOLO/ONVIF và cảnh báo tiếp tục hoạt động. Tray menu có các lệnh mở lại, ẩn và thoát hoàn toàn. Lệnh thoát đặt cờ shutdown rồi kết thúc desktop lifetime để coordinator được dispose đúng cách.

RTSP snapshot được warm ở task nền thay vì chặn lúc khởi động. Luồng preview được gộp theo camera: mỗi camera chỉ có một lần cập nhật UI đang chờ, frame mới thay thế frame cũ chưa hiển thị và bitmap cũ được giải phóng. Điều này tránh tích tụ hàng đợi Dispatcher khi camera phát nhiều frame và giảm giật lag khi mở ứng dụng.

## Zalo diagnostics và sửa cảnh báo thực tế — v0.7.0

Ảnh nhật ký người dùng cho thấy `sendMessage` kiểm thử có thể thành công, trong khi cảnh báo thực tế trước đây chỉ gọi `sendPhoto` bằng multipart file local. Zalo Bot API mô tả `photo` là một chuỗi đường dẫn ảnh; một file path trong máy LAN không phải URL HTTPS mà máy chủ Zalo có thể truy cập. Điều này giải thích các response `error_code=400` với `chat_id/photo must not be empty` khi endpoint không phân tích multipart như một file upload.

Bản v0.7.0 gửi `sendMessage` trước để cảnh báo chữ không bị mất, chỉ gọi `sendPhoto` khi giá trị ảnh là URL HTTPS hợp lệ, và ghi log khi ảnh local bị bỏ qua. Log tại `%LOCALAPPDATA%\EZVIZ Local Monitor\zalo-send.log` ghi thao tác, HTTP status, Chat ID đã che, độ dài Chat ID, trạng thái file/kích thước ảnh, cờ `photoIsHttpsUrl` và response rút gọn; không ghi Bot Token đầy đủ.

## Gia cố exception và logging Zalo — v0.7.1

AlertDispatcher tạo timeout riêng tối đa 12 giây cho từng thao tác Zalo và liên kết với CancellationToken của tiến trình. `sendMessage` và `sendPhoto` được xử lý độc lập; lỗi của ảnh không làm mất tin chữ, lỗi của Zalo không làm hỏng Telegram, và lỗi tổng hợp của `Task.WhenAll` được bắt trước khi trả trạng thái.

Các nhóm lỗi được cô lập gồm thiếu token/Chat ID, hủy hoặc timeout, lỗi DNS/HTTP, response JSON không hợp lệ, file ảnh bị xóa/không thể đọc, URL ảnh không hợp lệ, lỗi metadata và lỗi ghi log. Logger sử dụng worker nền, giới hạn 128 bản ghi chờ, tự xoay ở 2 MB và nuốt mọi lỗi I/O. Log chỉ ghi Chat ID đã che, không ghi Bot Token đầy đủ.

Build Release đã thành công với 0 lỗi biên dịch. Các cảnh báo CA1416 hiện hữu liên quan Windows DPAPI không thuộc thay đổi Zalo.

## UI/UX và vận hành — v0.8.0

Checklist triển khai theo thứ tự yêu cầu:

1. Tổng quan: trạng thái từng camera, số camera hoạt động, cảnh báo kết nối và thanh trạng thái hệ thống.
2. Cảnh báo: chia nhóm Kênh thông báo/Phân tích AI/Chẩn đoán và nút kiểm tra toàn bộ cấu hình không tự gửi tin.
3. Nhật ký: DataGrid cột cố định, tìm kiếm, lọc camera, chọn sự kiện, panel chi tiết, xem ảnh và mở ảnh gốc.
4. Lỗi/onboarding: trạng thái lỗi kênh được tách khỏi giám sát; onboarding lần đầu hướng dẫn bốn bước cấu hình.
5. Hiệu năng: hồ sơ Tiết kiệm CPU/Cân bằng/Phản hồi nhanh/Ưu tiên ONVIF, chỉ số CPU/RAM/FPS preview.
6. Tray/Auto-updater: menu tray có trạng thái, mở lại, ẩn, dừng camera, kiểm tra cập nhật và thoát; updater vẫn chạy khi có bản mới.
7. Giao diện/khả năng tiếp cận: theme sáng/tối, phóng to camera, F11/Esc, Ctrl+1/2/4 và tooltip cho điều khiển chính.
8. Đóng gói: build Release, restore DataGrid, kiểm tra ZIP/checksum, phát hành public không chứa cấu hình/token.

## Startup crash hotfix — v0.8.1

Bản v0.8.0 có thêm `Avalonia.Controls.DataGrid` và tham chiếu `avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml`. Kiểm tra package NuGet `Avalonia.Controls.DataGrid` 11.2.3 cho thấy gói runtime không chứa file XAML theme theo đường dẫn này. Tham chiếu đã được loại bỏ khỏi `App.axaml` trong v0.8.1 để tránh lỗi nạp resource làm ứng dụng dừng trước khi hiển thị cửa sổ.

Bản v0.8.1 thêm `StartupDiagnostics`: ghi `AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException` và lỗi từ `Program.Main` vào `%LOCALAPPDATA%\\EZVIZ Local Monitor\\startup-crash.log`. Logger không ghi token, Chat ID, mã xác thực camera hoặc cấu hình cảnh báo.

Kiểm thử: restore và Release build thành công, 0 lỗi biên dịch; bốn cảnh báo CA1416 DPAPI Windows vẫn tồn tại như các bản trước.

## Startup crash follow-up — v0.8.2

Log thực tế từ Windows 10.0.19045 xác định exception là `System.NullReferenceException` tại `MainWindow.PerformanceProfile_Changed`, dòng 290. Sự kiện `SelectionChanged` của ComboBox hồ sơ hiệu năng được phát sinh trong lúc Avalonia đang dựng XAML, trước khi field UI/constructor hoàn tất.

Bản v0.8.2 thêm cờ `_uiInitialized`, bỏ qua các event chạy sớm và đọc ComboBox từ `sender` sau khi kiểm tra kiểu/null. Đây là hồi quy bắt buộc sau v0.8.1; không liên quan Bot Token, Chat ID, camera, RTSP hoặc DPAPI.

## Event log display fix — v0.8.3

Cảnh báo Telegram/Zalo thành công chứng minh `MonitorCoordinator.QueueDetection` đã đi qua bước `_eventStore.Add(item)`. Nguyên nhân hiển thị trống ở v0.8.1/v0.8.2 là DataGrid style Fluent bị loại bỏ để xử lý startup crash, khiến DataGrid không có template/style đầy đủ để render.

Bản v0.8.3 khôi phục `avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml` theo tài liệu DataGrid Avalonia, đồng thời thêm summary `đã tải N sự kiện` và `hiển thị M/N sự kiện`. Refresh bắt exception đọc SQLite/DataGrid, đưa lỗi lên giao diện và ghi vào startup-crash.log.

## Duplicate Telegram alert fix — v0.8.4

Ảnh người dùng cho thấy cùng một caption và ảnh sự kiện xuất hiện hai lần trong cùng một phút. `CameraMonitor` đã có cooldown cục bộ, nhưng lớp điều phối chưa có khóa chung; nếu callback ONVIF/YOLO hoặc hai monitor đến gần như đồng thời, cả hai có thể cùng được đưa vào pipeline gửi.

Bản v0.8.4 thêm `ConcurrentDictionary<Guid, DateTimeOffset>` tại `MonitorCoordinator` và thao tác chấp nhận nguyên tử theo `CameraId`/`CooldownSeconds`. Chỉ callback đầu tiên được xử lý; callback trùng bị bỏ qua và dispose snapshot. Cơ chế áp dụng chung cho ONVIF và YOLO, không thay đổi nội dung cảnh báo hoặc sự kiện mới sau khi hết cooldown.

## Safe exit while monitoring — v0.8.5

Lỗi được truy vết tại `Window_Closing`: khi `_exitRequested` là true, code cũ gọi `DisposeAsync().AsTask().GetAwaiter().GetResult()` trên UI thread. `OnvifEventListener`, `CameraMonitor` hoặc `RtspSnapshotReader` có thể đang chờ I/O/lock, làm UI bị khóa vô thời hạn.

Bản v0.8.5 chuyển `ExitFromTray` sang async, ẩn cửa sổ ngay, gọi `StopMonitoringAsync` qua `Interlocked.Exchange`, chờ tối đa 8 giây và ghi timeout vào `startup-crash.log`. `Window_Closing` không còn chờ đồng bộ; chỉ dọn bitmap UI. Nếu một capture không dừng kịp, ứng dụng vẫn không khóa giao diện và desktop lifetime được shutdown sau timeout.


## QA regression — v0.9.0

Bản v0.9.0 đã được build bằng `dotnet build src/EzvizLocalMonitor/EzvizLocalMonitor.csproj -c Release --no-restore` và publish self-contained cho `win-x64`. Build đạt **0 lỗi**; chỉ còn các cảnh báo phân tích CA1416 cho Windows DPAPI vì mã nguồn được kiểm tra trên môi trường Linux nhưng runtime mục tiêu là Windows.

Bộ kiểm thử xUnit tại `tests/EzvizLocalMonitor.Tests` đạt **5/5 test**. Các nhóm đã kiểm tra gồm EventStore thêm/đọc/cập nhật trạng thái giao hàng và phân tích AI; AlertQueueService deduplication theo EventId, retry transient result và shutdown an toàn; MonitorScheduleService khớp Weekday và khoảng giờ.

Các luồng mới được kiểm tra ở mức mã nguồn và build gồm logger phân kênh, gói chẩn đoán đã che dữ liệu, backup/restore DPAPI, lịch giám sát, Task Scheduler/watchdog, relay ảnh HTTPS có đồng thuận và queue cảnh báo. Kiểm thử thực tế RTSP/ONVIF, Telegram/Zalo và relay cần được chạy trên máy Windows của người dùng với camera và endpoint thật; không sử dụng credential thật trong môi trường build.
