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
│            Client app (Flutter, `FinMate_FE`) — xem ghi chú     │
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

**Thêm 2026-10-10 (luồng quét hóa đơn, tách từng món):** ngoài hai khối trên còn có `ocr-service`
(container Python riêng, PaddleOCR; chỉ backend/ai-service gọi nội bộ) và **Gemini API** như một
bên thứ ba tuỳ chọn (mặc định tắt, chỉ nhận tên món đã lọc — xem §3.4 và §7.3). Luồng: Ảnh → backend →
`ocr-service` (đọc, lọc dữ liệu cá nhân, tách món) → backend → AI Service `classify-items` → backend
cộng tiền theo nhóm → app. App chỉ nói chuyện với backend.

**Cập nhật 2026-10-11 (đối chiếu code, Phase 19–23).**
- Khối trên cùng trước ghi "Android App (Kotlin)". Client đang làm là app **Flutter** ở repo riêng `FinMate_FE` (`TASKS.md`
  mục "Client app (`FinMate_FE`, Flutter)…"); repo này không chứa code client nên chi tiết bên trong app không mô tả ở đây.
- Ngoài AI Service còn hai container nội bộ, không mở cổng ra máy chủ (`docker-compose.yml`): `ocr-service` (§3.5) và
  `rasa` (Rasa NLU cho chat, chỉ ai-service gọi — §3.6). Bảng tổng quan đầy đủ hơn:

```
App ──HTTPS──► Backend ──Bearer AI_SERVICE_API_KEY──► AI Service ──RASA_URL──► rasa (NLU, tuỳ chọn)
                  │                                      │  └──GEMINI (tuỳ chọn, chỉ tên món)──► Gemini API
                  └──Bearer OCR_SERVICE_API_KEY──► ocr-service          (ai-service /api/v1/ocr cũng gọi được ocr-service)
                                                         │
                     ai-service + ocr-service ──────────► PostgreSQL AI DB `finmate_ai` (bảng bill_*: §4.2)
```

---

## 2. Backend — Layered Architecture

### 2.1 Cấu trúc thư mục backend

```
backend/
├── FinMate.API/                  # Entry point, Controllers, Middleware
│   ├── Controllers/
│   │   ├── AuthController.cs
│   │   ├── TransactionsController.cs   # gồm POST scan-receipt, chat, parse (§2.4)
│   │   ├── BudgetsController.cs
│   │   ├── SavingGoalsController.cs
│   │   ├── CategoriesController.cs
│   │   ├── NotificationsController.cs
│   │   ├── ReportsController.cs
│   │   ├── GamificationController.cs
│   │   ├── DevicesController.cs      # đăng ký/gỡ thiết bị nhận push (FCM)
│   │   └── Admin/
│   │       ├── AdminUsersController.cs
│   │       ├── AdminDataDeletionController.cs   # giám sát hàng đợi xoá cứng
│   │       ├── AdminProviderConfigsController.cs
│   │       └── AdminMissionsController.cs
│   ├── Middleware/
│   │   ├── ExceptionHandlingMiddleware.cs
│   │   ├── RequestLoggingMiddleware.cs
│   │   └── RateLimitingMiddleware.cs
│   ├── Configuration/
│   │   └── StartupSecretGuard.cs     # từ chối khởi động (ngoài Development) khi khóa còn giá trị mẫu, gồm OCR_SERVICE_API_KEY
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
│   │   │   ├── DeleteTransactionCommand.cs
│   │   │   ├── ScanReceiptCommand.cs (+Handler)   # ảnh → ocr-service → AI Service phân loại → receipt_scans (§2.4)
│   │   │   └── ChatCommand.cs (+Handler, Validator) # câu chat → ai-service /chat/parse → ghi / bản nháp / tổng (§2.4)
│   │   ├── Queries/
│   │   │   ├── GetTransactionListQuery.cs
│   │   │   └── GetTransactionDetailQuery.cs
│   │   ├── ReceiptConfidence.cs      # độ tin cậy lần quét, trần 0,80 (0,50 khi cần xem lại)
│   │   ├── ReceiptMemory.cs          # ShouldLearn: khi nào gửi /bill/learn
│   │   ├── ReceiptSplitter.cs        # cộng tiền theo nhóm khi tách từng món (§3.4)
│   │   └── ReceiptOptions.cs         # Receipt:SplitByItemsEnabled
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
│   │   ├── ReceiptScan.cs, ReceiptScanItem.cs, ReceiptScanLink.cs   # §4.1
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
│   │   ├── IAIServiceApi.cs, AIServiceApiContracts.cs   # Refit: /analyze, /ocr, /chat/parse, /bill/classify, /bill/learn, /bill/classify-items…
│   │   ├── OcrServiceClient.cs   # POST {OCR_SERVICE_URL}/analyze-bill (multipart "image" + header X-User-Hash)
│   │   ├── DisabledOcrServiceClient.cs  # đăng ký khi OCR_SERVICE_URL trống: IsConfigured = false
│   │   ├── UserIdHasher.cs       # SHA-256(user_id) hex thường, dùng chung cho AI Service và ocr-service
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

### 2.4 Backend điều phối AI: quét hóa đơn, chat, ghi nhớ cửa hàng (thêm 2026-10-11)

Backend là nơi **duy nhất** app gọi; ai-service và ocr-service chỉ hiểu/đọc, mọi thứ có hậu quả (ghi giao dịch, số dư,
ngân sách) nằm ở backend. Route liên quan (đều dưới `[Route("api/v1/transactions")]`, cần JWT):

| Route | Handler | Gọi ra ngoài |
|---|---|---|
| `POST /api/v1/transactions/scan-receipt` (multipart `file`) | `ScanReceiptCommandHandler` | ocr-service `/analyze-bill` → ai-service `/bill/classify` hoặc `/bill/classify-items`; không có ocr-service thì ai-service `/api/v1/ocr` |
| `POST /api/v1/transactions/chat` (`{text, financialAccountId?, clientRequestId?}`) | `ChatCommandHandler` | ai-service `/api/v1/chat/parse` |
| `POST /api/v1/transactions/parse` | `ParseNaturalLanguageCommandHandler` | (có từ Phase 10) |
| `POST /api/v1/transactions`, `PATCH /api/v1/transactions/{id}` | `CreateManualTransactionCommandHandler`, `UpdateTransactionCommandHandler` | ai-service `/bill/learn` (best-effort, xem dưới) |

**Quét hóa đơn (`ScanReceiptCommandHandler`).**
1. Chặn sớm: ảnh rỗng, quá **5 MB** (`MaxImageBytes`), hoặc không phải JPEG/PNG/WebP/HEIC/HEIF → lỗi nghiệp vụ, không gọi mạng.
2. `IOcrServiceClient.IsConfigured` quyết đường đi. `Program.cs` đăng ký `DisabledOcrServiceClient` khi `OCR_SERVICE_URL`
   trống (→ đường cũ `POST /api/v1/ocr` của ai-service, y như Phase 10 — đây là **lựa chọn cấu hình**, không phải đường dự
   phòng lúc chạy); có URL thì đăng ký `OcrServiceClient` và
   **bắt buộc** có `OCR_SERVICE_API_KEY` (thiếu thì ném lúc khởi động), header `Authorization: Bearer`, timeout 60 giây.
3. Đường ocr-service: `OcrServiceClient` gửi ảnh + `X-User-Hash = UserIdHasher.Hash(userId)`. 422 với `code = not_a_receipt`
   → `unreadable`; không có tổng tiền → `no_amount`; lỗi mạng/HTTP/JSON → `AIServiceUnavailableException` và **lần quét thất bại**
   (backend không quay sang `/api/v1/ocr`).
4. Độ tin cậy do backend tính (`ReceiptConfidence.Compute`): 0,55 nếu tổng tiền đọc từ dòng có nhãn (`total_source = "text"`),
   0,35 nếu không; +0,15 có tên cửa hàng; +0,10 có ngày; trần **0,80**; ocr-service báo `needs_review` thì trần **0,50**.
   Cùng công thức với `receipt_ocr._confidence` của ai-service, nên hai đường cho cùng con số.
5. Tách từng món nếu đủ điều kiện (§3.4); không thì phân loại cả bill qua `/bill/classify` (gửi tên OCR thô và **chữ đã lọc**).
   ai-service lỗi → danh mục để trống, lần quét **vẫn thành công**.
6. Mọi lần quét (kể cả `unreadable`/`no_amount`) ghi một dòng `receipt_scans` (không ảnh, không chữ OCR) và trả `scanId` cho app;
   app gửi lại `receiptScanId` khi bấm Lưu. Không tạo giao dịch ở bước này.

**Chat (`ChatCommandHandler`).** Text 1–500 ký tự (`ChatCommandValidator`). ai-service chỉ trả quyết định (§3.3, §3.6); backend:
- `entry` + `auto_save_ok` + là chi tiêu + backend tự chọn được ví (ví người dùng chọn, hoặc ví **duy nhất**) + slug tìm được
  danh mục hệ thống → ghi luôn qua `CreateManualTransactionCommandHandler` (`source = Manual`, có `clientRequestId` chống trùng),
  `action = "saved"`. Thiếu bất kỳ điều kiện nào → `action = "confirm"` kèm bản nháp và `missingFields` (`category`/`account`/`accounts`);
  thu nhập và chuyển tiền **luôn** qua xác nhận. Hoàn tác/sửa dùng `DELETE`/`PATCH /transactions/{id}` có sẵn.
- Câu có **nhiều khoản** (`entries` > 1): luôn `action = "confirm"`, `drafts` có đủ từng bản nháp (`draft` = bản đầu), không bao giờ ghi ngay.
- Ngày trong câu (`occurred_at`, ví dụ "hôm qua") được dùng làm `transactedAt` khi ghi và có trong bản nháp (`occurredAt`).
- **Cảnh báo trùng** (chỉ chi tiêu): có khoản Debit cùng số tiền của chính người dùng trong ±10 phút (chat) thì KHÔNG ghi ngay mà hỏi lại,
  bản nháp mang `possibleDuplicateTransactionId`. Gửi lại cùng `clientRequestId` vẫn nhận lại giao dịch cũ (không bị coi là trùng).
- `query` → `action = "summary"`: backend tự cộng từ giao dịch của chính người dùng (ai-service không thấy số liệu).
- Còn lại → `action = "reply"` (chào, ngoài chủ đề, thiếu số tiền…).

**Ghi nhớ cửa hàng khi lưu/sửa.** `ReceiptMemory.ShouldLearn` chỉ đúng khi: giao dịch `source = Receipt`, có tên cửa hàng,
danh mục là danh mục **hệ thống** (không phải danh mục tự tạo) khác `income`, và lần quét **không** bị tách. Gọi ở
`CreateManualTransactionCommandHandler` (sau khi đã lưu) và `UpdateTransactionCommandHandler` (chỉ khi đổi danh mục và giao dịch
không nối với lần quét đã tách). `AIServiceClient.LearnMerchantAsync` gọi `/bill/learn` với timeout **3 giây** và nuốt lỗi
(`ApiException`/`HttpRequestException`/`TaskCanceledException`): ghi nhớ không bao giờ làm hỏng việc lưu.

**Mã người dùng ra ngoài.** `UserIdHasher.Hash` = SHA-256 của `Guid.ToString()` dạng hex thường; cùng một hàm cho AI Service
(`user_id_hash`) và ocr-service (`X-User-Hash`), nên một người dùng có cùng mã ở cả hai nơi và cùng khóa trong bảng `bill_*`.

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
│   │   │   ├── bill.py           # POST /api/v1/bill/classify, /bill/learn, /bill/classify-items (§3.3)
│   │   │   ├── chat.py           # POST /api/v1/chat/parse (§3.3, §3.6)
│   │   │   ├── ocr.py            # POST /api/v1/ocr — ocr-service khi đặt OCR_SERVICE_URL, chỉ khi KHÔNG đặt mới dùng Tesseract tại chỗ
│   │   │   ├── stats.py          # GET /api/v1/stats
│   │   │   └── health.py         # GET /api/v1/health
│   │   └── deps.py               # Dependencies injection
│   │
│   ├── pipeline/                 # Core AI pipeline
│   │   ├── orchestrator.py       # Điều phối các stages
│   │   ├── stages/
│   │   │   ├── classifier.py     # Stage 1: Financial/Non-financial
│   │   │   ├── extractor.py      # Stage 2: Amount, merchant, date
│   │   │   ├── categorizer.py    # Stage 3: Category + confidence
│   │   │   ├── duplicate_detector.py  # Stage 4: Dedup check
│   │   │   ├── receipt_ocr.py    # đọc hóa đơn bằng Tesseract + _confidence (trần 0,80)
│   │   │   └── remote_ocr.py     # đường ocr-service của /ocr: gọi /analyze-bill, đổi về ReceiptReading
│   │   └── models/
│   │       ├── base_model.py
│   │       └── model_registry.py # Load model theo version
│   │
│   ├── bill/                     # Phân loại hóa đơn (thêm 2026-10-10 cho tách từng món)
│   │   ├── item_classifier.py    # classify_items: thừa kế → luật DB → nhóm thương hiệu → Gemini → unknown
│   │   ├── gemini.py             # GeminiClient: chỉ gửi tên món, kiểm kết quả theo schema, cache, khóa mạch 30s
│   │   ├── classifier.py         # cả bill → 1 trong 9 nhóm: bộ nhớ > thương hiệu trong tên > từ khóa tên > thương hiệu trong toàn văn > mặt hàng > từ khóa toàn văn
│   │   ├── knowledge.py          # DbBillKnowledge: nạp bill_brands/bill_keywords (TTL 60s), tra/ghi bộ nhớ cửa hàng theo user_id_hash
│   │   ├── brands.py             # thương hiệu → nhóm (bản trong code = mặc định, DB thay tại chỗ)
│   │   └── item_rules_seed.py    # bản SEED chỉ dẫn Gemini (nhóm, luật chốt, ví dụ) cho bảng bill_ai_instructions (0008)
│   │
│   ├── chat/                     # Hiểu câu chat (Phase 19–20), §3.6
│   │   ├── vocab.py              # từ khóa chat → 9 nhóm, loại thu nhập (salary, bonus…)
│   │   ├── vi_text.py            # bỏ dấu, đọc số viết bằng chữ ("bốn mươi lăm nghìn")
│   │   ├── amount.py             # số tiền tiếng lóng: 200k, 3 củ 8, 1 lít 2, 2 triệu rưỡi…
│   │   ├── nlu.py                # RasaNlu: gọi {RASA_URL}/model/parse, timeout 2s, cầu dao 30s
│   │   └── parser.py             # parse_chat: gộp Rasa + luật → intent, entry/query, auto_save_ok
│   │
│   ├── schemas/                  # Pydantic models
│   │   ├── request.py            # AnalyzeRequest, FeedbackRequest
│   │   ├── response.py           # AnalyzeResponse, PipelineResult
│   │   ├── chat.py               # ChatParseRequest/Response
│   │   └── bill.py               # BillClassify*, BillLearn*, BillClassifyItems*
│   │
│   ├── db/
│   │   ├── session.py            # SQLAlchemy session
│   │   ├── models/               # SQLAlchemy ORM models (AI DB)
│   │   │   ├── raw_sample.py
│   │   │   ├── labeled_sample.py
│   │   │   ├── model_version.py
│   │   │   ├── pipeline_request.py
│   │   │   ├── user_feedback.py
│   │   │   └── bill.py           # 12 bảng bill_* (§4.2), BILL_CHOICE_SLUGS
│   │   ├── bill_views.py         # SQL 2 view bill_unknown_merchants, bill_merchant_votes
│   │   └── repositories/
│   │       ├── sample_repo.py
│   │       └── pipeline_repo.py
│   │
│   ├── core/
│   │   ├── config.py             # Settings từ env vars
│   │   ├── security.py           # Verify internal API key từ backend
│   │   ├── anonymizer.py         # SHA-256 user_id, anonymize body
│   │   ├── enums.py              # CATEGORY_SLUGS: 11 slug hệ thống của backend
│   │   └── category_map.py       # SLUG_BY_LOCAL_CATEGORY: 9 nhóm bill/chat → slug backend (§3.7)
│   │
│   └── utils/
│       ├── provider_patterns.py  # Rule-based extraction fallback
│       └── text_utils.py         # VND amount parsing: "75k", "1.5tr"
│
├── tests/
│   ├── unit/
│   └── integration/
├── alembic/                      # DB migrations cho AI DB (0001–0008; Dockerfile chạy `alembic upgrade head` lúc khởi động)
├── rasa/                         # Dữ liệu + cấu hình Rasa NLU (§3.6); container `rasa` mount thư mục này
│   ├── config.yml, domain.yml, credentials.yml, endpoints.yml, README.md
│   ├── data/nlu.yml, data/rules.yml, data/stories.yml   # rules/stories chưa dùng
│   ├── tests/nlu_test.yml, tests/test_stories.yml
│   └── models/README.md          # file model finmate-nlu-v3.tar.gz đặt ở đây, KHÔNG nằm trong git (.gitignore)
├── requirements.txt
└── Dockerfile
```

**Cập nhật 2026-10-11.** Cây trên trước thiếu các route `chat`, `ocr`, `stats`, gói `app/chat/`, `remote_ocr.py`,
`category_map.py`, `schemas/chat.py`, `schemas/bill.py`, model `bill.py` và thư mục `rasa/` — đã bổ sung theo code hiện tại.
Thư mục `utils/` thực tế còn có `number_words.py`, `amount_slang.py` (Phase 10).

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

> **Cập nhật 2026-10-11.** Trong mọi ví dụ dưới đây, `"sha256-of-user-id"` chỉ là chỗ giữ chỗ: giá trị thật phải là SHA-256 dạng
> hex **đúng 64 ký tự** (schema ép `min_length = max_length = 64` ở `/analyze`, `/feedback`, `/ocr`, `/chat/parse`, `/bill/*`);
> gửi chuỗi khác độ dài thì nhận 422.

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

**OCR hóa đơn (Backend → AI Service) — thêm ở Phase 10:**
```
POST /api/v1/ocr
Authorization: Bearer {INTERNAL_API_KEY}
Content-Type: multipart/form-data

  file           (binary)  ảnh hóa đơn, tối đa 5MB, chỉ JPEG/PNG/WebP/HEIC
  user_id_hash   (text)    SHA-256 của user_id
```
```json
{
  "ocr_result": "success",
  "extraction": {
    "amount_cents": 110000,
    "transaction_type": "debit",
    "merchant_name": "WINMART+ NGUYEN TRAI",
    "description": "WINMART+ NGUYEN TRAI",
    "transacted_at": "2026-09-12T12:45:00Z",
    "balance_after_cents": null,
    "confidence": 0.8
  },
  "categorization": { "category_slug": "shopping", "confidence": 0.8 }
}
```

`ocr_result` có ba giá trị: `success`, `no_amount` (đọc được chữ nhưng không thấy tổng tiền),
`unreadable` (ảnh không ra chữ nào). Ba trạng thái dẫn tới ba lời nhắc khác hẳn nhau cho người
dùng — gộp thành một cờ thành/bại thì client chỉ còn cách nói "thử lại" cho cả ba. AI Service
không phản hồi là chuyện thứ tư nữa, và nó đi ra dưới dạng lỗi HTTP.

**Ảnh không được lưu ở đâu** — không đĩa, không blob store, không AI DB. Chỉ TEXT đọc được
mới vào `raw_samples` (source `receipt`) và đã qua `anonymizer`. Ảnh hóa đơn là một loại dữ
liệu cá nhân mới; dựng chỗ chứa nó phải là một quyết định riêng, không phải hệ quả phụ của
việc làm OCR.

`confidence` của luồng này **luôn dưới 0,85**. Docx phương thức 3 yêu cầu người dùng rà soát
trước khi lưu, mà 0,85 lại là mốc xác nhận một chạm của Flow 1 — trả về một con số nằm trong
vùng đó sẽ mâu thuẫn với chính yêu cầu kia.

**Stats (Backend → AI Service) — thêm ở Phase 8:**
```json
GET /api/v1/stats
Authorization: Bearer {INTERNAL_API_KEY}

{
  "models": [
    {"stage": "classifier", "version": "1.0.0", "trained_at": "...",
     "accuracy": 1.0, "macro_f1": 1.0, "evaluated_on_split": "test"},
    {"stage": "categorizer", "version": null}
  ],
  "raw_sample_count": 236,
  "labeled_sample_count": 236,
  "unlabeled_sample_count": 0,
  "split_counts": {"train": 168, "val": 38, "test": 30},
  "last_training_job": {"stage": "categorizer", "status": "succeeded", "sample_count": 111,
                        "started_at": "...", "finished_at": "...", "error_message": null},
  "pending_feedback_count": 4
}
```

Phục vụ `GET /api/v1/admin/ai-stats`. Chỉ số đếm ở mức hệ thống, không có gì gắn với một
người dùng cụ thể. `version: null` nghĩa là stage đó chưa promote model nào và đang chạy bằng
luật — trạng thái hợp lệ, không phải lỗi.

Route này **không** trả được "AI đoán đúng bao nhiêu phần trăm ngoài đời": AI DB thấy dự đoán
của chính nó nhưng không bao giờ thấy người dùng đã xác nhận hay sửa gì. Tỉ lệ người dùng sửa
lại danh mục do backend tự tính từ `ai_results` × `transactions`, và màn hình quản trị gộp hai
nửa lại. AI Service chết thì backend vẫn trả 200 với nửa của mình.

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

**Phân loại từng món (Backend → AI Service) — thêm 2026-10-10:**
```json
POST /api/v1/bill/classify-items
Authorization: Bearer {INTERNAL_API_KEY}
{
  "items": [{"name": "Kệ để giày 3 tầng", "parent": null}, {"name": "Thạch dừa", "parent": 0}],
  "merchant_name": "Cửa hàng ABC",
  "user_id_hash": "sha256-of-user-id",
  "allow_ai": true
}
```
```json
{
  "items": [
    {"index": 0, "category_slug": "shopping", "source": "ai"},
    {"index": 1, "category_slug": "shopping", "source": "inherit"}
  ],
  "ai_used": true
}
```
- **Không có số tiền** trong request, và `user_id` thật không rời backend (`user_id_hash` là SHA-256 hex đúng 64 ký tự).
- Giới hạn: 1–60 món, mỗi tên tối đa 200 ký tự; `parent` phải là chỉ số của một món **đứng trước** (phụ kiện
  `+` thừa kế nhóm món chính). `source` ∈ `rule | cache | inherit | merchant | ai | unknown`; `category_slug` null khi `unknown`.
- Thứ tự quyết (cập nhật 2026-10-12): thừa kế → luật/từ khóa trong DB → **khớp gần đúng** từ khóa sản phẩm (OCR sai một chữ, chỉ cặp chữ hay
  nhầm như m/n, u/v, hoặc thiếu/thừa một chữ; từ khóa ≥ 7 ký tự) → **bộ nhớ tên món** `bill_item_cache` (câu trả lời Gemini đã lưu, dùng chung,
  đọc được cả khi chưa `allow_ai`) → Gemini (chỉ khi `allow_ai`, có khóa, và bill không phải bill y tế; câu trả lời hợp lệ được ghi vào
  `bill_item_cache`, câu đầu tiên thắng) → nhóm mặc định **của thương hiệu** (chỉ món Gemini không quyết được) → `unknown`.
  Gemini đứng TRƯỚC nhóm mặc định cửa hàng vì cửa hàng tiện lợi bán cả đồ ăn lẫn khăn ướt. Lỗi/quá hạn Gemini không làm lỗi route.
- `ocr-service` `POST /analyze-bill` (nội bộ) trả thêm `items: [{name, amount, parent, is_discount}]` và `items_ok`
  (`amount` là số nguyên đồng, **âm** với giảm giá; `items_ok` chỉ true khi tổng các dòng bằng đúng tổng bill).
- `POST /api/v1/transactions/scan-receipt` (app → backend) trả thêm `groups: [{categorySlug, amountCents, items:[{name, amountCents}]}]`
  (null khi bill không tách), `needsReview` (bool: OCR chưa chắc, hoặc phần chưa rõ nhóm ≥ 20% tổng bill, hoặc cờ tách bật mà các dòng
  không khớp tổng) và `possibleDuplicateTransactionId` (cả bill khi không tách; từng nhóm trong `groups` khi tách): một khoản chi cùng
  số tiền của chính người dùng trong ±3 giờ. Chỉ để cảnh báo; `POST /api/v1/transactions` nhận thêm `receiptGroup` (cùng `receiptScanId`);
  `PATCH /api/v1/users/me` nhận thêm `shareReceiptItemsWithAi` (bool, bỏ trống = giữ nguyên). Mọi thay đổi chỉ **thêm** trường.

**Chat (Backend → AI Service) — thêm Phase 19–20, ghi vào tài liệu 2026-10-11:**
```json
POST /api/v1/chat/parse
Authorization: Bearer {INTERNAL_API_KEY}
{ "text": "ăn phở 45k", "user_id_hash": "sha256-of-user-id" }
```
```json
{
  "intent": "add_expense",
  "reply_text": null,
  "entry": {
    "kind": "expense", "amount_cents": 45000, "description": "ăn phở", "merchant": null,
    "category_slug": "food", "income_type": null, "auto_save_ok": true, "to_others": false, "occurred_at": null
  },
  "entries": [ /* mọi khoản trong câu; entry = entries[0]; câu một khoản thì entries = [entry] */ ],
  "query": null,
  "nlu": { "source": "rasa", "intent": "add_expense", "confidence": 0.98 },
  "unclear_reason": null,
  "processing_ms": 12
}
```
- `text` tối đa 500 ký tự; `user_id_hash` phải là SHA-256 hex đúng 64 ký tự (ví dụ trên chỉ là chỗ giữ chỗ). **Không lưu gì, không giữ trạng thái**, không ghi nội dung câu hay số tiền vào log.
- `intent` ∈ `add_expense | add_income | transfer_money | ask_spending | greet | goodbye | thanks | confirm | deny | cancel |
  out_of_scope | needs_amount | empty`. Có `entry` với ba intent ghi giao dịch, có `query` với `ask_spending`, còn lại chỉ `reply_text`.
- `entry.kind` ∈ `expense | income | transfer`. Chi tiêu: `category_slug` là slug backend hoặc `null` (backend hỏi người dùng);
  thu nhập: luôn `"income"` + `income_type` (`salary | bonus | gift | side_income | refund | other_income`); chuyển tiền: `null`.
  "Chuyển cho người khác / trả nợ" trả về `kind = "expense"`, `category_slug = "other"`, `to_others = true`.
- `auto_save_ok` chỉ `true` khi: chi tiêu, đã có slug, số tiền dưới `CHAT_AUTO_SAVE_BELOW` (mặc định 2.000.000 đồng; 0 = không bao giờ),
  và Rasa (nếu có) trả `add_expense` với độ chắc ≥ 0,9. Đây chỉ là **gợi ý**; backend còn tự kiểm ví và danh mục (§2.4).
- `entries` (thêm 2026-10-12): câu có nhiều số tiền ("ăn cơm 20k, uống nước 10k") tách thành nhiều khoản; chỉ tách khi **cả hai vế** có số tiền
  và vế sau có chữ mô tả, nên "1,5tr", "1.250.000", "mua 2 lít sữa 50k", "ăn 20k + 30k" vẫn là một khoản. Câu nhiều khoản không khoản nào
  `auto_save_ok` và đọc loại (chi/thu) theo luật từng vế. `occurred_at`: "hôm nay/qua/kia", "sáng/trưa/chiều/tối nay|qua", "ngày 5/10" (giờ Việt Nam,
  không nhận ngày tương lai; số trần "5/10" không nhận). Khóa `education` và `family` của từ vựng chat là slug backend dùng thẳng.
- `query`: `metric` ∈ `expense | income | balance`, `period_key`/`period_label` (mặc định "tháng này"), `start`/`end` nửa mở `[start, end)`
  theo giờ Việt Nam; `category_slug` chỉ khi `metric = expense`. Tổng do **backend** tính.
- `nlu.source` ∈ `rasa | rules`; `unclear_reason` ∈ `low_confidence | disagree | not_understood | null` (chưa lưu ở đâu).

**Phân loại cả bill (Backend → AI Service) — thêm Phase 21, ghi vào tài liệu 2026-10-11:**
```json
POST /api/v1/bill/classify
Authorization: Bearer {INTERNAL_API_KEY}
{ "user_id_hash": "sha256-of-user-id", "merchant_name": "WINMART+ NGUYEN TRAI", "lines": ["Sua tuoi 1L", "Tong cong 110.000"] }
```
```json
{ "category_slug": "food", "local_category": "groceries", "confidence": 0.8, "source": "brand", "processing_ms": 3 }
```
- `user_id_hash`: SHA-256 hex đúng 64 ký tự (ví dụ là chỗ giữ chỗ).
- `lines` là chữ **đã lọc** dữ liệu cá nhân (do ocr-service lọc), tối đa 200 dòng, mỗi dòng cắt còn 200 ký tự; `merchant_name` ≤ 200 ký tự.
- Thứ tự (`classify_category_detailed`): **bộ nhớ cửa hàng của chính người dùng** (bảng mới `bill_user_merchant_choice` trước, bảng
  cũ `bill_user_merchant_category` sau) → thương hiệu trong tên cửa hàng (0,8) → từ khóa trong tên (0,75) → thương hiệu nằm trong toàn
  văn, vd. website/địa chỉ (0,7) → mặt hàng (0,7/0,6) → từ khóa toàn văn (0,6) → `none`. Nhớ từ bảng mới (slug backend) thì trả nguyên
  slug đó, `local_category = null`, `source = "memory"`.
- `source` ∈ `memory | brand | keyword | product | model | none` theo schema, nhưng **hiện không bao giờ ra `model`**: route không truyền
  model học nào vào bộ phân loại (cập nhật 2026-10-11). Không nhận ra (`none`) → `category_slug = null`, `confidence = 0`:
  backend hỏi người dùng thay vì ghi `other` cho có. `confidence` bị chặn trần **0,80** (luồng hóa đơn luôn cần người xem lại).
- `local_category` giữ nhóm 9-nhóm (phân biệt `dining`/`groceries` mà backend gộp thành `food`, §3.7). Không ghi DB, không log nội dung.

**Ghi nhớ cửa hàng (Backend → AI Service) — thêm Phase 22, ghi vào tài liệu 2026-10-11:**
```json
POST /api/v1/bill/learn
Authorization: Bearer {INTERNAL_API_KEY}
{ "user_id_hash": "sha256-of-user-id", "merchant_name": "Pizza Hut", "category_slug": "food" }
```
```json
{ "learned": true }
```
- `user_id_hash`: SHA-256 hex đúng 64 ký tự (ví dụ là chỗ giữ chỗ); `merchant_name` 1–200 ký tự.
- `category_slug` phải thuộc `BILL_CHOICE_SLUGS` = 11 slug backend **trừ** `income` (sai → 422). Ghi/ghi đè một dòng
  `bill_user_merchant_choice` theo `(user_id_hash, merchant_key)`; `merchant_key` = tên bỏ dấu, chữ thường, gọn khoảng trắng.
- `learned = false` khi tên cửa hàng không dùng làm khóa được (không phải lỗi). Log chỉ ghi `learned`, không ghi tên hay danh mục.
- Backend gọi route này theo kiểu best-effort, timeout 3 giây (§2.4).

**`POST /api/v1/ocr` qua ocr-service (cập nhật 2026-10-11).** Khi ai-service có `OCR_SERVICE_URL`, route `/ocr` không chạy
Tesseract mà gọi `{OCR_SERVICE_URL}/analyze-bill` (`app/pipeline/stages/remote_ocr.py`, Bearer `OCR_SERVICE_API_KEY`, header
`X-User-Hash`, timeout `OCR_SERVICE_TIMEOUT_SECONDS` mặc định 30 giây) và đổi kết quả về đúng contract `/ocr` ở trên (không đổi
trường nào). Ngày vẫn đọc từ các dòng đã lọc bằng bộ đọc ngày cũ; danh mục 9 nhóm quy về slug qua `SLUG_BY_LOCAL_CATEGORY`, độ tin
cậy danh mục trần 0,80, bill bị đánh dấu cần xem lại thì độ tin cậy trích xuất trần 0,50. ocr-service trả 422 `not_a_receipt` →
`unreadable`; lỗi khác → 503. Đặt URL mà thiếu `OCR_SERVICE_API_KEY` cũng → 503. Có URL thì route **không bao giờ** quay về
Tesseract: Tesseract chỉ chạy khi ai-service **không** có `OCR_SERVICE_URL` (lựa chọn cấu hình, không phải dự phòng lúc chạy;
compose đặt URL cho cả backend lẫn ai-service nên bộ chạy mặc định không dùng Tesseract; **chưa quyết định gỡ Tesseract**). Đây là đường **thứ hai**; đường backend
điều phối (§2.4) không đi qua route này.

### 3.4 Tách hóa đơn theo từng món (thêm 2026-10-10)

Mục tiêu: một bill có hàng thuộc nhiều nhóm (mỹ phẩm và đồ ăn) được tách thành nhiều nhóm kèm số tiền từng
nhóm; bấm Lưu thì **mỗi nhóm một giao dịch**, cùng nối về một lần quét. Thiết kế đầy đủ:
`docs/superpowers/specs/2026-10-09-receipt-item-split-design.md`.

- **Cờ và đồng ý.** Chỉ chạy khi `Receipt:SplitByItemsEnabled` (mặc định **tắt**) bật, `ocr-service` báo `items_ok`,
  bill có ≥ 2 món chính (không tính phụ kiện) và không có phụ kiện mồ côi. Cổng này kiểm **trước** khi đọc người dùng và
  trước lời gọi AI. Đồng ý chia sẻ với AI đọc từ DB mỗi lần quét (`users.share_receipt_items_with_ai`, mặc định tắt).
- **Backend cộng tiền, AI chỉ gắn nhóm.** `ReceiptSplitter` gom món theo nhóm, chia giảm giá chung theo tỷ lệ (phần dư
  vào nhóm lớn nhất theo tiền gốc), gộp phụ phí (VAT, phí dịch vụ…) vào nhóm lớn nhất, và trả kết quả chỉ khi **tổng
  các nhóm bằng đúng tổng bill**. Chỉ tách khi có **ít nhất hai nhóm có danh mục thật**; món `unknown` thành một nhóm
  riêng (cần xem lại) chỉ khi đã có ≥ 2 nhóm thật. Nhóm cấp trên `categorySlug` = nhóm lớn nhất **có** danh mục.
- **Mọi lỗi → đường cũ (một nhóm), không đổi hành vi.** `items_ok=false`, dưới 2 món chính, tổng lệch, ai-service lỗi,
  trả sai dạng, hoặc dưới 2 nhóm thật → quét chạy y như trước; quét không bao giờ thất bại vì tính năng này.
- **Lưu.** App gọi `POST /transactions` một lần cho mỗi nhóm, mỗi nhóm có `clientRequestId` cố định suy ra từ
  `scanId` + nhóm (uuid v5), nên mở lại hoặc thử lại được phát lại (200) thay vì ghi trùng. Liên kết quét ↔ giao dịch
  qua `receipt_scan_links` (khóa duy nhất `(scan_id, group_category_slug)`; giao dịch chỉ nối tối đa một lần quét).
  Bill **chưa tách** luôn nối dưới một khóa cố định (`category_slug` của chính lần quét); chỉ bill đã tách mới dùng
  `receiptGroup` do app gửi (đã chuẩn hóa: cắt khoảng trắng; rỗng, có ký tự điều khiển hoặc quá 100 ký tự thì rơi về dự phòng).
- **Học.** Bill đã tách **không** dạy bộ nhớ cửa hàng (cả khi lưu lẫn khi sửa danh mục về sau): một cửa hàng bán
  hai loại hàng không thuộc một danh mục.
- **Nhận biết "đã tách".** `receipt_scan_items` có ≥ 2 nhóm khác nhau, hoặc `receipt_scans.category_source = 'items'`.

### 3.5 OCR Service — `ocr-service/` (thêm 2026-10-11)

Dịch vụ Python FastAPI riêng (image `python:3.10-slim`, PaddleOCR + VietOCR ONNX), **không mở cổng** ra máy chủ trong
`docker-compose.yml`; chỉ backend (và ai-service `/ocr`) gọi qua mạng nội bộ. Một worker uvicorn vì bộ nhớ lượt quét (`scan_id`)
nằm trong RAM của tiến trình.

```
ảnh (≤ 10 MB) ─► verify ảnh ─► quality.py (too_dark/too_bright/blurry/ok)
             ─► ocr.read_boxes: crop.py cắt tờ phiếu khỏi nền → PaddleOCR (lang=vi) → VietOCR đọc lại 8 dòng đầu (giữ dấu)
             ─► extract.receipt_score: không giống hóa đơn (và không phải ảnh chụp màn hình app) → 422 not_a_receipt
             ─► extract.analyze: tổng tiền, tên cửa hàng, nhóm 9-nhóm, needs_review   (app_screens.py cho ảnh chụp màn hình app)
             ─► privacy.sanitize_lines: bỏ/che SĐT, email, tên người, địa chỉ, số tài khoản → `lines` đã lọc
             ─► dates.find_date (đọc trên chữ gốc, chỉ trả ngày giờ)   ─► items.extract_items → `items`, `items_ok`
             ─► JSON (chữ gốc và ảnh không bao giờ rời service)
```

**Endpoint** (`ocr-service/main.py`):

| Route | Xác thực | Việc |
|---|---|---|
| `GET /health` | không | `{"status": "ok"}` — sống ngay khi khởi động, model nạp song song |
| `POST /analyze-bill` | Bearer `OCR_SERVICE_API_KEY` + `X-User-Hash` (tuỳ chọn, hex 16–64 ký tự) | multipart `image`; form `use_ai`, `share_data` (mặc định `false`) |
| `POST /merchant-category` | Bearer + `X-User-Hash` | lưu "cửa hàng → nhóm" vào bộ nhớ của ocr-service (bảng cũ `bill_user_merchant_category`) và dataset; **backend hiện không gọi** |
| `POST /admin/reload-knowledge` | header `X-Admin-Token` = `FINMATE_ADMIN_TOKEN` (chưa đặt = tắt hẳn, 403) | nạp lại thương hiệu/từ khóa từ DB không cần khởi động lại |

Khóa so bằng `hmac.compare_digest`; **chưa đặt `OCR_SERVICE_API_KEY` thì từ chối mọi request (503)**, không mở.

**`/analyze-bill` trả** (các trường backend dùng): `merchant` (tên OCR thô), `merchant_display` (tên đẹp: người dùng đã chốt >
AI > tự làm sạch), `total_amount` (đồng), `total_source` (`text` khi đọc từ dòng tổng có nhãn, `items` khi cộng từ dòng hàng),
`transacted_at` (ISO, giờ +07:00), `lines` (chữ **đã lọc**), `category` (9 nhóm) + `category_source` + `category_confidence`,
`needs_review`, `quality`, `receipt_score`, `scan_id`, `items: [{name, amount, parent, is_discount}]`, `items_ok`. Không giống hóa đơn
hoặc không có chữ → 422 `{"code": "not_a_receipt"}`; ảnh > 10 MB hoặc không phải ảnh → 400; lỗi khác → 500.
Backend (`OcrServiceClient`) chỉ gửi `image` nên `use_ai` và `share_data` (ghi bill lạ vào `bill_unknown_scans`) **không bật** trên
đường backend hiện tại. Lưu ý `use_ai` (`ai_refine.py`): khác đường `classify-items` của ai-service, nó gửi **tên cửa hàng, các dòng
mặt hàng đã lọc và tổng tiền** ra Gemini, hoặc ra một API tương thích OpenAI khi `FINMATE_AI_PROVIDER=openai`
(`FINMATE_AI_BASE_URL`, `FINMATE_AI_API_KEY`, `FINMATE_AI_MODEL`). **Không được bật** cờ này trên đường backend khi chưa rà soát riêng tư.

**Module** (một dòng mỗi file):
- `ocr.py` — PaddleOCR (đọc chữ + hộp tọa độ), làm thẳng ảnh nghiêng, đọc lại đầu phiếu bằng VietOCR (`FINMATE_VIETOCR=0` để tắt).
- `vietocr_onnx.py` — VietOCR seq2seq chạy qua onnxruntime (không cần PyTorch), model trong `models/vietocr/`.
- `crop.py` — cắt vùng tờ hóa đơn khỏi nền (bàn, tay…) trước OCR.
- `quality.py` — đánh giá/chỉnh độ sáng, đo mờ: `ok | too_dark | too_bright | blurry`.
- `extract.py` — luật bóc tổng tiền, tên cửa hàng, nhóm, `needs_review`, `receipt_score`.
- `dates.py` — ngày giao dịch trên phiếu (bỏ kỳ cước/hạn thanh toán, bỏ ngày tương lai, lấy ngày muộn nhất còn lại).
- `privacy.py` — `sanitize_lines`: bỏ dòng nhãn cá nhân, che email/SĐT/số tài khoản.
- `items.py` — tách từng món (tên + giá, giảm giá âm, phụ kiện `parent`) từ hộp chữ; `ok` chỉ khi cộng đúng tổng.
- `merchant_name.py` — làm đẹp tên cửa hàng (IN HOA, dính chữ, ký tự rác).
- `brands.py`, `chat_vocab.py` — thương hiệu/từ khóa trong code (mặc định khi DB trống).
- `app_screens.py` — ảnh chụp màn hình app (chuyến xe, thanh toán dịch vụ, danh sách đơn TMĐT).
- `text_clean.py` — sửa dấu lạ mà mô hình Latin gán nhầm cho tiếng Việt.
- `memory.py` / `storage_pg.py` — bộ nhớ cửa hàng và dataset: file cạnh code, hoặc PostgreSQL khi có `FINMATE_DATABASE_URL`.
- `knowledge_pg.py` — nạp `bill_brands`/`bill_keywords` từ DB lúc khởi động (DB trống thì giữ bản trong code), ghi `bill_unknown_scans`.
- `ai_refine.py` — Gemini (tuỳ chọn, chỉ khi `use_ai`) chuẩn hóa tên/nhóm từ chữ đã lọc.
- `category_model.py`, `train_category_model.py`, `training_pg.py`, `promotion_pg.py`, `datasets_pg.py`, `db_setup.py`, `seed_knowledge_to_pg.py`
  — script huấn luyện/nạp dữ liệu chạy tay (model TF-IDF + logistic chỉ dùng khi qua cổng chất lượng; gom phiếu bầu thành thương hiệu).

**Khởi động.** `lifespan` chạy `warm_up()` trong một thread nền: nạp PaddleOCR và VietOCR trước, vì lần quét đầu nạp model mất
vài chục giây — quá timeout 30 giây của ai-service. Model PaddleOCR đã được tải sẵn vào image lúc build (`Dockerfile`).

**Dữ liệu.** ocr-service **không có DB riêng**: compose đặt `FINMATE_DATABASE_URL` trỏ tới `finmate_ai` (cùng DB với ai-service) và
chờ ai-service healthy (ai-service chạy `alembic upgrade head` lúc khởi động, nên bảng `bill_*` đã có). Schema do alembic của
ai-service quản lý. ocr-service đọc `bill_brands`, `bill_keywords`, `bill_user_merchant_category`; ghi `bill_user_merchant_category`,
`bill_raw_samples`/`bill_labeled_samples` (qua `/merchant-category`), `bill_unknown_scans` (khi `share_data`), và các bảng
`bill_training_jobs`/`bill_model_versions`/`bill_evaluation_*`, `bill_brands` (nguồn `vote`) qua script chạy tay.
Không có `FINMATE_DATABASE_URL` (chạy ngoài docker) thì dùng file cạnh code.

### 3.6 Chat và Rasa NLU (thêm 2026-10-11)

`POST /api/v1/chat/parse` (contract ở §3.3) = **Rasa (tuỳ chọn) + luật**, trong `app/chat/`. Rasa và route này được ghi vào kiến trúc
như một phần của stack với sự đồng ý của chủ dự án ngày 2026-10-11.

- **Rasa chỉ hiểu câu**: trả ý định (10 intent: `add_expense, add_income, transfer_money, ask_spending, greet, goodbye, confirm,
  deny, cancel, out_of_scope`) và thực thể (`amount`, `merchant`). Rasa **không** quyết ghi hay không, chỉ nhận đúng câu chữ
  (`{"text": ...}`, không có mã người dùng), không nối DB nào (compose **không** mount `endpoints.yml` vào container `rasa`, nên Rasa dùng tracker mặc định trong bộ nhớ); ai-service quyết intent cuối cùng, backend quyết mọi hậu quả (§2.4).
- **Container `rasa`** (image `rasa/rasa:3.6.21`, `run --enable-api -m models/finmate-nlu-v3.tar.gz --port 5005`), không mở cổng ra
  máy chủ. Mount `ai-service/rasa/{models,config.yml,domain.yml,data,tests}`. Pipeline DIET 100 epoch + FallbackClassifier 0,4
  (`ai-service/rasa/config.yml`). Model `ai-service/rasa/models/finmate-nlu-v3.tar.gz` **không nằm trong git** (`.gitignore`);
  lệnh huấn luyện lại nằm trong comment của service `rasa` ở `docker-compose.yml`.
- **Cách ai-service gọi** (`app/chat/nlu.py`): `POST {RASA_URL}/model/parse`, timeout 2 giây. Bất kỳ lỗi nào → trả `None` (dùng luật),
  cảnh báo tối đa mỗi 60 giây, và **cầu dao 30 giây** không gọi lại Rasa. `RASA_URL` trống → chỉ dùng luật.
- **Gộp Rasa với luật** (`app/chat/parser.py`): Rasa chắc dưới 0,6 → bỏ qua; câu có số tiền mà Rasa bảo `out_of_scope` dưới 0,75 →
  theo luật; Rasa nói thu nhập/chuyển tiền nhưng luật thấy chi tiêu **và** câu có từ khóa chi tiêu rõ → theo luật (Rasa hay quá tự
  tin với từ lạ); Rasa và luật bất đồng loại giao dịch → Rasa chỉ thắng khi chắc ≥ 0,97; câu hỏi (có "?", "… không", "có nên…") không
  phải câu hỏi tổng → `out_of_scope`. `auto_save_ok` cần Rasa (nếu có) `add_expense` ≥ 0,9. Cụm chi tiêu không kèm số tiền ("ăn cơm",
  "mua nước") trả `needs_amount` kể cả khi không có Rasa (có động từ chi tiêu hoặc từ danh mục; câu hỏi đã bị chặn trước). Các mô-đun
  `app/chat/clauses.py` (tách khoản) và `app/chat/dates.py` (ngày) thêm 2026-10-12.
- **Số tiền và danh mục** luôn do luật: `amount.py` (Rasa chỉ giúp khi luật không thấy số), `vocab.py` (9 nhóm). Khi từ vựng chat
  không ra nhóm, route thử từ điển cửa hàng của pipeline thông báo (`categorize_by_rules`, chỉ nhận khớp độ chắc ≥ 0,90, không nhận `income`).

### 3.7 Hai bộ danh mục và chỗ dùng (thêm 2026-10-11)

| Bộ | Giá trị | Dùng ở |
|---|---|---|
| **11 slug backend** (`CATEGORY_SLUGS`, `app/core/enums.py`; do `CategorySeeder` của backend seed) | `food, transport, shopping, education, housing, bills, entertainment, health, family, income, other` | mọi thứ ra khỏi ai-service về backend: `/analyze`, `/ocr`, `/chat/parse`, `/bill/classify`, `/bill/classify-items`; `bill_user_merchant_choice` (trừ `income`) |
| **9 nhóm bill/chat** (`app/bill/classifier.py` `CATEGORIES`; `app/chat/vocab.py`; ocr-service) | `dining, transport, shopping, groceries, housing, utilities, healthcare, entertainment, other` | luật/thương hiệu/từ khóa trong `bill_brands`, `bill_keywords`; ocr-service `category`; `bill_labeled_samples`; bảng cũ `bill_user_merchant_category` |

Quy đổi duy nhất là `SLUG_BY_LOCAL_CATEGORY` (`app/core/category_map.py`), dùng ở `/bill/classify`, `/chat/parse` và `remote_ocr.py`:

| 9 nhóm | → slug backend |
|---|---|
| `dining`, `groceries` | `food` |
| `shopping` | `shopping` |
| `transport` | `transport` |
| `housing` | `housing` |
| `utilities` | `bills` |
| `healthcare` | `health` |
| `entertainment` | `entertainment` |
| `other` | `other` |

Phép quy đổi **mất thông tin** (`dining` và `groceries` cùng thành `food`) và không có nhóm 9-nhóm nào ra `education`/`family`/`income`.
Vì vậy bộ nhớ cửa hàng mới `bill_user_merchant_choice` (alembic 0007) lưu **nguyên slug backend** người dùng đã chọn (không mất gì),
còn bảng cũ `bill_user_merchant_category` giữ 9 nhóm; `/bill/classify` tra bảng mới trước, bảng cũ sau. Slug lạ làm giao dịch mất
danh mục một cách im lặng (backend tra `GetSystemBySlugAsync`), nên mọi đầu ra đều được lọc lại theo `CATEGORY_SLUGS`.

**Bộ phân loại chung chat + món trên bill (thêm 2026-10-12).** `app/core/category_resolver.py` gộp ba nguồn từ khóa từng tách rời
thành một chỉ mục `từ khóa → slug backend`: từ khóa sản phẩm của bill (`classifier._PRODUCT_KEYWORDS`, 9 nhóm đổi sang slug, vẫn nạp lại từ DB trong
tối đa 60 giây: `knowledge.apply_rows` gọi `invalidate()`), từ vựng chat (`vocab.CHAT_KEYWORDS`, phần chi tiêu) và từ điển `merchant_categories.json`
(bỏ `income`). `resolve(text, *, fuzzy=True) -> Resolution(slug|None, source)`: khớp từng từ (đã bỏ dấu), **từ khóa dài nhất thắng**, cùng độ dài thì slug
cụ thể hơn thắng (`family, education, health, housing, bills, entertainment, transport, shopping, food, other`), nên sữa bột và tã là `family`;
không khớp thì khớp gần đúng cho lỗi OCR một chữ (cặp chữ hay nhầm hoặc thiếu/thừa một chữ; từ khóa ≥ 7 ký tự; thắng cả từ khóa chính xác ngắn hơn);
không biết thì `None`, không ép `other`. Chat gọi với `fuzzy=False` (câu chat thường bị khớp gần đúng nhầm) và kiểm tra tên danh mục người dùng gõ
("giáo dục") trước. Chat còn tra bảng `bill_item_cache` cho khoản chưa rõ nhóm, chỉ **gợi ý** (`auto_save_ok = false`). Đo trên 66 cụm: cùng danh mục ở chat và bill
31/66 → 65/66 (`scripts/eval_flows.py --consistency`, `docs/evaluation.md`). **Không gộp:** phân loại cả bill (thương hiệu, từ khóa tên cửa hàng, bỏ phiếu theo dòng),
bảng DB `bill_brands`/`bill_keywords` (vẫn 9 nhóm), và bản sao bảng từ khóa trong ocr-service.

**Giới hạn đã biết (ghi 2026-10-11, đối chiếu code):**
- **Hai bộ nhớ cửa hàng không nói chuyện với nhau.** `/bill/learn` chỉ ghi `bill_user_merchant_choice`, và chỉ `/bill/classify` của
  ai-service đọc bảng đó. `category` và `merchant_display` ("tên người dùng đã chốt") của ocr-service chỉ đọc bảng cũ
  `bill_user_merchant_category`; trên đường backend không ai ghi bảng cũ (backend không gọi `/merchant-category`), nên view
  `bill_merchant_votes` và việc gom phiếu thành thương hiệu (`promotion_pg.py`) không có dữ liệu mới. Route `/api/v1/ocr` của ai-service
  (qua `remote_ocr`) dùng `category` của ocr-service, tức là cũng không thấy lựa chọn đã học.
- **Không có timeout riêng cho các lời gọi Refit** `/chat/parse`, `/bill/classify`, `/bill/classify-items`: `Program.cs` không đặt
  `Timeout` cho client Refit nên áp mặc định 100 giây của `HttpClient`. Chỉ `/bill/learn` có 3 giây và lời gọi ocr-service có 60 giây.
- **Tên cửa hàng lệch giữa lúc phân loại và lúc học.** Phân loại gửi tên OCR thô (`MerchantName ?? MerchantDisplay`), còn học gửi tên
  cửa hàng của giao dịch đã lưu (thường là tên hiển thị hoặc tên người dùng sửa). Hai tên chuẩn hóa ra hai `merchant_key` khác nhau thì
  lựa chọn đã học không bao giờ khớp lần quét sau.
- **Kiến thức trong DB cần seed tay** trên DB mới (xem §4.2).

---

## 4. Database Architecture

### 4.1 Backend DB — 25 bảng, 6 domain groups

```
Auth & Identity:
  users, refresh_tokens, device_tokens, audit_logs, data_deletion_requests

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

Receipt scan (thêm 2026-10-10):
  receipt_scans, receipt_scan_items, receipt_scan_links
```

**Cập nhật 2026-10-10.** Tiêu đề cũ ghi "20 bảng" nhưng danh sách liệt kê 22 tên — con số cũ đếm sai; cộng 3 bảng
mới thành 25. Ba bảng mới (EF migration `AddReceiptScans`, `AddReceiptScanItemsAndLinks`):
- `receipt_scans` — kết quả **bóc tách** của một lần quét (số tiền, cửa hàng, ngày, nhóm AI đoán, độ tin cậy). Không lưu ảnh,
  không lưu chữ OCR. Cột `transaction_id` cũ đã **bỏ**, thay bằng `receipt_scan_links`.
- `receipt_scan_items` — từng món của lần quét đã tách (tên ≤ 120 ký tự, số tiền gốc, âm với giảm giá, nhóm, nguồn,
  vị trí món cha). Xóa lần quét thì xóa món. Chỉ có dòng khi bill thực sự được tách.
- `receipt_scan_links` — nối giao dịch với lần quét: `transaction_id` là khóa chính, duy nhất `(scan_id, group_category_slug)`;
  xóa hẳn giao dịch chỉ gỡ liên kết. Migration chép các liên kết cũ trước khi bỏ cột.
- `users.share_receipt_items_with_ai` (cột mới, `NOT NULL DEFAULT false`) — đồng ý cho AI hỗ trợ phân loại tên món.

### 4.2 AI DB — 25 bảng (không tính `alembic_version`) + 2 view đã tạo, 3 bảng hoãn

```
Dataset:        raw_samples, labeled_samples, sample_splits, provider_patterns
Model:          model_versions, training_jobs
Evaluation:     evaluation_runs, evaluation_category_metrics, evaluation_predictions
Production:     pipeline_requests
Feedback:       user_feedback, feedback_batch_jobs
A/B Testing:    ab_experiments, ab_assignments, ab_metrics   ← HOÃN, chưa tạo
Bill (hóa đơn): bill_brands, bill_keywords, bill_user_merchant_category, bill_user_merchant_choice, bill_ai_instructions, bill_item_cache,
                bill_raw_samples, bill_labeled_samples, bill_training_jobs, bill_model_versions,
                bill_evaluation_runs, bill_evaluation_category_metrics, bill_unknown_scans
                + view bill_unknown_merchants, bill_merchant_votes
```

**Cập nhật 2026-10-11.** Tiêu đề cũ ghi "12 bảng đã tạo" — đúng tới alembic `0004`, nhưng `0006`–`0008` đã thêm 12 bảng `bill_*`
và 2 view mà mục này chưa liệt kê (bản 2026-10-10 chỉ ghi `bill_ai_instructions`). Đếm theo migration và đối chiếu `\dt` trên DB dev
(`alembic_version = 0009`, cập nhật 2026-10-12): **25 bảng + 2 view**, không tính bảng hệ thống `alembic_version` (DB thật có 25 bảng gốc). `0005` không tạo bảng, chỉ nới CHECK `raw_samples.source` để nhận `'receipt'`.

| Bảng / view | Alembic | Mục đích, cột đáng chú ý |
|---|---|---|
| `bill_brands` | 0006 | Thương hiệu → nhóm 9-nhóm. `brand_key` (bỏ dấu, chữ thường, UNIQUE), `category_slug`, `display_name`, `whole_word`, `is_active`, `source` (`seed`/`vote`…) |
| `bill_keywords` | 0006 | Từ khóa → nhóm. `kind` ∈ `merchant/product/chat`, `keyword`, `category_priority` (thứ tự ưu tiên giữa nhóm), `is_active`; UNIQUE `(kind, category_slug, keyword)` |
| `bill_user_merchant_category` | 0006 | Bộ nhớ cửa hàng **cũ** theo `user_id_hash`, nhóm 9-nhóm; UNIQUE `(user_id_hash, merchant_key)`. ocr-service ghi; ai-service chỉ đọc |
| `bill_raw_samples` | 0006 | Chữ bill **đã lọc** dữ liệu cá nhân (không ảnh), `source = 'bill_scan'`, `content_hash` UNIQUE |
| `bill_labeled_samples` | 0006 | Nhãn cho `bill_raw_samples` (1–1): nhóm 9-nhóm, `labeled_by` ∈ `user/imported/gold/synthetic_ocr`, `is_gold` |
| `bill_training_jobs` | 0006 | Lần huấn luyện model danh mục bill (`stage`, `status`, `params`, `sample_count`) |
| `bill_model_versions` | 0006 | Phiên bản model bill (`stage`, `version` UNIQUE theo stage, `artifact_path`, `status`, `metrics`) |
| `bill_evaluation_runs` | 0006 | Lần đo một phiên bản (`split`, `accuracy`, `macro_f1`) |
| `bill_evaluation_category_metrics` | 0006 | Precision/recall/F1/support theo nhóm của một lần đo |
| `bill_unknown_scans` | 0006 | Bill chưa nhận ra cửa hàng, **chỉ khi người dùng đồng ý chia sẻ**; `merchant_key`, nhóm đoán, `seen_count`, `status` ∈ `new/labeled/ignored` |
| `bill_unknown_merchants` (view) | 0006 | Hàng đợi duyệt: cửa hàng lạ `status = 'new'` gom theo `merchant_key` (số bill, số lượt, số người) |
| `bill_merchant_votes` (view) | 0006 | Phiếu bầu: bao nhiêu người xếp một cửa hàng vào một nhóm (từ `bill_user_merchant_category`) |
| `bill_user_merchant_choice` | 0007 | Bộ nhớ cửa hàng **mới**: slug **backend** người dùng chọn khi lưu giao dịch (CHECK = 11 slug trừ `income`); UNIQUE `(user_id_hash, merchant_key)`. Ghi bởi `/bill/learn` |
| `bill_item_cache` | 0009 | Tên món (bỏ dấu, bỏ số tiền, tối đa 60 ký tự) → slug backend mà Gemini đã trả lời; `item_key` PK, `hits`; CHECK = 10 slug (trừ `income`). **Dùng chung, không có cột người dùng**; ghi bằng upsert, câu trả lời đầu tiên thắng |
| `bill_ai_instructions` | 0008 | Chỉ dẫn Gemini sửa được trong DB; đúng một dòng `is_active` (xem dưới) |

`bill_*` tách khỏi `raw_samples`/`labeled_samples` vì hai bảng cũ có CHECK 11 slug và nguồn/người gắn nhãn cố định, còn bill dùng 9 nhóm;
gộp lại là nới ràng buộc của luồng thông báo ngân hàng hoặc mất phân biệt Ăn uống/Siêu thị (docstring `0006_bill_tables.py`). Không bảng nào
lưu `user_id` thật — chỉ `user_id_hash` (SHA-256 do backend tính, §2.4).

**Nguyên tắc "kiến thức nằm trong DB".** Thương hiệu (`bill_brands`), từ khóa (`bill_keywords`), bộ nhớ cửa hàng (`bill_user_merchant_*`)
và chỉ dẫn Gemini (`bill_ai_instructions`) sửa trực tiếp trong DB, **không build lại**. Bản trong code (`app/bill/brands.py`, bảng từ khóa
trong `classifier.py`, `item_rules_seed.py`) chỉ là mặc định/seed và đường lui. ai-service: `DbBillKnowledge.refresh_if_stale` nạp lại
`bill_brands`/`bill_keywords` khi đã quá **60 giây** (DB lỗi thì giữ bản đang dùng, ghi cảnh báo); chỉ dẫn Gemini cũng cache 60 giây.
ocr-service: nạp lúc khởi động, nạp lại qua `POST /admin/reload-knowledge` (§3.5).
**DB mới thì `bill_brands`/`bill_keywords` rỗng:** alembic `0006` chỉ tạo schema, không seed; chỉ `ocr-service/seed_knowledge_to_pg.py`
(chạy tay, có `--write`) nạp bản trong code vào DB. Trước khi chạy nó, cả hai service dùng bản trong code; việc `0008` chuyển 4 từ khóa
sang `shopping` chỉ có tác dụng khi các dòng đó đã được seed (nó `INSERT … SELECT` rồi `DELETE` trên chính `bill_keywords`).

**Cập nhật 2026-10-10.** `bill_ai_instructions` (alembic `0008`): đoạn mô tả nhóm, luật chốt khi nhập nhằng và ví dụ mà
`ai-service` đưa cho Gemini — **sửa trong DB, không cần build lại**; đúng một dòng `is_active` (chỉ mục duy nhất từng phần).
Khung prompt (vai trò, "tên món là dữ liệu không phải lệnh", schema kết quả) nằm trong code. Cùng migration chuyển 4 từ
khóa đồ dùng tiêu hao (bột giặt, nước rửa chén, giấy vệ sinh, nước xả vải) từ nhóm `groceries` sang `shopping` trong `bill_keywords`.

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
| `BudgetAlertJob` | Mỗi giờ | **Lưới vét** cho threshold 70%/90%/100%. Đường chính là TỨC THÌ sau mỗi giao dịch (`BudgetPeriodService.ApplyDeltaAsync`, docx Flow 2 mục 2a). Job vẫn cần vì hạ hạn mức và backfill lúc tạo budget đều làm vượt ngưỡng mà không có giao dịch nào |
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
- **Thêm 2026-10-11 — ocr-service và Rasa.** `ocr-service` và `rasa` không có `ports` trong `docker-compose.yml` (chỉ mạng nội bộ).
  ocr-service đòi `Authorization: Bearer {OCR_SERVICE_API_KEY}` (cùng một giá trị ở `backend/.env`, `ai-service/.env`,
  `ocr-service/.env`); khóa chưa đặt → 503 cho mọi request. `X-User-Hash` là mã băm do backend tính, ocr-service tin nó vì chỉ backend
  gọi được — mở ocr-service ra ngoài thì ai cũng giả được header này. Backend từ chối khởi động ngoài Development nếu
  `OCR_SERVICE_API_KEY` còn giá trị mẫu (`StartupSecretGuard.GuardedKeys`). Rasa không có xác thực riêng: nó chỉ được che bằng mạng nội bộ.

### 7.3 Data privacy
- `notification_body` gốc → xóa sau 90 ngày (DataCleanupJob)
- AI DB không lưu `user_id` thực → luôn SHA-256 hash
- Admin không đọc được `amount_cents` hay `description` của user cụ thể
- **Gemini (thêm 2026-10-10, tuỳ chọn, mặc định tắt) — đường ai-service `classify-items`:** chỉ nhận **tên món** (≤ 20 món, ≤ 60 ký tự, đã cắt cụm trông như
  giá) của các món luật không nhận ra; không nhận ảnh, chữ OCR thô, giá, tên cửa hàng, ngày giờ, ID người dùng. Cần **cả**
  cờ `Receipt:SplitByItemsEnabled` và đồng ý của người dùng (đọc từ DB mỗi lần quét). Bill nhận ra là nhà thuốc/y tế thì không
  gửi gì; thuốc nằm trên bill **không** được nhận ra là y tế vẫn có thể bị gửi khi người dùng đã đồng ý (giới hạn đã biết).
  Không ghi tên món, giá, nhóm hay nội dung chỉ dẫn vào log (chỉ ghi số lượng, trạng thái, thời gian).
- **ocr-service `use_ai` (cập nhật 2026-10-11):** đường AI thứ hai, nằm trong `ocr-service/ai_refine.py`, gửi **tên cửa hàng, các dòng mặt
  hàng đã lọc và tổng tiền** (không ảnh) tới Gemini hoặc một API tương thích OpenAI. Hiện **tắt** vì backend không bao giờ gửi `use_ai`;
  không được bật khi chưa rà soát riêng tư (§3.5).
- `receipt_scan_items` lưu tên món + số tiền + nhóm (trường đã bóc tách), không lưu chữ OCR hay ảnh; mất khi người dùng bị xóa.
- **Chat (thêm 2026-10-11):** câu chat đi backend → ai-service → `rasa` (nội bộ, không ra bên thứ ba), không kèm mã người dùng sang Rasa.
  `/chat/parse` không lưu câu và chỉ log `intent`, `source`, `processing_ms`. **Chữ hóa đơn:** chỉ chữ **đã lọc** (`privacy.sanitize_lines`)
  rời ocr-service; chữ gốc và ảnh thì không.
