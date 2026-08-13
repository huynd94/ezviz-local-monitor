# Thiết kế AI endpoint tương thích OpenAI — v0.3

## Phạm vi và nguyên tắc riêng tư

Lớp AI là **bổ sung**, không thay thế phát hiện cục bộ. RTSP, lấy mẫu khung hình, phát hiện người và xác nhận nhiều khung vẫn chạy trong LAN. Chỉ khi một sự kiện đã được xác nhận theo quy tắc cục bộ, ứng dụng mới có thể gửi tối đa hai ảnh JPEG tới endpoint AI do người dùng cấu hình: khung ngay trước sự kiện và khung sự kiện. AI đánh giá sự khác biệt giữa hai khung để phân loại có chuyển động đáng kể hay không, có người hay không, và tạo mô tả ngắn cho cảnh báo.

| Chế độ | Dữ liệu rời LAN | Hành vi |
|---|---|---|
| AI tắt — mặc định | Không | Cảnh báo hoạt động như v0.2.1, chỉ dựa trên nhận diện cục bộ. |
| AI ghi chú | Tối đa 2 ảnh/sự kiện | Vẫn gửi cảnh báo cục bộ; kết quả AI bổ sung mô tả vào thông báo. |
| AI xác nhận trước khi gửi | Tối đa 2 ảnh/sự kiện | Chỉ gửi Telegram/Zalo nếu AI trả `motion_detected=true` hoặc `person_present=true`. |

> Phân tích AI trên một ảnh tĩnh không thể khẳng định chuyển động theo thời gian. Vì vậy bản v0.3 dùng cặp ảnh trước/sau sự kiện. Nếu khung trước không sẵn sàng, AI sẽ nhận một ảnh và báo rõ mức độ tin cậy giảm.

## Cấu hình endpoint

Người dùng nhập trực tiếp trong ứng dụng: **Base URL**, **Model**, **API key**, chế độ AI và timeout. Base URL được chuẩn hóa để gọi `POST {baseUrl}/chat/completions`; ví dụ endpoint chuẩn OpenAI là `https://api.openai.com/v1`. API key được giữ trong cấu hình DPAPI gắn với tài khoản Windows, không xuất hiện trong log, thông báo hay repository.

Request dùng định dạng `messages[].content[]` gồm prompt văn bản và `image_url` dạng data URL JPEG. Ứng dụng yêu cầu JSON có các trường `motion_detected`, `person_present`, `confidence` và `summary`; nếu endpoint không hỗ trợ JSON object/vision, UI hiển thị lỗi cấu hình và không làm dừng phát hiện cục bộ.

## Quy tắc tích hợp

| Bước | Xử lý |
|---|---|
| 1 | CameraMonitor xác nhận người cục bộ, lưu ảnh trước sự kiện và ảnh sự kiện. |
| 2 | MonitorCoordinator lưu sự kiện SQLite, sau đó gọi AI nếu AI được bật. |
| 3 | Kết quả AI được ghi vào nhật ký cùng trạng thái gửi cảnh báo. |
| 4 | AlertDispatcher thêm mô tả AI vào caption; nếu bật chế độ xác nhận, kết quả AI điều khiển việc gửi. |
| 5 | Nếu endpoint lỗi/timeout, lựa chọn mặc định an toàn là vẫn gửi cảnh báo cục bộ và ghi trạng thái AI thất bại. |

Không dùng AI để nhận dạng danh tính, khuôn mặt, biển số hoặc đưa ra quyết định an ninh tự động ngoài điều kiện cảnh báo do chủ sở hữu cấu hình.
