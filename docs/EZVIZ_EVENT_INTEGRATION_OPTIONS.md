# Tích hợp sự kiện nhận diện của EZVIZ — kết luận kỹ thuật

## Kết luận ngắn

**Có thể dùng tính năng nhận diện tích hợp sẵn của camera**, nhưng không thể lấy sự kiện đó trực tiếp từ RTSP một cách mặc định. RTSP cung cấp luồng hình; sự kiện cần đi qua **ONVIF Events** trong LAN hoặc **EZVIZ Open Platform push** qua cloud. Với yêu cầu xử lý nội bộ LAN, phương án nên thử đầu tiên là ONVIF PullPoint subscription.

## So sánh phương án

| Phương án | Phạm vi xử lý | Ưu điểm | Ràng buộc |
|---|---|---|---|
| ONVIF Events trong LAN | Nội bộ | Không gửi video/sự kiện lên cloud; giảm CPU; có thể nhận `Motion alarm` và `Human shape detection` nếu camera expose topic | Phụ thuộc biến thể/firmware; cần xác minh topic thực tế trên C6N/H8C; ONVIF FAQ chỉ xác nhận một số firmware/model hỗ trợ ONVIF |
| EZVIZ Open Platform Push | Cloud/EZVIZ developer service | Có kênh push alarm notification và online/offline chính thức | Cần tài khoản developer/app credentials, quyền API và endpoint webhook public; không còn “hoàn toàn nội bộ LAN” |
| RTSP + YOLO cục bộ hiện tại | Nội bộ | Kiểm soát đầy đủ, không phụ thuộc event API của camera; hoạt động khi ONVIF không expose event | Dùng CPU i7-7500U; cần giữ tốc độ suy luận thấp và lọc nhiều khung |

ONVIF integration documentation mô tả PullPoint subscription cho các topic như `Motion alarm` và `Human shape detection`.[1] EZVIZ liệt kê C6N/H8c ở các biến thể firmware nhất định trong danh sách hỗ trợ ONVIF.[2] EZVIZ cũng có Developer Services mô tả push alarm notification tới developers, nhưng phần công khai chưa nêu đủ thông tin đăng ký và payload để tích hợp ngay.[3]

## Kiến trúc được khuyến nghị

Ứng dụng nên chuyển sang **hybrid trigger**. Sau khi kết nối camera, dịch vụ thử đăng nhập ONVIF bằng `admin` và mã xác thực, khám phá event topics và đăng ký PullPoint. Khi nhận `Motion alarm` hoặc `Human shape detection`, ứng dụng dùng RTSP để lấy một khung hình xác minh, lưu ảnh và gửi Telegram/Zalo. Nếu ONVIF không có event, ứng dụng tự động trở lại YOLO cục bộ hiện tại. Người dùng nhìn thấy trạng thái `Camera event`, `ONVIF event` hoặc `Local AI fallback` trong giao diện.

Phát hiện người từ camera và phát hiện chuyển động không nên coi là cùng một tín hiệu. `Motion alarm` có thể bị kích hoạt bởi vật thể/ánh sáng; `Human shape detection` gần với nhu cầu phát hiện người hơn. Quy tắc an toàn là không gửi cảnh báo chỉ vì một event đơn lẻ: giữ debounce/cooldown và xác minh khung RTSP trước khi cảnh báo.

## Điều kiện để triển khai

Cần chạy một bài kiểm tra trên chính C6N và H8C của người dùng: xác định IP, đăng nhập ONVIF bằng `admin` và mã xác thực, gọi `GetEventProperties`, thử `CreatePullPointSubscription`, sau đó bật Alarm Notification/nhận diện người trên app EZVIZ và đi qua vùng nhìn. Nếu không có topic hoặc đăng ký bị từ chối, giữ fallback YOLO; không nên dùng API cloud không chính thức.

> Với dữ liệu hiện có, câu trả lời chính xác là “có thể, nhưng phải kiểm tra ONVIF Events trên từng firmware”. Không nên hứa rằng mọi C6N/H8C sẽ truyền metadata chuyển động qua RTSP hoặc ONVIF.

## Tài liệu tham khảo

[1] [Home Assistant — ONVIF](https://www.home-assistant.io/integrations/onvif/) — mô tả ONVIF PullPoint subscription và các topic motion/human shape.

[2] [EZVIZ Support — Whether EZVIZ devices support ONVIF protocol](https://m-support.ezviz.com/faq/article/Whether-EZVIZ-devices-support-ONVIF-protocol) — danh sách model/firmware ONVIF được EZVIZ công bố.

[3] [EZVIZ Developer Services](https://www.ezviz.com/developer/index) — mô tả push alarm notification và trạng thái online/offline tới developers.

## Trạng thái triển khai

Bản cập nhật hiện đã có `OnvifEventListener`. Khi camera được thêm từ LAN discovery và có `OnvifServiceUrl`, ứng dụng thử `GetCapabilities(Events)`, tạo `CreatePullPointSubscription` rồi gọi `PullMessages` theo chu kỳ. Các topic có từ khóa motion, human/person, field detection hoặc region được chuyển thành sự kiện. Ứng dụng lấy khung RTSP xác minh, áp dụng cooldown và đưa sự kiện vào cùng pipeline SQLite/Telegram/Zalo/AI.

Nếu xác thực ONVIF, tạo subscription hoặc đọc RTSP thất bại, camera được chuyển sang `CameraMonitor` và YOLO cục bộ. Nếu PullPoint mất kết nối sau khi chạy, listener phát tín hiệu fallback và camera được bật lại bằng YOLO. Vì chưa có camera thật trong môi trường dựng, topic/firmware cụ thể của C6N và H8C vẫn phải nghiệm thu trên PC Windows cùng LAN.
