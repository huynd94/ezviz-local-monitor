# EZVIZ Local Monitor

**Phiên bản phát hành hiện tại: v1.4.1**

## Bản vá v1.4.1 — gỡ sạch và khởi động nền cùng Windows

Bộ cài hiện tạo thêm `Uninstall-EzvizLocalMonitor-Clean.ps1` để dừng tiến trình, xóa Task Scheduler, shortcut, thư mục chương trình, dữ liệu `%LOCALAPPDATA%\\EZVIZ Local Monitor` và thư mục tạm updater. Chạy không có `-Force` sẽ yêu cầu nhập `REMOVE`; thêm `-KeepData` nếu muốn gỡ chương trình nhưng giữ cấu hình, token mã hóa, log, database và ảnh sự kiện.

Cơ chế khởi động cùng Windows được sửa để tạo lại Task Scheduler bằng tham số an toàn, chạy `--background` sau khi đăng nhập 10 giây, ở chế độ tương tác với quyền người dùng thường và ghi kết quả `schtasks` vào log. Bộ cài có thêm `Repair-EzvizLocalMonitorStartup.ps1` để tạo lại, kiểm tra hoặc chạy thử task ngay.

## Bản cập nhật v1.4.0 — quản lý camera và backup cấu hình

Nút `Xóa` trong tab Camera yêu cầu xác nhận trước khi xóa. Nếu ứng dụng đang giám sát, coordinator và các worker camera được dừng an toàn, camera được xóa khỏi danh sách và trạng thái/preview cũ được dọn khỏi bộ nhớ trước khi các camera còn lại được khởi động lại.

Nút `Sao lưu cấu hình` xuất toàn bộ `AppSettings`, gồm camera, Telegram, Zalo Bot, relay ảnh, AI, lịch giám sát, theme, bố cục, hiệu năng, startup và watchdog. Các thay đổi camera đang có trên form được ghi vào bản backup trước khi xuất. File backup dùng mã hóa Windows DPAPI, vì vậy chỉ khôi phục được bằng đúng tài khoản Windows đã tạo file.

Nút `Khôi phục cấu hình` kiểm tra header và giải mã file, chuẩn hóa dữ liệu, dừng giám sát hiện tại, thay thế toàn bộ cấu hình, dọn trạng thái/preview cũ, nạp lại giao diện và khởi động lại giám sát nếu trước đó đang chạy. File hỏng, sai định dạng hoặc không giải mã được sẽ không thay thế cấu hình hiện tại.

## Bản vá v1.3.1 — hiển thị nút hộp thoại xác nhận

Hộp thoại xác nhận dọn dữ liệu đã được sửa bằng Grid có hàng riêng cho nút thao tác. Hai nút `Hủy` và `Đồng ý dọn` được neo ở hàng cuối, có chiều cao, màu nền, foreground và hover riêng để luôn nhìn thấy trên Windows. Lỗi trước đây xảy ra vì nhóm nút chưa được gán `Grid.Row`, khiến chúng bị đặt chồng vào vùng nội dung.

## Bản cập nhật v1.3.0 — lọc và dọn nhật ký sự kiện

Tab Nhật ký sự kiện có bộ lọc thời gian gồm tất cả thời gian, 1 ngày, 2 ngày, 7 ngày gần nhất và ngày chỉ định theo dạng `yyyy-MM-dd`. Bộ lọc thời gian kết hợp được với tìm kiếm camera/nguồn/trạng thái và bộ lọc camera.

Khu vực Dọn dữ liệu cho phép giữ lại 1 ngày, 2 ngày, 1 tuần, 1 tháng hoặc xóa tất cả. Thao tác có hộp thoại xác nhận, xóa các event SQLite cũ, ảnh chính và ảnh `_before` tương ứng, đồng thời cắt phần log cũ theo timestamp. Sau khi thực hiện, giao diện hiển thị số sự kiện đã xóa, số log đã xử lý, dung lượng giải phóng và file lỗi nếu có.

Khi chọn Xóa tất cả, toàn bộ event, ảnh sự kiện và các log ứng dụng được xóa; thao tác không thể hoàn tác. File đang bị tiến trình khác sử dụng có thể được giữ lại và báo trong kết quả.

## Bản cập nhật v1.2.0 — AI trong cảnh báo và startup nền

Khi bật Phân tích AI, ứng dụng hiện chờ kết quả AI trước khi enqueue cảnh báo. `AiSummary` được ghi vào DetectionEvent và được ghép vào caption văn bản gửi Telegram/Zalo; nếu AI lỗi, cảnh báo vẫn có thể gửi trong chế độ không bắt buộc xác nhận và nội dung sẽ ghi rõ không nhận được kết quả AI. Tùy chọn `Chỉ gửi cảnh báo khi AI xác nhận` chỉ dùng để lọc cảnh báo, không còn quyết định việc có chạy AI hay không.

Panel Phân tích AI dùng cùng resource PanelBrush, ControlForegroundBrush, InputBackgroundBrush và InputForegroundBrush với các thành phần khác trong tab. Checkbox AI có style hover/pressed/focus riêng, giữ màu chữ tương phản theo theme.

Tùy chọn khởi động cùng Windows hiện tạo Task Scheduler chạy với tham số `--background`. Sau khi đăng nhập Windows, ứng dụng tự khởi động nền trong khay thông báo; giám sát và cảnh báo vẫn chạy, còn live view tạm dừng cho đến khi người dùng mở cửa sổ từ tray. Watchdog cũng giữ chế độ nền khi khởi động lại sau crash.

## Bản cập nhật v1.1.0 — tiết kiệm CPU khi chạy nền

Khi ứng dụng được ẩn vào khay thông báo, live view sẽ tạm dừng: CameraMonitor không clone frame preview, MonitorCoordinator không phát snapshot ONVIF lên UI, và worker encode không giữ frame chờ. Giám sát RTSP/ONVIF, YOLO, tracking hiện diện, ghi sự kiện và cảnh báo Telegram/Zalo vẫn tiếp tục hoạt động.

Khi người dùng mở lại cửa sổ từ khay, live view được bật lại và nhận frame mới nhất; ứng dụng không phát lại hàng loạt frame cũ. Footer hiển thị `Preview tạm dừng (tray)` khi chạy nền để người dùng dễ xác nhận trạng thái.

## Bản phát hành 1.0.0 — ổn định giao diện Tổng quan

Bản 1.0.0 sửa triệt để lỗi chồng nội dung ở footer trạng thái Tổng quan. Năm thẻ CAMERA, KẾT NỐI, CẢNH BÁO, AI và SỰ KIỆN dùng Grid hai cột cố định: label ở cột 0 bên trái và giá trị ở cột 1 bên phải. Giá trị không tự xuống dòng; nếu thiếu chiều rộng sẽ dùng dấu ba chấm, còn tooltip vẫn giữ nội dung đầy đủ. Mỗi thẻ có chiều cao tối thiểu để label và giá trị không thể vẽ chồng lên nhau.

Đợt phát hành này giữ các tối ưu live view RTSP của v0.9.21, gồm preview mới nhất, worker encode riêng và worker YOLO tách khỏi vòng đọc RTSP.

## Bản cập nhật v0.9.21 — tối ưu live view RTSP

Bản v0.9.21 tách nhịp preview khỏi nhịp YOLO: preview có thể cập nhật tối đa 5 FPS trong khi YOLO vẫn chạy theo hồ sơ 1–3 lần/giây/camera. Resize và JPEG encode được chuyển khỏi thread đọc RTSP sang worker riêng; mỗi camera chỉ giữ frame preview mới nhất và bỏ frame cũ khi UI hoặc encoder đang bận. YOLO cũng chạy trong worker riêng với hàng đợi chỉ giữ frame suy luận mới nhất, tránh làm live view đứng hình khi CPU i7-7500U đang xử lý nhận diện.

Preview được giới hạn kích thước tối đa 960 px ở cạnh dài để giảm chi phí encode và cập nhật Avalonia. Khi thoát ứng dụng, các frame Mat/Bitmap đang chờ được giải phóng an toàn.

## Bản cập nhật v0.9.20 — sửa chồng nội dung thẻ trạng thái

Bản v0.9.20 chuyển các thẻ CAMERA, KẾT NỐI, CẢNH BÁO, AI và SỰ KIỆN sang bố cục một hàng cố định: label ở bên trái, giá trị ở bên phải. Giá trị không tự xuống dòng; khi cửa sổ hẹp, nội dung dài được rút gọn bằng dấu ba chấm thay vì chồng lên label. Tooltip vẫn giữ thông tin đầy đủ cho trạng thái kỹ thuật và sự kiện gần nhất.

## Bản cập nhật v0.9.19 — sửa timeout gửi ảnh Telegram

Bản v0.9.19 tách client upload ảnh Telegram khỏi client gửi văn bản và tăng timeout upload ảnh từ 10 lên 45 giây. Ảnh được gửi bằng stream file bất đồng bộ thay vì đọc toàn bộ file vào bộ nhớ trước khi upload. Khi upload ảnh timeout hoặc lỗi, văn bản đã gửi thành công không bị gửi lại; hàng đợi chỉ retry thao tác ảnh nhờ idempotency theo EventId và thao tác. Log `alerts.log` ghi tên file, dung lượng và timeout nhưng không ghi token, Chat ID hoặc dữ liệu ảnh.

## Bản cập nhật v0.9.18 — thu gọn footer trạng thái

Bản v0.9.18 chuyển các thẻ CAMERA, KẾT NỐI, CẢNH BÁO, AI và SỰ KIỆN sang bố cục ngang nhỏ gọn. Tiêu đề thẻ giảm còn 9 px, giá trị trạng thái còn 12 px, padding còn 4 px và khoảng cách giữa các thẻ được giảm. Hàng nút Bắt đầu/Dừng cũng được thu gọn để dành thêm chiều cao cho khung hình camera. Tooltip vẫn giữ thông tin chi tiết cho sự kiện gần nhất và trạng thái kỹ thuật.

## Bản cập nhật v0.9.17 — tăng vùng hiển thị camera

Bản v0.9.17 tinh gọn trang Tổng quan để ưu tiên vùng xem camera. Header và footer được giảm padding/margin; thanh điều khiển dùng cỡ chữ và kích thước control gọn hơn; nhãn bố cục được rút gọn thành `1 ô`, `2 ô`, `4 ô`; thẻ trạng thái và thanh trạng thái camera cũng giảm padding nhưng vẫn giữ tương phản và khả năng đọc. Các nút chức năng vẫn được giữ đầy đủ và toolbar tiếp tục tự xuống dòng khi cửa sổ hẹp.

## Bản cập nhật v0.9.16 — tối ưu trang Tổng quan

Bản v0.9.16 triển khai đợt tối ưu trang Tổng quan theo hướng video là trung tâm và trạng thái dễ quét. Thanh trạng thái camera có màu chữ tương phản theo theme; preview hỗ trợ `Giữ nguyên tỷ lệ` và `Lấp đầy khung`; trạng thái hệ thống được trình bày thành các thẻ CAMERA, KẾT NỐI, CẢNH BÁO, AI và SỰ KIỆN GẦN NHẤT. Thông tin kỹ thuật đầy đủ vẫn có trong tooltip.

Thanh điều khiển được gom thành các nhóm Bố cục, Hiển thị và Hiệu năng. Mỗi tile camera có overlay tên camera cùng nút Phóng to; nút chỉ hiện với camera đã cấu hình. Dashboard có hai chế độ `Giám sát` và `Vận hành`: chế độ Giám sát ưu tiên preview lớn, còn chế độ Vận hành hiển thị thêm thông tin kỹ thuật và nút mở thư mục sự kiện. Các lựa chọn mới được lưu trong cấu hình cục bộ.

## Bản vá v0.9.15 — rà soát chống tràn giao diện

Bản v0.9.15 rà soát các cửa sổ và panel có nút chức năng. Thanh công cụ Tổng quan, bộ lọc Nhật ký sự kiện, nhóm nút Lịch giám sát và nhóm nút chẩn đoán trong tab Cảnh báo đã chuyển sang bố cục tự xuống dòng để không làm mất nút khi cửa sổ hẹp. Cửa sổ chính giữ ngưỡng kích thước tối thiểu an toàn; các tab Camera, Cảnh báo, Hướng dẫn và Chi tiết sự kiện tiếp tục có vùng cuộn phù hợp.

Updater PowerShell được bổ sung DPI scaling, tự cuộn, cho phép phóng to và neo các nút ở cạnh dưới/phải. Onboarding đã có ScrollViewer và hộp thoại cập nhật Avalonia đã được gia cố từ v0.9.14 bằng hàng nút riêng cùng màu tương phản rõ ràng.

## Bản vá v0.9.14 — hiển thị đầy đủ hộp thoại cập nhật

Bản v0.9.14 sửa hộp thoại phát hiện phiên bản mới trong ứng dụng chính. Cửa sổ được tăng không gian hiển thị, tách khu vực nút thành hàng riêng và đặt màu nền, màu chữ, viền cùng trạng thái hover rõ ràng cho ba lựa chọn `Cập nhật ngay`, `Để sau` và `Mở trang release`. Các nút không còn bị khuất hoặc bị chìm trên nền trắng do style Fluent/theme dùng chung.

## Bản vá v0.9.13 — không lặp nội dung caption Telegram/Zalo

Bản v0.9.13 sửa lỗi cùng một nội dung cảnh báo xuất hiện hai lần trong tin nhắn Telegram khi ứng dụng gửi văn bản trước rồi gửi ảnh sau. Sau khi thao tác gửi văn bản của cùng `EventId` thành công, thao tác gửi ảnh không gửi lại caption; nếu văn bản thất bại, ảnh vẫn giữ caption để không mất nội dung cảnh báo. Chính sách này được áp dụng đồng nhất cho Telegram và Zalo, đồng thời bổ sung test hồi quy.

## Bản cập nhật v0.9.12 — làm rõ updater và panel Phân tích AI

Bản v0.9.12 làm nổi bật ba nút `Kiểm tra bản mới`, `Cập nhật ngay` và `Đóng` trong cửa sổ updater bằng màu nền, chữ trắng, viền, hover và trạng thái disabled riêng. Panel Phân tích AI dùng foreground nâu đậm cố định trên nền cảnh báo sáng, nên không bị theme tối ghi đè làm chữ biến mất.


## Bản cập nhật v0.9.11 — chống gửi lặp Telegram/Zalo

Bản v0.9.11 sửa lỗi một sự kiện chỉ có một dòng trong nhật ký nhưng bị gửi lặp qua Telegram hoặc Zalo. Hàng đợi vẫn retry lỗi mạng, nhưng AlertDispatcher ghi nhận thành công theo `EventId` và từng thao tác Telegram text/photo, Zalo text/photo. Khi retry, chỉ thao tác thất bại được thử lại; thao tác đã thành công không tạo tin nhắn trùng.

Các trạng thái hợp lệ như Zalo gửi được văn bản nhưng không gửi ảnh local vì chưa bật relay HTTPS không còn bị coi là lỗi để retry toàn bộ sự kiện. Bộ test hồi quy bao gồm dedup khi đang chờ, dedup sau khi hoàn tất, kết quả gộp Telegram/Zalo, retry lỗi tạm thời và shutdown an toàn.


## Bản cập nhật v0.9.10 — sửa bố cục tab Camera

Bản v0.9.10 bọc panel cấu hình Camera bằng vùng cuộn dọc. Khi cửa sổ toàn màn hình có chiều cao thấp hoặc Windows dùng scaling lớn, các nút `Lưu camera` và `Kiểm tra RTSP` vẫn có thể truy cập bằng cách cuộn thay vì bị nằm ngoài viewport. Khu vực danh sách camera và các nút `Thêm`/`Xóa` tiếp tục co giãn theo cửa sổ.


## Bản cập nhật v0.9.9 — dọn thư mục updater legacy

Bản v0.9.9 bổ sung dọn dẹp tự động các thư mục tạm legacy có tên `EZVIZ-AutoUpdater-*` còn sót từ các phiên bản updater cũ. Updater chỉ xóa thư mục đúng tiền tố ở cấp trực tiếp của thư mục Temp; không quét hoặc xóa dữ liệu ở vị trí khác. Cơ chế dọn `EZVIZ-GUI-*` và `EZVIZ-Update-*` hiện tại vẫn được giữ nguyên.


## Bản cập nhật v0.9.8 — sửa chữ bị chìm trên các tab

Bản v0.9.8 sửa lớp màu nền panel theo từng theme: Orchid, Ocean, Lavender và Crimson dùng panel sáng với chữ tối; Midnight và Dark dùng panel tối với chữ sáng. Heading, muted text, nội dung hướng dẫn, footer, DataGrid, tiêu đề cột và ô dữ liệu được áp dụng foreground tương phản riêng, không dùng màu mặc định dễ bị Fluent ghi đè.


## Bản cập nhật v0.9.7 — button và input tương phản cao

Bản v0.9.7 tách riêng màu nền, màu chữ, màu viền, hover và pressed cho button; tách riêng nền/chữ/viền/focus cho input. Button được ép hiển thị chữ trắng trên nền button màu chính, in đậm và không dùng opacity mờ. TextBox và ComboBox có nền, chữ, viền và viền focus rõ ràng theo từng theme. Dark/Light tự đổi giữa bộ màu sáng và tối tương ứng.


## Bản cập nhật v0.9.6 — tăng độ rõ chữ và tiêu đề tab

Bản v0.9.6 chuẩn hóa màu foreground cho cả sáu theme, tách riêng màu chữ nội dung, chữ control và chữ tiêu đề tab, đồng thời tăng độ tương phản trên panel sáng/tối. Các tiêu đề tab được in đậm và tăng kích thước để dễ nhận biết. Màu chữ trạng thái, label, button, TextBox, ComboBox và CheckBox được áp dụng theo palette tương ứng thay vì dùng màu mặc định có thể bị chìm.


## Bản cập nhật v0.9.5 — bộ chọn theme

Bản v0.9.5 thêm bộ chọn giao diện gồm **Orchid**, **Ocean**, **Midnight**, **Lavender**, **Crimson** và **Dark/Light** trong thanh Tổng quan. Mỗi theme có palette riêng cho nền ứng dụng, header, panel, tiêu đề, chữ phụ, thông tin và cảnh báo; lựa chọn được lưu trong cấu hình DPAPI và tự áp dụng sau khi mở lại hoặc khôi phục backup.

Nút **Sáng/Tối** cũ vẫn hoạt động như chuyển nhanh trong theme Dark/Light. Các theme Midnight dùng Fluent Dark, các theme còn lại dùng Fluent Light; màu trạng thái cảnh báo được giữ tương phản để dễ theo dõi.


## Bản cập nhật v0.9.4 — tab Hướng dẫn & Tác giả

Bản v0.9.4 thêm tab **Hướng dẫn & Tác giả** trong ứng dụng. Tab giải thích cách bắt đầu nhanh, thêm camera, giảm cảnh báo giả, cấu hình Telegram/Zalo/AI, sử dụng lịch, backup/restore, xuất gói chẩn đoán và xử lý các lỗi thường gặp.

Thông tin tác giả được hiển thị trực tiếp trong ứng dụng: **Nguyen Duc Huy**, website [huynd.io.vn](https://huynd.io.vn) và email [huynd130994@gmail.com](mailto:huynd130994@gmail.com). Website và email là các nút có thể bấm để mở trình duyệt hoặc ứng dụng email mặc định.


## Bản cập nhật v0.9.3 — cải thiện giao diện nhập liệu

Bản v0.9.3 điều chỉnh các cột label/input trong tab Camera và Cảnh báo để label có đủ không gian, TextBox co giãn theo cửa sổ và các trường dài như RTSP URL, Relay URL, Base URL không bị cắt sớm. Các nút, trường nhập liệu, checkbox và slider quan trọng đều có tooltip tiếng Việt giải thích dữ liệu cần điền, phạm vi giá trị, tác động đến cảnh báo và lưu ý bảo mật.


## Bản vá v0.9.2 — sửa installer trả mã lỗi 1

Bản v0.9.2 sửa lỗi updater kết thúc với thông báo chung `Bộ cài trả mã lỗi 1`. Trong cấu trúc ZIP, script cài đặt nằm trong thư mục `installer`, còn executable nằm trong thư mục `app` ở cấp gốc; installer cũ tìm nhầm `installer\\app` nên thất bại. Installer mới tìm đúng thư mục gốc của gói, còn worker updater ghi lại stdout/stderr của installer để hiển thị nguyên nhân cụ thể nếu lỗi tiếp diễn.


## Bản vá v0.9.1 — updater Windows PowerShell 5.1

Bản v0.9.1 giữ nguyên 10 nhóm tính năng của v0.9.0 và sửa lỗi updater GUI/worker bị `ParserError` trên Windows PowerShell 5.1. Hai file `.ps1` updater được lưu lại dưới dạng **UTF-8 có BOM**, để PowerShell 5.1 nhận diện đúng tiếng Việt thay vì đọc theo ANSI và biến chuỗi như `Cập nhật` thành ký tự lỗi.


## 10 nhóm cải tiến v0.9.0

Bản v0.9.0 hoàn thiện các nhóm vận hành đã thống nhất. Trung tâm Tổng quan hiển thị sức khỏe từng camera, kênh cảnh báo, sự kiện gần nhất và chỉ số runtime. CameraMonitor có state machine RTSP/ONVIF, reconnect counter, tracking thời gian xuất hiện tối thiểu và vùng ROI; cảnh báo chỉ được tạo sau khi đạt đủ xác nhận và thời gian hiện diện.

Cảnh báo được đưa qua hàng đợi nền giới hạn 128 mục, idempotency theo Event ID và tối đa ba lần retry với backoff. Nhật ký được tách thành `app.log`, `camera.log`, `alerts.log` và `ai.log`; nút **Xuất gói chẩn đoán** tạo ZIP gồm log cùng metadata đã che token, API key, Chat ID, IP LAN và mã xác thực.

Backup cấu hình dùng Windows DPAPI và chỉ giải mã được trong đúng tài khoản Windows. Tab **Lịch giám sát** hỗ trợ ngày trong tuần, Weekday/Weekend, giờ qua nửa đêm và hồ sơ hiệu năng. **Khởi động cùng Windows** dùng Task Scheduler thay vì Registry; watchdog chỉ khởi động lại sau thoát bất thường và được tắt khi người dùng chọn thoát hoàn toàn.

Zalo vẫn chỉ nhận `photo` là URL HTTPS. Relay ảnh tùy chọn dùng endpoint HTTPS tùy chỉnh, chỉ hoạt động khi người dùng bật relay và đánh dấu đồng ý ảnh rời LAN; nếu không, ứng dụng gửi văn bản Zalo và ghi rõ ảnh local chưa thể gửi. Bộ test xUnit bao phủ EventStore CRUD, queue dedup/retry/shutdown và lịch hoạt động.



Bản 0.8.5 có icon ứng dụng riêng, Auto-updater kiểm tra bản mới khi mở ứng dụng, updater GUI đã sửa lỗi Runspace và trang **Tổng quan** với bố cục linh động **1, 2 hoặc 4 màn hình**. Chọn bố cục ở hàng nút phía trên lưới camera; lựa chọn được lưu trong cấu hình Windows và giữ lại ở lần mở sau. Bố cục 1 màn hình hiển thị camera đầu tiên, bố cục 2 hiển thị hai camera đầu tiên, còn bố cục 4 hiển thị tối đa bốn camera; các ô chưa cấu hình vẫn hiện trạng thái chờ.

**Phiên bản:** 0.9.1
**Mục đích:** Giám sát cục bộ tối đa bốn camera EZVIZ, phát hiện **người** tại máy Windows và gửi ảnh kèm văn bản qua Telegram cùng Zalo Bot Platform. Với i7-7500U/RAM 8 GB, nên bắt đầu với hai camera rồi đo CPU trước khi bật bốn luồng YOLO đồng thời.

## 8 nhóm cải tiến giao diện và vận hành

Bản 0.8.5 triển khai đầy đủ các nhóm cải tiến đã thống nhất. Tổng quan có trạng thái từng camera, số camera hoạt động, cảnh báo kết nối, kênh Telegram/Zalo và chỉ số CPU/RAM/FPS preview. Tab Cảnh báo được chia nhóm và có nút kiểm tra toàn bộ cấu hình mà không tự gửi tin. Nhật ký sự kiện dùng bảng cột cố định, tìm kiếm, lọc theo camera, chọn dòng, xem ảnh và mở ảnh gốc.

Onboarding lần đầu hướng dẫn tìm camera LAN, nhập mã xác thực, kiểm tra RTSP/ONVIF và cấu hình cảnh báo. Hồ sơ hiệu năng gồm Tiết kiệm CPU, Cân bằng, Phản hồi nhanh và Ưu tiên ONVIF. Tray có trạng thái động, dừng camera, kiểm tra cập nhật và thoát hoàn toàn. Người dùng có thể chuyển sáng/tối, phóng to camera, dùng F11/Esc và Ctrl+1/Ctrl+2/Ctrl+4.

Các trạng thái lỗi được tách khỏi trạng thái giám sát; lỗi một kênh thông báo không làm dừng camera. Các chỉ số CPU/RAM/FPS chỉ mang tính quan sát runtime và giúp chọn hồ sơ phù hợp với máy i7-7500U/RAM 8 GB.

## Xử lý lỗi ứng dụng tự đóng khi mở

Bản v0.8.5 đã loại bỏ tham chiếu resource Fluent của DataGrid không có trong gói runtime v11.2.3, là điểm có thể làm Avalonia dừng ngay trong lúc nạp giao diện. Bản này cũng ghi lỗi khởi động vào file:

```text
%LOCALAPPDATA%\EZVIZ Local Monitor\startup-crash.log
```

Nếu ứng dụng vẫn tự đóng, hãy mở file trên bằng Notepad và gửi phần thông báo lỗi, sau khi kiểm tra không có Bot Token, Chat ID, mã xác thực camera hoặc thông tin riêng tư. Bản v0.8.3 sửa thêm lỗi `NullReferenceException` trong `PerformanceProfile_Changed`: ComboBox hồ sơ hiệu năng có thể phát sinh `SelectionChanged` trong lúc Avalonia chưa hoàn tất khởi tạo field UI. Handler hiện bỏ qua sự kiện sớm và lấy ComboBox từ `sender` an toàn. Bản vá không xóa cấu hình DPAPI hiện có.

## Nhật ký sự kiện v0.8.3

Bản v0.8.5 khôi phục Fluent style bắt buộc của Avalonia DataGrid để bảng có thể render đúng sau hotfix startup. Tab **Nhật ký sự kiện** hiển thị số lượng bản ghi đã tải và số dòng sau lọc. Nếu SQLite không đọc được, giao diện hiển thị lỗi đọc nhật ký và ghi exception vào `startup-crash.log` thay vì im lặng để bảng trống.

## Chống gửi trùng cảnh báo — v0.8.4

Bản v0.8.4 thêm lớp chống trùng ở `MonitorCoordinator`, dùng chung cho sự kiện ONVIF và phát hiện YOLO. Mỗi camera chỉ được chấp nhận một cảnh báo trong khoảng **Khoảng im lặng (giây)** đã cấu hình. Nếu hai callback đến gần như đồng thời, chỉ callback đầu tiên được gửi; callback còn lại được giải phóng ảnh và ghi trạng thái bỏ qua, không làm dừng giám sát.

Cơ chế này không thay đổi nội dung cảnh báo và không chặn sự kiện mới sau khi hết khoảng im lặng. Muốn nhận cảnh báo thường xuyên hơn, giảm giá trị Khoảng im lặng trong tab Camera; không nên đặt bằng 0 vì hệ thống luôn giữ tối thiểu một giây để tránh lặp do nhiều callback cùng một frame.

## Thoát hoàn toàn khi đang giám sát — v0.8.5

Bản v0.8.5 sửa lỗi ứng dụng bị đứng khi chọn **Thoát hoàn toàn** trong lúc camera đang chạy. Trước đây `Window_Closing` chờ `DisposeAsync` bằng `GetAwaiter().GetResult()` trên UI thread; nếu RTSP/ONVIF đang chờ I/O, cửa sổ bị khóa và phải kết thúc bằng Task Manager. Bản mới dừng coordinator bằng async, chờ tối đa 8 giây, ghi timeout vào `startup-crash.log` và chỉ sau đó mới shutdown desktop lifetime.

Đóng cửa sổ bằng nút X vẫn có nghĩa là **ẩn vào khay**. Muốn kết thúc hoàn toàn, dùng menu tray **Thoát hoàn toàn**. Trong lúc dừng, cửa sổ được ẩn và không khóa thao tác hệ thống.

## Script cập nhật nhanh từ GitHub

Gói hiện tại có updater dạng cửa sổ tại `scripts\Update-EzvizLocalMonitor.ps1` và file chạy nhanh `scripts\Update-EzvizLocalMonitor.cmd`. Double-click file `.cmd` để mở giao diện. Cửa sổ hiển thị phiên bản hiện tại, phiên bản mới nhất, trạng thái kết nối GitHub, tiến trình tải, xác minh SHA-256, giải nén và cài đặt.

Mã nguồn vẫn nằm trong repository private. Các gói phát hành được phân phối qua repository public `huyavm/ezviz-local-monitor-releases`, vì vậy updater mặc định không cần GitHub token. Trường Repository chỉ cần thay đổi nếu bạn có kênh phát hành riêng.

Khi mở ứng dụng, Auto-updater sẽ kiểm tra release public ở chế độ nền với thời gian chờ ngắn để không làm chậm giao diện. Nếu có bản mới, ứng dụng hiển thị lựa chọn **Cập nhật ngay**, **Để sau** hoặc **Mở trang release**. Chọn cập nhật sẽ mở updater GUI riêng, dừng giám sát an toàn, tải gói, xác minh SHA-256 và tự mở lại ứng dụng sau khi hoàn tất. Nếu không có mạng hoặc GitHub tạm thời không phản hồi, ứng dụng vẫn mở và giám sát bình thường.

Trong updater GUI, bấm **Kiểm tra bản mới** để chỉ kiểm tra. Khi có bản mới, bấm **Cập nhật ngay**, xác nhận hộp thoại, rồi chờ đến trạng thái **Cập nhật thành công**. Có thể bỏ chọn **Mở ứng dụng sau khi cập nhật** nếu cần.

Có thể chạy bằng PowerShell nếu cần:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Update-EzvizLocalMonitor.ps1
```

## Gửi cảnh báo và log chẩn đoán Zalo

Theo [tài liệu sendPhoto của Zalo Bot](https://bot.zapps.me/docs/apis/sendPhoto/), trường `photo` có kiểu **String** và là đường dẫn ảnh; API không nhận trực tiếp đường dẫn file local trong máy. Vì ứng dụng hoạt động trong LAN, ảnh sự kiện hiện được lưu local và không có URL HTTPS công khai để máy chủ Zalo truy cập. Bản v0.7.0 không tiếp tục gửi multipart file local khiến Zalo trả `chat_id/photo must not be empty`; ứng dụng gửi **văn bản Zalo trước** và ghi rõ lý do ảnh chưa thể gửi. Telegram vẫn gửi văn bản và ảnh theo cơ chế riêng.

Ứng dụng ghi log riêng tại `%LOCALAPPDATA%\EZVIZ Local Monitor\zalo-send.log`. Trong tab **Cảnh báo**, bấm **Mở log Zalo** để mở file này. Log gồm thời điểm, thao tác `sendMessage`/`sendPhoto`, HTTP status, độ dài Chat ID, trạng thái tồn tại/kích thước ảnh, việc `photo` có phải URL HTTPS hay không và response lỗi đã rút gọn. Bot Token và Chat ID chỉ được che một phần, không ghi đầy đủ vào log; log tự xoay khi vượt quá 2 MB. Ghi log chạy ở worker nền, tối đa 128 bản ghi chờ; nếu hệ thống bận, bản ghi phụ có thể bị bỏ qua để ưu tiên camera và giao diện.

Mỗi thao tác Zalo có timeout riêng tối đa 12 giây và liên kết với token hủy của ứng dụng. Lỗi timeout, hủy, DNS, HTTP, JSON, file ảnh hoặc lỗi ghi log đều được cô lập và trả về trạng thái; không được phép làm dừng giám sát hoặc kết thúc tiến trình.

Khi báo lỗi, hãy tạo lại một sự kiện mới, mở log và gửi các dòng liên quan đến `sendMessage`/`sendPhoto` sau khi đã kiểm tra không có dữ liệu nhạy cảm. Không gửi Bot Token, mã xác thực camera hoặc toàn bộ cấu hình ứng dụng.

## Khay thông báo và chạy nền

Khi bấm nút đóng cửa sổ, ứng dụng sẽ **ẩn vào khay thông báo** thay vì thoát. Các luồng camera, nhận diện người và cảnh báo vẫn tiếp tục hoạt động. Bấm biểu tượng EZVIZ Local Monitor trong khay để mở lại cửa sổ; chọn **Thoát hoàn toàn** trong menu khay nếu muốn dừng giám sát và kết thúc ứng dụng.

Bản 0.8.5 cũng giữ các tối ưu giảm giật lag lúc mở ứng dụng bằng cách mở trước RTSP ở nền, giới hạn buffer khung hình để ưu tiên dữ liệu mới và gộp các preview đang chờ để không làm nghẽn hàng đợi giao diện.

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

Ví dụ minh họa dùng placeholder, không phải thông tin camera thật:

```text
rtsp://admin:<VERIFICATION_CODE>@<CAMERA_IP>:554/ch1/main
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
