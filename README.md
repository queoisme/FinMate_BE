# FinMate

Ứng dụng theo dõi chi tiêu cá nhân, tự động ghi nhận giao dịch từ **thông báo ngân hàng/ví
điện tử** bằng AI, cộng thêm ba kênh nhập khác: chat câu tự nhiên, giọng nói, chụp hoá đơn.

Repo này chứa **phía server**: backend và các dịch vụ AI. App khách hàng làm bằng Flutter, nằm
ở repo riêng `FinMate_FE`. Web cho Staff, Manager và Admin hiện mới có API, chưa có giao diện.

---

## Chức năng

| Nhóm | Nội dung |
|---|---|
| Giao dịch | Thông báo ngân hàng, chat, giọng nói, quét hoá đơn, chuyển khoản qua mã VietQR. FinMate không tự chuyển tiền |
| Quản lý tài chính | Ngân sách và cảnh báo vượt ngưỡng, mục tiêu tiết kiệm, báo cáo, 13 phương pháp quản lý tài chính gợi ý theo khảo sát |
| Dự báo | Dòng tiền, số tiền có thể chi, khoản định kỳ, giao dịch bất thường, ngân sách tháng sau |
| Tài chính nhóm | Ngân sách, mục tiêu và báo cáo chung. Ví vẫn của riêng từng người, thành viên tự chọn khoản muốn chia sẻ. Đang tắt bằng cờ `features.groups` |
| Vận hành nội bộ | Ba vai trò `Staff`, `OpsManager`, `Admin`: ca hỗ trợ, xem dữ liệu khách khi khách đồng ý, giao dịch nháp, duyệt hai người, xác thực 2 lớp. API khách hàng đang tắt bằng cờ `features.support` |
| Giám sát | Kiểm tra dịch vụ mỗi phút, gom lỗi thành sự cố, gửi email cho Admin và OpsManager |
| Quản trị | Người dùng, cấu hình hệ thống, khoá dịch vụ bên ngoài, luật và model AI, nhật ký thao tác |

---

## Chạy toàn bộ hệ thống

Cần **Docker** và **Docker Compose**. Không cần cài .NET hay Python trên máy.

```bash
cp backend/.env.example backend/.env
cp ai-service/.env.example ai-service/.env
cp ocr-service/.env.example ocr-service/.env
cp stt-service/.env.example stt-service/.env
docker compose up -d
```

Tám service khởi động theo thứ tự phụ thuộc, migration tự chạy:

| Service | Cổng trên máy | |
|---|---|---|
| `backend` | 8080 | ASP.NET Core 9 Web API, gồm cả trang Hangfire `/hangfire` |
| `ai-service` | 8000 | Python 3.11 FastAPI |
| `ocr-service` | không mở | đọc hoá đơn, Python 3.10 FastAPI |
| `stt-service` | không mở | giọng nói thành chữ |
| `rasa` | không mở | hiểu ý định câu chat |
| `postgres-main` | 5433 | DB của backend |
| `postgres-ai` | 5434 | DB của AI service, tách hoàn toàn |
| `redis` | 6379 | cache, hạn mức, mã OTP |

Kiểm tra:

```bash
curl http://localhost:8080/health/ready     # backend, Postgres, Redis
curl http://localhost:8000/api/v1/health    # AI service
```

Tài liệu API tự sinh: **http://localhost:8080/swagger** và **http://localhost:8000/docs**

Tài khoản admin mặc định lấy từ `ADMIN_SEED_EMAIL` và `ADMIN_SEED_PASSWORD` trong `backend/.env`.

> **Sửa code rồi thì phải build lại**, `restart` không đủ, vì các service COPY mã nguồn vào
> image chứ không mount:
> ```bash
> docker compose up -d --build backend      # hoặc tên service khác
> ```

---

## Những gì chạy được khi thiếu cấu hình

Thiếu khoá hoặc model thì tính năng tự lùi về chế độ đơn giản hơn, phần còn lại vẫn chạy.

| Thiếu | Hệ quả |
|---|---|
| `BREVO_API_KEY` | mã OTP và email cảnh báo **in ra log** thay vì gửi đi: xem bằng `docker compose logs backend` |
| `FCM_CREDENTIALS_*` | thông báo đẩy chỉ ghi log |
| `GOOGLE_CLIENT_SECRET` | 3 endpoint `/auth/google/*` trả 422; đăng nhập email/mật khẩu không ảnh hưởng |
| `GOOGLE_VISION_API_KEY` | đọc hoá đơn bằng OCR cục bộ (PaddleOCR + VietOCR) |
| `GEMINI_API_KEY` | phân loại món bằng luật, món lạ thành `unknown` |
| Model trong `ai-service/rasa/models/` | chat chỉ dùng luật. Model không có trong git, xem `ai-service/rasa/README.md` |
| Model trong `stt-service/models/` | nhập bằng giọng nói không dùng được. Cách tải ở `stt-service/README.md` |

Hướng dẫn bật từng khoá nằm trong các file `.env.example` và `.context/TECH_STACK.md` §4.2.

**Nên đổi ngay cả khi chạy local:** `JWT_SECRET`, vì giá trị mẫu nằm công khai trong repo. Ngoài
môi trường `Development`, ứng dụng **từ chối khởi động** nếu còn bí mật mẫu.

**Trước khi lên production** còn phải đặt `SECRETS_MASTER_KEY`, `BREVO_API_KEY`, `OPS_WEB_ORIGINS`.
Giám sát không tự báo được khi chính backend hoặc Postgres sập, nên cần đăng ký thêm dịch vụ theo
dõi bên ngoài gọi `/health/ready`: xem [docs/ops-runbook.md](docs/ops-runbook.md).

---

## Kiểm thử

```bash
docker compose exec ai-service python -m pytest -q
cd backend && dotnet test          # hoặc chạy trong container SDK 9, xem backend/README.md
cd ocr-service && pytest tests     # stt-service cũng vậy; cả hai dùng engine giả, không cần model
```

Integration test của backend tự dựng PostgreSQL thật bằng Testcontainers. Test tích hợp của AI
service cần Postgres, không có thì bỏ qua.

---

## Kiến trúc tóm tắt

```
Flutter app ──┐                   ┌──► ai-service ──► postgres-ai
              ├──► backend (8080) ┤        └──► rasa
Web nội bộ ───┘       │           ├──► ocr-service ──► Google Vision (khi có khoá)
                      │           └──► stt-service
               postgres-main + redis
```

**backend**: ASP.NET Core 9, chia lớp `API → Application → Domain ← Infrastructure`. CQRS viết
tay, không dùng MediatR. API khách hàng ở `/api/v1/*`, quản trị ở `/api/v1/admin/*`, vận hành ở
`/api/v1/ops/*`. Job định kỳ chạy bằng Hangfire.

**ai-service**: FastAPI. Pipeline `Classifier → Extractor → Categorizer → Duplicate Detector`.
Bóc số tiền bằng **regex theo từng ngân hàng**, không bằng model, vì thông báo ngân hàng là mẫu cố
định và số tiền không được phép là phỏng đoán của model. Phân loại dùng model scikit-learn có
phiên bản, tự lùi về luật khi chưa có model nào được promote. DB tách hẳn khỏi backend: **không có
khoá ngoại nào bắc qua**, và AI DB **không bao giờ lưu `user_id` thật**, chỉ lưu `SHA-256(user_id)`.

**ocr-service**: đọc hoá đơn bằng Google Vision khi có khoá, không thì đọc cục bộ. Không kết nối
DB, đọc và ghi qua API của ai-service. Ảnh hoá đơn không được lưu lại, nhưng có khoá thì ảnh gốc
được gửi tới Google.

**stt-service**: giọng nói thành chữ bằng GIPFormer. Âm thanh chỉ đi qua bộ nhớ.

Sơ đồ thư mục đầy đủ và contract Backend ↔ AI Service: `.context/ARCHITECTURE.md` §2–3.

---

## Đọc thêm

| Tài liệu | Nội dung |
|---|---|
| [AGENTS.md](AGENTS.md) | luật bắt buộc, ranh giới phạm vi, khi nào phải hỏi người dùng |
| [docs/REQUIREMENTS.md](docs/REQUIREMENTS.md) | yêu cầu, phần đã làm và còn thiếu (§13.1), vấn đề chờ quyết định (§13.4) |
| [.context/ARCHITECTURE.md](.context/ARCHITECTURE.md) | kiến trúc, DB schema, API contract, job định kỳ |
| [.context/TECH_STACK.md](.context/TECH_STACK.md) | package được dùng / bị cấm, biến môi trường |
| [.context/CONVENTIONS.md](.context/CONVENTIONS.md) | quy ước API, lỗi, validation, logging, bảo mật |
| [.context/TASKS.md](.context/TASKS.md) | tiến độ từng phase và **ghi chép kiểm chứng** |
| [docs/group-api.md](docs/group-api.md) | API và ma trận quyền của tài chính nhóm |
| [docs/ops-api.md](docs/ops-api.md) | API và ma trận quyền của vận hành, giám sát |
| [docs/ops-runbook.md](docs/ops-runbook.md) | quy trình xử lý cho Staff, Manager, Admin |
| README của [backend](backend/README.md), [ai-service](ai-service/README.md), [ocr-service](ocr-service/README.md), [stt-service](stt-service/README.md) | chi tiết từng service |

Các đoạn **Verify** trong `TASKS.md` đáng đọc nhất: chúng ghi lại những gì đã chạy thật, và cả
những lỗi chỉ lộ ra khi chạy thật.

---

## Vài luật xuyên suốt, dễ vi phạm vì không được kiểu dữ liệu ép

- Tiền luôn là **`BIGINT` đơn vị đồng** — không `decimal`, không `float`.
- ID luôn là **`Guid`/UUID**, không `int`.
- Thời gian luôn là **`DateTimeOffset`/`TIMESTAMPTZ`**, không `DateTime` trần.
- Repository **luôn lọc theo `userId`**, và `userId` lấy từ JWT chứ không bao giờ từ body/path.
- Không bao giờ ghi log `notification_body`, `amount_cents`, mật khẩu hay token.

Danh sách đầy đủ ở `AGENTS.md` §3.

---

## Trạng thái (2026-11-06)

**Chưa có**

- Giao diện web vận hành, gồm bước nhập OTP.
- Các màn hình Flutter cho tài chính nhóm, hỗ trợ và Điều khoản bản 4. Bản 4 chưa phát hành, câu chữ
  cần người phụ trách pháp lý xem qua. `features.groups` và `features.support` vẫn tắt tới khi xong.
- Dịch vụ theo dõi bên ngoài cho `/health/ready`.

**Test đỏ đã biết**

- Backend: `MethodSelectionOnSeedDataTests`. Hồ sơ mẫu ra `rule_80_20` hay `cash_flow_budget` đang chờ
  chủ sản phẩm quyết (`REQUIREMENTS.md` §13.4).
- `ocr-service`: 5 test.

**Chưa kiểm chứng**

- Giám sát: gửi email thật qua Brevo và một job Hangfire thất bại thật. Đã chạy trên backend local
  với email ghi ra log.

Tiến độ chi tiết ở `.context/TASKS.md`. Khoảng cách so với đặc tả ở `docs/REQUIREMENTS.md` §13.1.
