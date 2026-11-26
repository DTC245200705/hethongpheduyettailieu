# Đề tài 12 - Hệ thống Phê duyệt Tài liệu Phân tán (.NET 10)

Hệ thống quản lý quy trình tạo, gửi, phê duyệt và từ chối tài liệu theo kiến trúc phân tán hướng sự kiện, xây dựng bằng **C# / .NET 10 Minimal API**, **PostgreSQL**, **RabbitMQ**, **SignalR** và **Docker Compose**.

## 1. Kiến trúc hệ thống

```text
Web Client (:5000) -> Document API (:5001) -> PostgreSQL (document_db + Outbox)
                                                   |
                                                   v
                                          RabbitMQ (Topic Exchange + DLQ)
                                                   |
                                                   v
Web Client (SignalR) <--- Notification Service (:5002) -> PostgreSQL (notification_db + Idempotency)
```

### Các thành phần chính
- **`Document.Api` (Port `5001`):** Minimal API quản lý tài liệu, máy trạng thái (`Draft`, `Submitted`, `Approved`, `Rejected`), lịch sử xử lý và **Transactional Outbox Worker** phát sự kiện sang RabbitMQ.
- **`Notification.Service` (Port `5002`):** Consumer lắng nghe RabbitMQ với cơ chế **Idempotent Consumer**, retry + **Dead-Letter Queue (`notification.doc.dlq`)**, lưu lịch sử thông báo và đẩy thông báo real-time về client qua **SignalR Hub** (`/hubs/notifications`).
- **`Approval.WebClient` (Port `5000`):** Ứng dụng Web App + BFF Proxy giao tiếp với các backend qua Docker network bằng service name (`document-api`, `notification-service`) và nhận thông báo tức thì qua SignalR.
- **`PostgreSQL` (Port `5432`):** Chứa 2 database tách biệt `document_db` và `notification_db`.
- **`RabbitMQ` (Ports `5672`, `15672`):** Message Broker trung tâm kèm Management UI.

### Các yêu cầu mở rộng đã tích hợp
- **Transactional Outbox Pattern** chống mất sự kiện khi RabbitMQ tạm dừng.
- **Retry & Dead-Letter Queue (DLQ)** bảo vệ consumer khỏi poison messages.
- **Idempotent Consumer** (`processed_events`) chống gửi đúp thông báo.
- **Correlation ID (`X-Correlation-Id`)** truy vết xuyên suốt từ HTTP → Outbox → RabbitMQ → Notification.
- **Structured Logging (Serilog JSON)** và **Health Checks (`/health`)** cho tất cả service.

---

## 2. Hướng dẫn cài đặt và chạy hệ thống

### Yêu cầu môi trường
- Docker Desktop (hoặc Docker Engine + Docker Compose v2).
- (Tùy chọn nếu chạy local không qua Docker) .NET 10 SDK.

### Khởi chạy toàn bộ hệ thống bằng 1 lệnh

```bash
# (Tùy chọn) Tạo file .env từ mẫu để tùy chỉnh port/mật khẩu
cp .env.example .env

# Build và khởi chạy toàn bộ 5 container
docker compose up --build -d

# Kiểm tra trạng thái healthy của các container
docker compose ps
```

### Các địa chỉ truy cập sau khi chạy
| Thành phần | URL | Ghi chú |
| :--- | :--- | :--- |
| **Web Client** | http://localhost:5000 | Giao diện thao tác & thông báo real-time |
| **Document API (Swagger)** | http://localhost:5001/swagger | Test trực tiếp 6 REST API chính |
| **Notification Service (Swagger)** | http://localhost:5002/swagger | Xem thông báo & Health Check |
| **RabbitMQ Management** | http://localhost:15672 | Tài khoản mặc định: `docmq` / `docmq_secret_change_me` |

---

## 3. Danh sách API chính (`Document.Api`)

| Method | Endpoint | Mô tả |
| :--- | :--- | :--- |
| `POST` | `/documents` | Tạo tài liệu mới ở trạng thái `Draft` |
| `GET` | `/documents` | Tra cứu theo `?status=&approver=&createdBy=&search=` |
| `GET` | `/documents/{id}` | Chi tiết tài liệu kèm toàn bộ lịch sử xử lý |
| `POST` | `/documents/{id}/submit` | Gửi tài liệu vào quy trình phê duyệt (`Draft`/`Rejected` → `Submitted`) |
| `POST` | `/documents/{id}/approve` | Phê duyệt tài liệu (`Submitted` → `Approved`) |
| `POST` | `/documents/{id}/reject` | Từ chối tài liệu kèm lý do (`Submitted` → `Rejected`) |
| `GET` | `/health` | Kiểm tra tình trạng DB & RabbitMQ |
| `GET` | `/outbox/stats` | Xem số lượng sự kiện chờ gửi / đã gửi trong Outbox |

---

## 4. Kịch bản kiểm thử khả năng chịu lỗi (Demo khi vấn đáp)

### Kịch bản A: `notification-service` tạm dừng
1. Tắt service thông báo:
   ```bash
   docker compose stop notification-service
   ```
2. Vào http://localhost:5000 (hoặc Swagger), tạo tài liệu và bấm **Submit** → **Approve**. Hệ thống vẫn xử lý và lưu DB thành công.
3. Mở RabbitMQ Management (http://localhost:15672) → tab **Queues** → thấy message đang nằm chờ an toàn trong `notification.doc.queue`.
4. Bật lại service:
   ```bash
   docker compose start notification-service
   ```
5. `notification-service` tự động kết nối lại, tiêu thụ hết hàng đợi và đẩy thông báo real-time về Web Client.

### Kịch bản B: `rabbitmq` tạm dừng (Outbox Pattern)
1. Tắt RabbitMQ:
   ```bash
   docker compose stop rabbitmq
   ```
2. Thao tác **Submit** hoặc **Approve** tài liệu trên Web Client → `document-api` vẫn trả `200 OK`, không sập.
3. Gọi `GET http://localhost:5001/outbox/stats` (hoặc bấm nút **Trạng thái hệ thống** trên Web) → thấy `pending > 0`.
4. Bật lại RabbitMQ:
   ```bash
   docker compose start rabbitmq
   ```
5. Trong vòng vài giây, `OutboxPublisherWorker` tự động phát bù toàn bộ sự kiện (`pending` về `0`) và Web Client nhận được thông báo.

### Kịch bản C: Kiểm tra Logs có Correlation ID
```bash
docker compose logs -f document-api notification-service
```

---

## 5. Đẩy mã nguồn lên GitHub

Repository đã có sẵn lịch sử 14 commit chuẩn Conventional Commits:
```bash
git log --oneline
git remote add origin https://github.com/<your-username>/distributed-doc-approval.git
git push -u origin main
```
