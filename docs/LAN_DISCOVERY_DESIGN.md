# Thiết kế tự tìm camera LAN — v0.2

## Mục tiêu trải nghiệm

Người dùng mở tab **Camera**, bấm **Tìm camera trong LAN**, chọn một camera trong danh sách và nhập mã xác thực trên nhãn thiết bị. Ứng dụng tạo RTSP URL theo mẫu EZVIZ, thử đọc một khung hình rồi chỉ thêm camera khi xác thực thành công. Mã không được hiển thị lại và được bảo vệ cùng cấu hình ứng dụng bằng Windows DPAPI.

## Chiến lược khám phá theo thứ tự

| Bước | Cách làm | Kết quả |
|---|---|---|
| 1 | Gửi WS-Discovery Probe multicast trong LAN và đọc phản hồi ONVIF. | Tìm được camera hỗ trợ ONVIF, gồm URL ONVIF khi thiết bị trả lời. |
| 2 | Nếu không đủ kết quả, quét có giới hạn chính subnet `/24` của các giao diện IPv4 riêng, chỉ thử TCP 554. | Tìm ứng viên có RTSP đang mở. Không quét ra Internet hay các subnet lớn. |
| 3 | Người dùng chọn ứng viên và nhập mã xác thực. | Ứng dụng tạo `rtsp://admin:<code>@<ip>:554/ch1/main` và đọc thử khung hình. |
| 4 | Thành công thì lưu camera; thất bại thì báo lý do và không lưu. | Không có camera được thêm bằng suy đoán hay lưu mã sai. |

FAQ LAN Live View của EZVIZ mô tả cùng trải nghiệm: thiết bị và điện thoại cùng LAN, quét camera, username mặc định là `admin`, và device verification code trên nhãn làm mật khẩu. [1] FAQ ONVIF cũng liệt kê C6N/H8c có thể hỗ trợ ONVIF theo đúng biến thể và firmware, vì vậy bước WS-Discovery là ưu tiên nhưng không bắt buộc. [2]

> Một camera không xuất hiện trong danh sách không có nghĩa camera đó không hoạt động. ONVIF/RTSP có thể bị tắt trên firmware hoặc camera có thể ở một VLAN khác. Ứng dụng sẽ giải thích rõ và vẫn giữ lựa chọn nhập RTSP thủ công như phương án dự phòng.

## Riêng tư và an toàn

Quét chỉ được thực hiện khi người dùng bấm nút, chỉ trong mạng cục bộ đang kết nối. Ứng dụng không gửi IP, mã xác thực hay kết quả quét ra Internet. Trong danh sách chỉ hiển thị IP, cổng, nguồn phát hiện và URL ONVIF (nếu có); camera phải được xác thực bằng mã riêng trước khi tạo luồng RTSP.

## Tài liệu tham khảo

[1] [EZVIZ Support — How to use LAN Live View](https://m-support.ezviz.com/faq/article/How-to-use-LAN-Live-View)

[2] [EZVIZ Support — Whether EZVIZ devices support ONVIF protocol](https://m-support.ezviz.com/faq/article/Whether-EZVIZ-devices-support-ONVIF-protocol)
