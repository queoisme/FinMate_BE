# FinMate

Ứng dụng theo dõi chi tiêu cá nhân, tự động ghi nhận giao dịch từ **thông báo ngân hàng/ví
điện tử** bằng AI, cộng thêm ba kênh nhập tay (gõ câu tự nhiên, giọng nói, chụp hoá đơn). Có
ngân sách theo danh mục, mục tiêu tiết kiệm, báo cáo, dự báo chi tiêu và gamification.

Repo này chứa **backend + AI service**. Giao diện làm bằng Flutter và nằm ở repo riêng.

---

## Chạy toàn bộ hệ thống

Cần **Docker** và **Docker Compose**. Không cần cài .NET hay Python trên máy.

```bash
cp backend/.env.example backend/.env
cp ai-service/.env.example ai-service/.env
docker compose up -d
```

Xong. Năm service khởi động theo đúng thứ tự phụ thuộc, migration tự chạy:

| Service | Cổng | |
|---|---|---|
| `backend` | 8080 | ASP.NET Core 9 Web API |
| `ai-service` | 8000 | Python 3.11 FastAPI |
| `postgres-main` | 5433 | DB của backend |
| `postgres-ai` | 5434 | DB của AI service, tách hoàn toàn |
| `redis` | 6379 | cache, hạn mức, mã OTP |

Kiểm tra:

```bash
curl http://localhost:8080/health/ready     # backend + Postgres + Redis
curl http://localhost:8000/api/v1/health    # AI service
```

Tài liệu API tự sinh: **http://localhost:8080/swagger** và **http://localhost:8000/docs**

> **Sửa code rồi thì phải build lại**, `restart` không đủ — cả hai service COPY mã nguồn vào
> image chứ không mount:
> ```bash
> docker compose up -d --build backend      # hoặc ai-service
> ```

---

## Chạy được ngay, không cần cấu hình gì thêm

Ba tính năng gọi dịch vụ bên ngoài đều **tự lùi về chế độ local** khi thiếu khoá, thay vì
hỏng. Dòng log lúc khởi động luôn nói rõ đang ở chế độ nào.

| Thiếu cấu hình | Hệ quả |
|---|---|
| `BREVO_API_KEY` | mã OTP **in ra log** thay vì gửi email — đọc bằng `docker compose logs backend` |
| `FCM_CREDENTIALS_*` | thông báo đẩy chỉ ghi log |
| `GOOGLE_CLIENT_SECRET` | 3 endpoint `/auth/google/*` trả 422; đăng nhập email/mật khẩu không ảnh hưởng |

Nghĩa là clone về là chạy và thử được toàn bộ luồng nghiệp vụ. Hướng dẫn bật từng cái nằm
trong `backend/.env.example` và `.context/TECH_STACK.md` §4.2.

**Một thứ NÊN đổi ngay cả khi chạy local:** `JWT_SECRET`. Giá trị mẫu nằm công khai trong
repo. Ngoài môi trường `Development`, ứng dụng **từ chối khởi động** nếu còn bí mật mẫu nào.

---

## Kiểm thử

```bash
docker compose exec ai-service python -m pytest -q
cd backend && dotnet test          # hoặc chạy trong container SDK 9, xem backend/README.md
```

Integration test của backend tự dựng PostgreSQL thật bằng Testcontainers. Test của AI service
giả lập Tesseract, vì OCR cần gói hệ điều hành chỉ có trong image.

---

## Kiến trúc tóm tắt

```
Flutter app  ──►  backend (8080)  ──►  ai-service (8000)
                      │                     │
                 postgres-main          postgres-ai
                   + redis
```

**backend** — ASP.NET Core 9, chia lớp `API → Application → Domain ← Infrastructure`.
CQRS viết tay, không dùng MediatR. 12 controller công khai + 7 controller admin, 8 job định
kỳ (Hangfire), 23 bảng.

**ai-service** — FastAPI. Pipeline `Classifier → Extractor → Categorizer → Duplicate Detector`.
Bóc số tiền bằng **regex theo từng ngân hàng**, không bằng model — thông báo ngân hàng là mẫu
cố định, và số tiền không được phép là phỏng đoán của model. Phân loại thì dùng model
scikit-learn có phiên bản, tự lùi về luật khi chưa có model nào được promote. 13 bảng, DB
tách hẳn khỏi backend: **không có khoá ngoại nào bắc qua**, và AI DB **không bao giờ lưu
`user_id` thật** — chỉ lưu `SHA-256(user_id)`.

Sơ đồ thư mục đầy đủ và contract Backend ↔ AI Service: `.context/ARCHITECTURE.md` §2–3.

---

## Đọc thêm

| Tài liệu | Nội dung |
|---|---|
| [AGENTS.md](AGENTS.md) | luật bắt buộc, ranh giới phạm vi, khi nào phải hỏi người dùng |
| [.context/ARCHITECTURE.md](.context/ARCHITECTURE.md) | kiến trúc, DB schema, API contract, job định kỳ |
| [.context/TECH_STACK.md](.context/TECH_STACK.md) | package được dùng / bị cấm, biến môi trường |
| [.context/CONVENTIONS.md](.context/CONVENTIONS.md) | quy ước API, lỗi, validation, logging, bảo mật |
| [.context/TASKS.md](.context/TASKS.md) | tiến độ từng phase, và **ghi chép kiểm chứng** cuối file |
| [backend/README.md](backend/README.md) · [ai-service/README.md](ai-service/README.md) | chi tiết từng service |

Phần **Verify** ở cuối `TASKS.md` đáng đọc nhất: nó ghi lại những gì đã chạy thật bằng curl,
và cả những lỗi chỉ lộ ra khi chạy thật.

---

## Vài luật xuyên suốt, dễ vi phạm vì không được kiểu dữ liệu ép

- Tiền luôn là **`BIGINT` đơn vị đồng** — không `decimal`, không `float`.
- ID luôn là **`Guid`/UUID**, không `int`.
- Thời gian luôn là **`DateTimeOffset`/`TIMESTAMPTZ`**, không `DateTime` trần.
- Repository **luôn lọc theo `userId`**, và `userId` lấy từ JWT chứ không bao giờ từ body/path.
- Không bao giờ ghi log `notification_body`, `amount_cents`, mật khẩu hay token.

Danh sách đầy đủ ở `AGENTS.md` §3.

---

## Trạng thái

Backend và AI service **hoàn chỉnh cho phạm vi MVP** — 326/328 task ở `.context/TASKS.md`; hai
task còn lại bỏ có chủ ý (OTP qua SMS, 3 bảng A/B testing).

**Điều chưa được kiểm chứng, ghi ra để không ai hiểu nhầm:** Core Flow 3 và Core Flow 4 chưa
từng được đối chiếu với tài liệu đặc tả — chỉ Flow 1 và Flow 2 đã đọc trọn vẹn. Con số task ở
trên đo `TASKS.md` với chính nó, nên nó không chứng minh được gì cho hai flow đó.
