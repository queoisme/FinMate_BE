# ARCHITECTURE.md — FinMate System Architecture

---

## 0. Quyết định đối chiếu với `FinMate_Core_User_Flows.docx` (2026-09-11)

Bản đặc tả `FinMate_Core_User_Flows.docx` (4 Core User Flows) trước đây chưa từng được đối chiếu
với `.context/`. Sau khi rà soát, 4 điểm lệch đã được user chốt như sau — **file này là nguồn sự
thật cho code; docx là nguồn cho yêu cầu nghiệp vụ.** Chỗ nào 2 bên còn lệch mà chưa ghi ở đây
thì hỏi user, đừng tự chọn.

| # | Điểm lệch | Quyết định | Ảnh hưởng |
|---|---|---|---|
| 1 | Docx Flow 2(c) có **Internal Transfer** (rút ATM, nạp ví), `.context/` không có | **Làm** | `TransactionType` thêm `Transfer`; `transactions` thêm `counter_account_id`; mọi phép tính chi tiêu/ngân sách/báo cáo phải loại `transfer` ra |
| 2 | Docx cảnh báo ngân sách **70/90/100%**, §5 file này ghi 80/100% | **Đổi theo docx: 70/90/100** | `budget_periods`: 3 cột cờ `alert_70/90/100_sent_at`; `BudgetAlertJob` 3 mốc |
| 3 | Docx bước 1.1 yêu cầu đăng ký bằng **SĐT + OTP** | **Chưa làm trong MVP** | Giữ Email/Password + Google login. Docx coi như overspec ở điểm này |
| 4 | Docx Flow 2 có **Voice input** và **Receipt OCR**; `TASKS.md` để ở Backlog | **Kéo lên MVP** | Voice: STT ở client → tái dùng `POST /transactions/parse`, không cần route AI mới. OCR: **cần thêm route mới vào contract §3.3** |

**Nguyên tắc Internal Transfer (quyết định #1) — dễ sai nhất, đọc kỹ:**

- Transfer chuyển tiền giữa **2 tài khoản của cùng 1 user**: `financial_account_id` là ví nguồn
  (trừ tiền), `counter_account_id` là ví đích (cộng tiền). Cả 2 phải thuộc về user gọi API.
- Transfer **KHÔNG** tiêu hạn mức ngân sách, **KHÔNG** tính vào chi tiêu tháng, **KHÔNG** tính
  vào thu nhập, **KHÔNG** vào forecast/insight/daily summary. Đây là lý do tồn tại của nó: rút
  tiền ATM mà tính thành `debit` sẽ thổi phồng chi tiêu và trừ oan ngân sách của user.
- Transfer **không có category** — `category_id` phải NULL.
- Mọi truy vấn cộng dồn chi tiêu đều phải lọc `transaction_type <> 'transfer'`. Thêm truy vấn
  tổng hợp mới ở bất kỳ đâu thì kiểm tra lại điều này trước.

---

## 1. Tổng quan hệ thống

```
┌─────────────────────────────────────────────────────────────────┐
│                        Android App (Kotlin)                     │
│   NotificationListenerService │ UI │ Mascot │ Dashboard         │
└────────────────────┬──────────────────────────────────────────--┘
                     │ HTTPS REST
                     ▼
┌─────────────────────────────────────────────────────────────────┐
│                 Backend — ASP.NET Core 9 Web API                │
│                                                                 │
│  ┌──────────┐ ┌───────────┐ ┌──────────┐ ┌───────────────────┐ │
│  │   Auth   │ │Transaction│ │  Budget  │ │  Gamification     │ │
│  │  Module  │ │  Module   │ │  Module  │ │  Module           │ │
│  └──────────┘ └───────────┘ └──────────┘ └───────────────────┘ │
│  ┌──────────┐ ┌───────────┐ ┌──────────┐ ┌───────────────────┐ │
│  │Notif.    │ │ Category  │ │  Saving  │ │  Reports          │ │
│  │  Module  │ │  Module   │ │  Module  │ │  Module           │ │
│  └──────────┘ └───────────┘ └──────────┘ └───────────────────┘ │
│                         │ HTTP nội bộ                           │
└─────────────────────────┼───────────────────────────────────────┘
                          │
                          ▼
┌─────────────────────────────────────────────────────────────────┐
│                  AI Service — Python 3.11 FastAPI               │
│                                                                 │
│  Classifier → Extractor → Categorizer → Duplicate Detector     │
│                                                                 │
│  PostgreSQL AI DB (tách biệt hoàn toàn)                        │
└─────────────────────────────────────────────────────────────────┘

Storage:
  Backend DB  → PostgreSQL 16 (Main)
  AI DB       → PostgreSQL 16 (AI — tách riêng)
  Cache       → Redis 7
  Background  → Hangfire (jobs chạy trên backend process)
```

---

## 2. Backend — Layered Architecture

### 2.1 Cấu trúc thư mục backend

```
backend/
├── FinMate.API/                  # Entry point, Controllers, Middleware
│   ├── Controllers/
│   │   ├── AuthController.cs
│   │   ├── TransactionsController.cs
│   │   ├── BudgetsController.cs
│   │   ├── SavingGoalsController.cs
│   │   ├── CategoriesController.cs
│   │   ├── NotificationsController.cs
│   │   ├── ReportsController.cs
│   │   ├── GamificationController.cs
│   │   └── Admin/
│   │       ├── AdminUsersController.cs
│   │       ├── AdminProviderConfigsController.cs
│   │       └── AdminMissionsController.cs
│   ├── Middleware/
│   │   ├── ExceptionHandlingMiddleware.cs
│   │   ├── RequestLoggingMiddleware.cs
│   │   └── RateLimitingMiddleware.cs
│   └── Program.cs
│
├── FinMate.Application/          # Business logic, Use Cases
│   ├── Auth/
│   │   ├── Commands/             # Write operations (CQRS pattern)
│   │   │   ├── RegisterCommand.cs
│   │   │   ├── LoginCommand.cs
│   │   │   └── RefreshTokenCommand.cs
│   │   └── Queries/              # Read operations
│   │       └── GetUserProfileQuery.cs
│   ├── Transactions/
│   │   ├── Commands/
│   │   │   ├── ConfirmTransactionCommand.cs
│   │   │   ├── CreateManualTransactionCommand.cs
│   │   │   └── DeleteTransactionCommand.cs
│   │   └── Queries/
│   │       ├── GetTransactionListQuery.cs
│   │       └── GetTransactionDetailQuery.cs
│   ├── Budgets/
│   ├── SavingGoals/
│   ├── Notifications/
│   │   └── Commands/
│   │       └── AnalyzeNotificationCommand.cs
│   ├── Reports/
│   ├── Gamification/
│   └── Common/
│       ├── Interfaces/           # IRepository, IAIService, ICacheService...
│       ├── Exceptions/           # Domain exceptions
│       └── Models/               # DTOs, Request/Response models
│
├── FinMate.Domain/               # Domain entities, Value objects
│   ├── Entities/
│   │   ├── User.cs
│   │   ├── Transaction.cs
│   │   ├── Budget.cs
│   │   ├── BudgetPeriod.cs
│   │   ├── SavingGoal.cs
│   │   ├── Category.cs
│   │   ├── FinancialAccount.cs
│   │   ├── NotificationLog.cs
│   │   ├── AiResult.cs
│   │   └── Gamification/
│   │       ├── UserGamification.cs
│   │       ├── Mission.cs
│   │       └── UserMission.cs
│   ├── Enums/
│   │   ├── TransactionType.cs
│   │   ├── TransactionSource.cs
│   │   ├── AccountType.cs
│   │   └── MissionPeriodType.cs
│   └── ValueObjects/
│       └── Money.cs              # Wrapper cho BIGINT amount
│
├── FinMate.Infrastructure/       # EF Core, Repos, External services
│   ├── Persistence/
│   │   ├── FinMateDbContext.cs
│   │   ├── Configurations/       # IEntityTypeConfiguration per entity
│   │   │   ├── UserConfiguration.cs
│   │   │   ├── TransactionConfiguration.cs
│   │   │   └── ...
│   │   ├── Migrations/
│   │   └── Repositories/
│   │       ├── TransactionRepository.cs
│   │       ├── BudgetRepository.cs
│   │       └── ...
│   ├── ExternalServices/
│   │   ├── AIServiceClient.cs    # HttpClient gọi AI Service
│   │   └── PushNotificationService.cs
│   ├── Caching/
│   │   └── RedisCacheService.cs
│   └── BackgroundJobs/
│       ├── BudgetAlertJob.cs
│       ├── StreakCheckJob.cs
│       ├── ForecastJob.cs
│       └── DataCleanupJob.cs
│
└── FinMate.Tests/
    ├── Unit/
    └── Integration/
```

### 2.2 Data flow — Request lifecycle

```
HTTP Request
    │
    ▼
[Middleware] ExceptionHandling → RateLimiting → RequestLogging
    │
    ▼
[Controller] Validate input schema → Map sang Command/Query → Gọi Handler
    │                                (Controller KHÔNG chứa business logic)
    ▼
[Application Handler] Business logic → Gọi Repository → Gọi External Service
    │
    ▼
[Repository] EF Core → PostgreSQL
    │
    ▼
[Response] Map Entity → DTO → HTTP Response
```

### 2.3 Ranh giới tầng — Layer boundaries

| Tầng | Được phép gọi | Không được phép gọi |
|---|---|---|
| API (Controller) | Application layer | Domain, Infrastructure trực tiếp |
| Application | Domain, Infrastructure interfaces | Infrastructure concrete classes |
| Domain | Không ai | Không gọi tầng nào khác |
| Infrastructure | Domain | Application, API |

---

## 3. AI Service — Pipeline Architecture

### 3.1 Cấu trúc thư mục AI Service

```
ai-service/
├── app/
│   ├── main.py                   # FastAPI entry point
│   ├── api/
│   │   ├── v1/
│   │   │   ├── pipeline.py       # POST /api/v1/analyze
│   │   │   ├── feedback.py       # POST /api/v1/feedback
│   │   │   └── health.py         # GET /api/v1/health
│   │   └── deps.py               # Dependencies injection
│   │
│   ├── pipeline/                 # Core AI pipeline
│   │   ├── orchestrator.py       # Điều phối các stages
│   │   ├── stages/
│   │   │   ├── classifier.py     # Stage 1: Financial/Non-financial
│   │   │   ├── extractor.py      # Stage 2: Amount, merchant, date
│   │   │   ├── categorizer.py    # Stage 3: Category + confidence
│   │   │   └── duplicate_detector.py  # Stage 4: Dedup check
│   │   └── models/
│   │       ├── base_model.py
│   │       └── model_registry.py # Load model theo version
│   │
│   ├── schemas/                  # Pydantic models
│   │   ├── request.py            # AnalyzeRequest, FeedbackRequest
│   │   └── response.py           # AnalyzeResponse, PipelineResult
│   │
│   ├── db/
│   │   ├── session.py            # SQLAlchemy session
│   │   ├── models/               # SQLAlchemy ORM models (AI DB)
│   │   │   ├── raw_sample.py
│   │   │   ├── labeled_sample.py
│   │   │   ├── model_version.py
│   │   │   ├── pipeline_request.py
│   │   │   └── user_feedback.py
│   │   └── repositories/
│   │       ├── sample_repo.py
│   │       └── pipeline_repo.py
│   │
│   ├── core/
│   │   ├── config.py             # Settings từ env vars
│   │   ├── security.py           # Verify internal API key từ backend
│   │   └── anonymizer.py         # SHA-256 user_id, anonymize body
│   │
│   └── utils/
│       ├── provider_patterns.py  # Rule-based extraction fallback
│       └── text_utils.py         # VND amount parsing: "75k", "1.5tr"
│
├── tests/
│   ├── unit/
│   └── integration/
├── alembic/                      # DB migrations cho AI DB
├── requirements.txt
└── Dockerfile
```

### 3.2 Pipeline flow chi tiết

```
POST /api/v1/analyze
  Body: { backend_request_id, user_id_hash, package_name,
          notification_title, notification_body, received_at }
    │
    ▼
[Orchestrator]
    │
    ├─► [1. Classifier]
    │       Input: notification_body, package_name
    │       Output: { label: "financial|non_financial|uncertain",
    │                 confidence: 0.0-1.0 }
    │       If label != "financial" → early return
    │
    ├─► [2. Extractor]  (chỉ chạy nếu financial)
    │       Input: notification_body, package_name, provider_patterns
    │       Output: { amount_cents, transaction_type: "debit|credit",
    │                 merchant_name, description, transacted_at,
    │                 balance_after_cents }
    │       Fallback: rule-based extraction nếu model uncertain
    │
    ├─► [3. Categorizer]
    │       Input: merchant_name, description, amount_cents, transaction_type
    │       Output: { category_slug, confidence: 0.0-1.0 }
    │
    ├─► [4. Duplicate Detector]
    │       Input: user_id_hash, amount_cents, transacted_at, package_name
    │       Logic: query pipeline_requests trong 5 phút gần nhất
    │       Output: { is_duplicate: bool, duplicate_request_id? }
    │
    ├─► [Logger] Ghi vào pipeline_requests
    │
    └─► Return PipelineResult
```

### 3.3 API Contract — Backend ↔ AI Service

**Request (Backend → AI Service):**
```json
POST /api/v1/analyze
Authorization: Bearer {INTERNAL_API_KEY}
{
  "backend_request_id": "uuid",
  "user_id_hash": "sha256-of-user-id",
  "package_name": "com.mb.android",
  "notification_title": "Thong bao giao dich",
  "notification_body": "TK 123: -75,000VND...",
  "received_at": "2026-09-09T10:30:00+07:00"
}
```

**Response (AI Service → Backend):**
```json
{
  "pipeline_result": "financial|non_financial|uncertain|extraction_failed|error",
  "classifier": {
    "label": "financial",
    "confidence": 0.97
  },
  "extraction": {
    "amount_cents": 75000,
    "transaction_type": "debit",
    "merchant_name": "Highlands Coffee",
    "description": "Thanh toan Highlands",
    "transacted_at": "2026-09-09T10:28:00+07:00",
    "balance_after_cents": 2500000,
    "confidence": 0.91
  },
  "categorization": {
    "category_slug": "food",
    "confidence": 0.89
  },
  "duplicate": {
    "is_potential_duplicate": false,
    "duplicate_request_id": null
  },
  "model_versions": {
    "classifier": "1.2.3",
    "extractor": "1.1.0",
    "categorizer": "2.0.1"
  },
  "processing_ms": 245
}
```

**Feedback (Backend → AI Service):**
```json
POST /api/v1/feedback
{
  "backend_transaction_id_hash": "sha256",
  "user_id_hash": "sha256",
  "pipeline_request_id": "uuid",
  "package_name": "com.mb.android",
  "predicted_category": "food",
  "corrected_category": "entertainment",
  "feedback_type": "category_correction"
}
```

---

## 4. Database Architecture

### 4.1 Backend DB — 19 bảng, 5 domain groups

```
Auth & Identity:
  users, refresh_tokens, audit_logs, data_deletion_requests

Financial Core:
  financial_accounts, provider_configs,
  notification_logs, ai_results,
  transactions, categories

Budget & Goals:
  budgets, budget_periods,
  saving_goals, goal_contributions

Reports:
  daily_summaries, spending_insights

Gamification:
  user_gamification, missions, user_missions,
  mascot_items, user_mascot_items
```

### 4.2 AI DB — 12 bảng đã tạo, 3 bảng hoãn

```
Dataset:        raw_samples, labeled_samples, sample_splits, provider_patterns
Model:          model_versions, training_jobs
Evaluation:     evaluation_runs, evaluation_category_metrics, evaluation_predictions
Production:     pipeline_requests
Feedback:       user_feedback, feedback_batch_jobs
A/B Testing:    ab_experiments, ab_assignments, ab_metrics   ← HOÃN, chưa tạo
```

**Cập nhật 2026-09-11 (Phase 9).** Mục này trước ghi "14 bảng" nhưng danh sách bên dưới
liệt kê 15 tên — con số cũ đếm sai, không phải danh sách thiếu.

Đã tạo **12 bảng** qua 4 migration alembic (`0001_dataset`, `0002_model_registry`,
`0003_evaluation`, `0004_production_feedback`). Ba bảng `ab_*` **chưa tạo**: A/B testing
nằm ở mục Backlog "ngoài MVP scope" của `TASKS.md`, và bảng rỗng vĩnh viễn không phải cách
production làm — migration là thứ rẻ, tạo khi tính năng ship. Quyết định này đã chốt với
user trước khi làm Phase 9.

Hai điểm dễ đọc nhầm thành vi phạm quy tắc:

- `pipeline_requests` **lưu** `amount_cents`. Bắt buộc: Duplicate Detector (§3.2 bước 4)
  truy vấn chính cột đó. `AGENTS.md` §3.2 cấm **log** số tiền, không cấm lưu.
- `raw_samples` giữ nội dung thông báo. Đó là tập dữ liệu huấn luyện, và nội dung đã qua
  `anonymizer` (che số tài khoản/thẻ/SĐT/email, giữ nguyên số tiền vì số tiền là nhãn của
  chính bài toán). Bản gốc chưa che chỉ tồn tại ở `notification_logs` phía backend và bị
  `DataCleanupJob` xoá sau 90 ngày theo §7.3.

### 4.3 Quy tắc database quan trọng

- `amount_cents` → **BIGINT**, đơn vị đồng VND, KHÔNG dùng DECIMAL
- `id` → **UUID** (gen_random_uuid()), không expose BIGSERIAL ra API
- Thời gian → **TIMESTAMPTZ** (có timezone), không dùng TIMESTAMP
- Soft delete → `deleted_at TIMESTAMPTZ NULL` (NULL = chưa xóa)
- Enum → **TEXT + CHECK constraint**, không dùng PostgreSQL ENUM type
- AI DB → **KHÔNG FK** sang Backend DB, dùng `_hash` fields để reference
- `transaction_type='transfer'` → **luôn loại khỏi mọi phép cộng dồn chi tiêu/thu nhập** (budget, report, forecast, daily summary). Xem §0 quyết định #1

---

## 5. Background Jobs (Hangfire)

| Job | Schedule | Mô tả |
|---|---|---|
| `BudgetAlertJob` | Mỗi giờ | Kiểm tra budget threshold 70%/90%/100%, gửi alert (xem §0 quyết định #2) |
| `StreakCheckJob` | 23:55 mỗi ngày | Reset streak nếu user không có transaction hôm nay |
| `ForecastJob` | 06:00 mỗi ngày | Gọi AI Service tính spending forecast tháng |
| `InsightGeneratorJob` | 02:00 mỗi ngày | Generate spending insights từ transaction data |
| `DailySummaryJob` | 00:05 mỗi ngày | Tổng hợp daily_summaries của ngày hôm trước |
| `MissionResetJob` | 00:01 mỗi ngày/tuần | Reset daily/weekly missions |
| `DataCleanupJob` | 03:00 mỗi ngày | Xóa raw notification body > 90 ngày, process deletion requests |
| `RetryFailedNotifJob` | Mỗi 15 phút | Retry notification_logs với status='failed', retry_count < 3 |

---

## 6. Caching Strategy (Redis)

| Cache key | TTL | Nội dung |
|---|---|---|
| `user:{id}:profile` | 15 phút | User profile (refresh khi update) |
| `user:{id}:budget_summary:{year}:{month}` | 5 phút | Budget summary tháng hiện tại |
| `user:{id}:active_missions` | 30 phút | Danh sách missions đang active |
| `categories:system` | 60 phút | System categories (ít thay đổi) |
| `provider_configs:active` | 60 phút | Provider configs (admin managed) |
| `user:{id}:gamification` | 10 phút | Level, EXP, streak |

**Cache invalidation:**
- Khi user confirm transaction → invalidate `budget_summary` và `gamification`
- Khi admin cập nhật provider_configs → invalidate `provider_configs:active`
- Khi user update profile → invalidate `user:{id}:profile`

---

## 7. Security Architecture

### 7.1 Authentication flow
```
Login → access_token (JWT, 15 phút) + refresh_token (opaque, 30 ngày)
      ↓
access_token hết hạn → POST /auth/refresh (với refresh_token)
      ↓
Refresh Token Rotation: hủy token cũ, cấp cặp token mới
      ↓
Nếu refresh_token bị reuse → hủy TẤT CẢ tokens của user (security breach)
```

### 7.2 Internal security (Backend ↔ AI Service)
- AI Service không expose ra public internet
- Xác thực bằng `INTERNAL_API_KEY` trong header `Authorization: Bearer {key}`
- Key được set qua environment variable, không hardcode

### 7.3 Data privacy
- `notification_body` gốc → xóa sau 90 ngày (DataCleanupJob)
- AI DB không lưu `user_id` thực → luôn SHA-256 hash
- Admin không đọc được `amount_cents` hay `description` của user cụ thể
