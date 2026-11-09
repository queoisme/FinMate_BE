# FinMate Backend — ASP.NET Core 9

Layered architecture (API → Application → Domain → Infrastructure), CQRS thủ công (không dùng MediatR).
Chi tiết đầy đủ: xem `../.context/ARCHITECTURE.md` §2, `../.context/TECH_STACK.md` §1, `../.context/CONVENTIONS.md`.

## Projects

| Project | Vai trò |
|---|---|
| `FinMate.API` | Entry point, Controllers, Middleware |
| `FinMate.Application` | Business logic, Commands/Queries (CQRS thủ công, không dùng MediatR) |
| `FinMate.Domain` | Entities, Enums, Value Objects — không phụ thuộc tầng nào khác |
| `FinMate.Infrastructure` | EF Core, Repositories, External services, Caching, Background jobs |
| `FinMate.Tests` | Unit + Integration tests |

## Nhóm API

Danh sách đầy đủ xem ở Swagger (`http://localhost:8080/swagger`). Quyền được đặt ở lớp cơ sở, nên
không gắn lại trên từng controller.

| Đường dẫn | Dành cho | Tài liệu |
|---|---|---|
| `/api/v1/*` | Khách hàng trên app Flutter | Swagger; nhóm ở `../docs/group-api.md` |
| `/api/v1/admin/*` | `Admin`, kế thừa `AdminControllerBase` | `FinMate.API/Controllers/Admin/README.md` |
| `/api/v1/ops/*` | `Staff`, `OpsManager`, `Admin`, kế thừa `OpsControllerBase` | `../docs/ops-api.md` |
| `/api/v1/admin/market-*`, `/api/v1/admin/fuel-prices` | `Admin`: thông tin thị trường | `../docs/market-data-api.md` |
| `/health/live`, `/health/ready` | Hạ tầng. `live` chỉ báo tiến trình còn sống, `ready` kiểm tra Postgres và Redis | |
| `/hangfire` | Dashboard job, basic auth `HANGFIRE_DASHBOARD_USER/PASS` | |

## Job định kỳ

Hangfire đọc cron theo **UTC**, nên job chạy theo giờ Việt Nam phải tự quy đổi (trừ 7 giờ). Ví dụ
`daily-summary` `5 17 * * *` là 00:05 giờ Việt Nam.

| Job | Cron (UTC) | Việc |
|---|---|---|
| `market-data-refresh` | `5 */3 * * *` | Đọc nguồn thị trường, xác nhận giá xăng, gửi bài cho AI, xét cảnh báo |
| `system-monitor` | `* * * * *` | Kiểm tra dịch vụ, mở và đóng sự cố, gửi email. Tắt bằng `MONITORING_ENABLED=false` |
| `monitoring-digest` | `0 1 * * *` | Gom sự cố mức thường, xoá lỗi quá hạn. Tắt cùng job trên |
| `retry-failed-notifications` | `*/15 * * * *` | Thử lại thông báo phân tích lỗi |
| `forecast-alert-dispatch` | `*/15 * * * *` | Gửi cảnh báo dự báo đang chờ |
| `budget-alerts` | `0 * * * *` | Lưới an toàn cho cảnh báo ngân sách |
| `ai-category-sync` | `30 * * * *` | Đồng bộ danh mục sang ai-service |
| `forecast-daily-alerts` | `0 1 * * *` | Kiểm tra dự báo hằng ngày |
| `goal-deadline-check` | `0 1 * * *` | Nhắc mục tiêu quá hạn, 08:00 giờ Việt Nam |
| `daily-summary` | `5 17 * * *` | Tổng hợp ngày |
| `recurring-detection` | `30 18 * * *` | Nhận diện khoản định kỳ |
| `insight-generator` | `0 19 * * *` | Sinh gợi ý chi tiêu |
| `data-deletion` | `0 20 * * *` | Xoá hẳn tài khoản đã yêu cầu xoá, 03:00 giờ Việt Nam |
| `support-retention` | `30 20 * * *` | Đóng ca đã giải quyết sau 7 ngày, xoá ảnh sau khi đóng 30 ngày, 03:30 giờ Việt Nam |

## Điểm cần biết khi làm tiếp

- Lịch theo **giờ Việt Nam (UTC+7) cố định** qua `Application/Common/VietnamTime.cs`. Không dùng
  `TimeZoneInfo.FindSystemTimeZoneById`, vì `InvariantGlobalization=true` bật cho cả solution. Mốc chu
  kỳ trả về ở **offset 0**, vì Npgsql từ chối ghi `DateTimeOffset` có offset khác 0 vào `timestamptz`.
- Vận hành chỉ xem dữ liệu khách qua `IOpsCustomerDataService`, xem `../docs/ops-api.md`. Repository theo `userId`
  không được thêm nhánh "hoặc nhân viên", kể cả với Admin.
- Các luật dễ vi phạm khác của từng mảng nằm trong `../CLAUDE.md`.

## Setup local

```bash
cp .env.example .env
```

Giá trị mẫu đủ để chạy trong môi trường `Development`. Ngoài `Development`, ứng dụng từ chối khởi
động nếu các biến bí mật còn giá trị mẫu (`Configuration/StartupSecretGuard.cs`). Các biến cần chú ý:

- `JWT_SECRET`: chuỗi ngẫu nhiên từ 64 ký tự. Nên đổi ngay cả khi chạy local, vì giá trị mẫu công khai.
- `GOOGLE_CLIENT_ID`: **Web OAuth Client ID**, không phải Android Client ID hay API Key. Bắt buộc có
  giá trị để app khởi động, dù chưa dùng đăng nhập Google.
- `AI_SERVICE_URL`, `AI_SERVICE_API_KEY`: bắt buộc để app khởi động.
- `ADMIN_SEED_EMAIL`, `ADMIN_SEED_PASSWORD`: tài khoản admin được tạo khi khởi động, email đã xác minh sẵn.

Chạy cả hệ thống theo [README gốc](../README.md). Chỉ cần backend và hạ tầng thì chạy từ thư mục gốc repo:

```bash
docker compose up -d --no-deps postgres-main redis
docker compose up -d --no-deps backend
curl http://localhost:8080/health/ready
```

Backend tự chạy migration và seed khi khởi động, không cần chạy migration thủ công cho local dev.
Với `--no-deps`, các tính năng cần AI, OCR, giọng nói sẽ báo lỗi cho tới khi bật các service đó.

## Commands

```bash
dotnet build
dotnet test
dotnet test --filter FullyQualifiedName~ClassName.MethodName   # 1 test cụ thể
dotnet ef migrations add <PascalCaseDescriptiveName> --project FinMate.Infrastructure --startup-project FinMate.API
```

**Máy không có .NET 9 SDK:** chạy trong container `mcr.microsoft.com/dotnet/sdk:9.0`, và phải mount
**thư mục gốc repo**, vì `FinMate.Application` nhúng `../../ocr-service/data/shared_words.json`. Chạy từ
thư mục gốc repo:

```bash
docker run --rm -v "$PWD":/repo -w /repo/backend mcr.microsoft.com/dotnet/sdk:9.0 dotnet build

docker run --rm -v "$PWD":/repo -w /repo/backend \
  -v /var/run/docker.sock:/var/run/docker.sock \
  --network host -e TESTCONTAINERS_HOST_OVERRIDE=localhost \
  mcr.microsoft.com/dotnet/sdk:9.0 dotnet test
```

**Docker daemon tắt, dùng rootless podman thay thế:**

```bash
systemctl --user start podman.socket
podman run --rm --security-opt label=disable --network host \
  -v "$PWD":/repo -w /repo/backend \
  -v /run/user/1000/podman/podman.sock:/var/run/docker.sock \
  -e TESTCONTAINERS_RYUK_DISABLED=true -e TESTCONTAINERS_HOST_OVERRIDE=localhost \
  mcr.microsoft.com/dotnet/sdk:9.0 dotnet test
```

Toàn bộ test mất khoảng 9 phút. `dotnet test` cần Docker hoặc podman vì Testcontainers dựng PostgreSQL
thật cho integration test. Test thay Google và AI Service bằng `FakeGoogleTokenVerifier`,
`FakeAIServiceClient`, đăng ký trong `AuthApiFactory`.

## Trạng thái

Tiến độ theo phase ở `../.context/TASKS.md`. Phần còn thiếu so với đặc tả ở `../docs/REQUIREMENTS.md` §13.1.
