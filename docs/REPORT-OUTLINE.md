# Đề cương Quyển Báo cáo Bài tập lớn - Đề tài 12

**Học phần:** Phát triển ứng dụng phân tán  
**Đề tài:** Hệ thống phê duyệt tài liệu phân tán  
**Công nghệ:** C#, .NET 10, ASP.NET Core Minimal API, PostgreSQL, RabbitMQ, SignalR, Docker Compose

---

## Chương 1. Giới thiệu bài toán và phân tích yêu cầu
1.1. Đặt vấn đề và mục tiêu hệ thống phê duyệt tài liệu phân tán.  
1.2. Yêu cầu chức năng (8 chức năng cốt lõi + vòng đời trạng thái `Draft -> Submitted -> Approved / Rejected`).  
1.3. Yêu cầu phi chức năng (độc lập dịch vụ, cấu hình hóa môi trường, structured logging, khả năng chịu lỗi và phục hồi).

## Chương 2. Kiến trúc hệ thống và các cơ chế giao tiếp phân tán
2.1. Kiến trúc tổng thể (chèn hình render từ `docs/architecture.puml`).  
2.2. Chiến lược Database-per-Service (`document_db` và `notification_db`).  
2.3. Giao tiếp đồng bộ (RESTful Minimal API giữa `web-client` và `document-api` kèm `X-Correlation-Id`).  
2.4. Giao tiếp bất đồng bộ hướng sự kiện qua RabbitMQ (Topic Exchange `doc.events.exchange`, Durable Queue, Dead-Letter Exchange/Queue).  
2.5. Thông báo thời gian thực qua SignalR WebSocket Hub (`/hubs/notifications`).

## Chương 3. Thiết kế chi tiết các thành phần
3.1. `Shared.Contracts` (Enum trạng thái, Event DTOs, hằng số Messaging).  
3.2. `Document.Api` (CSDL, máy trạng thái, đặc tả 6 API tối thiểu + Outbox Worker).  
3.3. `Notification.Service` (Idempotent Consumer, retry + DLQ, SignalR Hub).  
3.4. `Approval.WebClient` (BFF Proxy qua Docker network, giao diện web, giám sát Health Check).  
3.5. Sơ đồ tuần tự luồng phê duyệt (chèn hình render từ `docs/sequence-approve.puml`).

## Chương 4. Triển khai, kiểm thử và đánh giá hệ thống
4.1. Đóng gói đa giai đoạn (Multi-stage Dockerfile) và điều phối bằng `docker-compose.yml`.  
4.2. Kết quả chạy thực nghiệm các luồng nghiệp vụ chính (chụp ảnh giao diện tạo, gửi, duyệt, từ chối và lịch sử).  
4.3. Kịch bản kiểm thử khả năng chịu lỗi và phục hồi:
- Tắt `notification-service`, thao tác trên `document-api`, bật lại kiểm tra nhận bù thông báo.
- Tắt `rabbitmq`, thao tác duyệt tài liệu, kiểm tra bảng `outbox_messages`, bật lại kiểm tra tự động phát bù.
- Kiểm thử message lỗi rơi vào `notification.doc.dlq` trên RabbitMQ Management UI.
4.4. Đánh giá ưu điểm, hạn chế và hướng phát triển (JWT/OAuth2, phân quyền đa cấp duyệt, Audit Service).
