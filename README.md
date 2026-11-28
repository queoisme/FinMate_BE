# FinMate

Ứng dụng theo dõi chi tiêu cá nhân, tự động ghi nhận giao dịch từ **thông báo ngân hàng/ví
điện tử** bằng AI, cộng thêm ba kênh nhập khác: chat câu tự nhiên, giọng nói, chụp hoá đơn.

Repo này chứa **phía server**: backend và các dịch vụ AI. App khách hàng làm bằng Flutter, nằm
ở repo riêng `FinMate_FE`. Web cho Staff, Manager và Admin hiện mới có API, chưa có giao diện.

---

## Chức năng

| Nhóm | Nội dung |
|---|---|
| Giao dịch | Thông báo ngân hàng, chat, giọng nói, quét hoá đơn (tách theo món sau cờ `Receipt:SplitByItemsEnabled`, đang tắt), chuyển khoản qua mã VietQR. FinMate không tự chuyển tiền |
| Làm sạch giao dịch | Hoàn tiền và đảo giao dịch, bản trùng có liên kết (gộp bằng tay), gợi ý ghép cặp chuyển tiền giữa các ví của chính mình. Hệ thống chỉ gợi ý, người dùng xác nhận |
| Quản lý tài chính | Ngân sách và cảnh báo vượt ngưỡng, mục tiêu tiết kiệm, báo cáo kèm dòng tiền, sổ vay nợ (`/loans`), khoản định kỳ và trả góp, 13 phương pháp quản lý tài chính gợi ý theo khảo sát |
| Dự báo | Dòng tiền, số tiền có thể chi, giao dịch bất thường, ngân sách tháng sau, ngày đạt mục tiêu. Cảnh báo khi dữ liệu thiếu vì dịch vụ đọc thông báo trên điện thoại ngừng chạy. Độ chính xác đo bằng backtest trên dữ liệu giả lập (`backend/FinMate.Backtest`) |
| Tài chính nhóm | Ngân sách, mục tiêu, báo cáo chung và quỹ chung (sổ ảo, FinMate không giữ tiền). Ví vẫn của riêng từng người, thành viên tự chọn khoản muốn chia sẻ. Đang tắt bằng cờ `features.groups` và `features.groupFunds` |
| Cộng đồng | Bài viết theo chủ đề, ẩn danh mặc định, địa điểm theo vùng. Đang tắt bằng cờ `features.community` |
| Gói trả phí | Gói và tính năng theo gói, thanh toán qua SePay trên website riêng. Đang tắt bằng cờ `features.subscriptions` |
| Dữ liệu cá nhân | Xuất CSV giao dịch và gói ZIP toàn bộ dữ liệu (cờ `features.dataExport`, bật mặc định), yêu cầu xoá tài khoản, Điều khoản và Chính sách có phiên bản |
| Vận hành nội bộ | Ba vai trò `Staff`, `OpsManager`, `Admin`: ca hỗ trợ, xem dữ liệu khách khi khách đồng ý, giao dịch nháp, duyệt hai người, xác thực 2 lớp, Staff soạn và OpsManager duyệt tham số dự báo, dashboard KPI sản phẩm. API khách hàng đang tắt bằng cờ `features.support` |
| Thông tin thị trường | Admin gửi link trang giá xăng, báo, trang tài chính. Hệ thống tự đọc, AI hiểu tin bài, admin duyệt, rồi nhắc người có ngân sách đang căng. Cảnh báo đang tắt bằng cờ `features.marketAlerts` |
| Giám sát | Kiểm tra dịch vụ mỗi phút, gom lỗi thành sự cố, theo dõi ngân hàng đổi mẫu thông báo, gửi email cho Admin và OpsManager |
| Quản trị | Người dùng, quyền theo vai trò, cấu hình hệ thống, khoá dịch vụ bên ngoài, luật và model AI, văn bản pháp lý, nhật ký thao tác |

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

Bảy service khởi động theo thứ tự phụ thuộc, migration tự chạy:

| Service | Cổng trên máy | |
|---|---|---|
| `backend` | 8080 | ASP.NET Core 9 Web API, gồm cả trang Hangfire `/hangfire` |
| `ai-service` | 8000 | Python 3.11 FastAPI |
| `ocr-service` | không mở | đọc hoá đơn, Python 3.10 FastAPI |
| `stt-service` | không mở | giọng nói thành chữ |
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

Backtest dự báo (không cần DB, khoảng 1 phút, ghi báo cáo vào `docs/evaluation/`):

```bash
cd backend && dotnet run --project FinMate.Backtest -c Release -- --out ../docs/evaluation
```

---

## Kiến trúc tóm tắt

```
Flutter app ──┐                   ┌──► ai-service ──► postgres-ai
              ├──► backend (8080) ┤
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
| [docs/ERD.md](docs/ERD.md) | sơ đồ và mô tả từng bảng |
| Giao dịch: [refund](docs/transactions-refund-api.md), [trùng](docs/transactions-duplicates-api.md), [ghép chuyển tiền](docs/transactions-transfer-pairing-api.md), [mở rộng](docs/transaction-extras-api.md) | hoàn tiền/đảo, bản trùng, chuyển tiền giữa ví của mình, nháp và gợi ý |
| [loans](docs/loans-api.md), [recurring-items](docs/recurring-items-api.md), [reports-cash-flow](docs/reports-cash-flow-api.md), [goals-and-reports-extras](docs/goals-and-reports-extras-api.md) | sổ vay nợ, khoản định kỳ và trả góp, dòng tiền của báo cáo |
| [forecast-cashflow](docs/forecast-cashflow-api.md), [listener-health](docs/listener-health-api.md), [agent](docs/agent-api.md) | dự báo, sức khoẻ dịch vụ đọc thông báo, trợ lý |
| [group-api](docs/group-api.md), [group-spending](docs/group-spending-api.md) | API và ma trận quyền của tài chính nhóm, quỹ chung |
| [ops-api](docs/ops-api.md), [ops-runbook](docs/ops-runbook.md) | API, ma trận quyền và quy trình của vận hành, giám sát, KPI, văn bản pháp lý |
| [market-data](docs/market-data-api.md), [community](docs/community-api.md), [subscription](docs/subscription-api.md), [data-export](docs/data-export-api.md) | thông tin thị trường, cộng đồng, gói trả phí, xuất dữ liệu |
| [forecast-backtest](docs/evaluation/forecast-backtest.md), [forecast-model-fixes](docs/evaluation/forecast-model-fixes.md), [evaluation](docs/evaluation.md) | đánh giá dự báo và AI |
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

## Trạng thái (2026-11-28)

`main` có tới Phase 97 (sửa mô hình dự báo số dư theo chẩn đoán backtest, PR #41).

**Chưa có**

- Giao diện web vận hành, gồm bước nhập OTP.
- Các màn hình Flutter cho tài chính nhóm, hỗ trợ. `features.groups` và `features.support` vẫn tắt tới khi xong.
- Điều khoản bản mới (Google Vision, nhóm, hỗ trợ): bản 4 trong bảng `legal_versions` là chữ tạm, bản
  thật cần người phụ trách pháp lý duyệt trước khi Admin xuất bản.
- Dịch vụ theo dõi bên ngoài cho `/health/ready`.
- Trước khi bật `features.community`: cách liên hệ tác giả ẩn danh đang gặp nguy hiểm và đường dây hỗ
  trợ đã kiểm chứng. Trước khi bật `features.subscriptions`: chính sách Google Play và một lần chạy SePay
  Sandbox thật. Chi tiết ở `CLAUDE.md` và `docs/ops-runbook.md`.

**Test đỏ đã biết** (backend, cả hai chờ chủ sản phẩm quyết, `REQUIREMENTS.md` §13.4)

- `MethodSelectionOnSeedDataTests…GetsCashFlowBudget…`: hồ sơ mẫu ra `rule_80_20` thay vì `cash_flow_budget`.
- `NotificationsControllerTests.Analyze_HighConfidence…AutoCommits…`: đặc tả muốn tự ghi khi độ tin cậy
  ≥ 85%, code đang làm theo hướng xác nhận một chạm (Phase 15).

**Chưa kiểm chứng**

- Thông tin thị trường: AI đọc tin bằng Gemini thật, vì chưa có key. Điều khoản của từng nguồn cũng chưa được kiểm tra.
- Giám sát: gửi email thật qua Brevo và một job Hangfire thất bại thật. Đã chạy trên backend local
  với email ghi ra log.
- Gói trả phí: SePay thật. Xuất dữ liệu: mở file CSV bằng Excel (vùng Vietnamese) và Google Sheets.
- `ocr-service`: bộ test chưa chạy lại từ lần ghi "5 test đỏ" (2026-11-06).

Tiến độ chi tiết ở `.context/TASKS.md`. Khoảng cách so với đặc tả ở `docs/REQUIREMENTS.md` §13.1.
