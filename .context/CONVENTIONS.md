# CONVENTIONS.md — FinMate Coding Conventions

---

## 1. API Design Conventions

### 1.1 URL Structure

```
/api/v1/{resource}                    # Collection
/api/v1/{resource}/{id}               # Single resource
/api/v1/{resource}/{id}/{sub}         # Sub-resource
/api/v1/{resource}/{id}/actions/{verb} # Actions (không phải CRUD)

# Ví dụ thực tế:
GET    /api/v1/transactions                    # Danh sách
GET    /api/v1/transactions/{id}               # Chi tiết
POST   /api/v1/transactions                    # Tạo mới (manual)
PATCH  /api/v1/transactions/{id}               # Cập nhật một phần
DELETE /api/v1/transactions/{id}               # Xóa

# Actions — dùng noun/verb rõ ràng:
POST   /api/v1/transactions/{id}/confirm       # Xác nhận draft
POST   /api/v1/notifications/analyze           # Phân tích notification
POST   /api/v1/auth/refresh                    # Refresh token
POST   /api/v1/auth/logout
POST   /api/v1/saving-goals/{id}/contribute    # Đóng góp vào goal

# Admin endpoints — prefix riêng:
GET    /api/v1/admin/users
PATCH  /api/v1/admin/users/{id}/lock
GET    /api/v1/admin/ai-stats
```

**Quy tắc URL:**
- `kebab-case` cho URL path: `/saving-goals`, không phải `/savingGoals` hay `/saving_goals`
- Số nhiều cho collection: `/transactions`, `/budgets`
- Không để trailing slash: `/transactions` không phải `/transactions/`
- Version trong URL: `/api/v1/` — không dùng header versioning

### 1.2 HTTP Methods

| Method | Dùng khi | Idempotent? |
|---|---|---|
| `GET` | Đọc data, không thay đổi state | ✅ |
| `POST` | Tạo mới, hoặc action không idempotent | ❌ |
| `PATCH` | Cập nhật một phần fields | ✅ |
| `PUT` | Thay thế toàn bộ resource (ít dùng) | ✅ |
| `DELETE` | Xóa resource | ✅ |

**Không dùng `POST` cho mọi thứ** — xác định đúng method.

### 1.3 Request/Response Format

**Request body naming:** `camelCase`
```json
{
  "amountCents": 75000,
  "categoryId": "uuid",
  "transactedAt": "2026-09-09T10:30:00+07:00",
  "description": "Ăn sáng"
}
```

**Response envelope — luôn wrap trong envelope:**
```json
// Success
{
  "success": true,
  "data": { ... },
  "meta": {                    // optional, dùng cho pagination
    "total": 100,
    "page": 1,
    "limit": 20,
    "cursor": "next_token"
  }
}

// Error
{
  "success": false,
  "error": {
    "code": "TRANSACTION_NOT_FOUND",  // machine-readable error code
    "message": "Không tìm thấy giao dịch",  // human-readable
    "details": [                       // optional, validation errors
      { "field": "amountCents", "message": "Phải lớn hơn 0" }
    ]
  }
}
```

**HTTP Status Codes:**
```
200 OK            → GET thành công, PATCH thành công
201 Created       → POST tạo mới thành công
204 No Content    → DELETE thành công
400 Bad Request   → Validation error, malformed request
401 Unauthorized  → Chưa đăng nhập hoặc token hết hạn
403 Forbidden     → Đã đăng nhập nhưng không có quyền
404 Not Found     → Resource không tồn tại
409 Conflict      → Duplicate (email đã tồn tại, hash trùng)
422 Unprocessable → Business logic error (không đủ balance, budget conflict)
429 Too Many Requests → Rate limit
500 Internal Server Error → Unexpected error (không để lộ details)
```

**Phân biệt 400 vs 422:**
- `400`: Input sai format, type sai, required field thiếu → FluentValidation catches
- `422`: Input format đúng nhưng business rule vi phạm → Service layer throws

---

## 2. Error Handling

### 2.1 Exception Hierarchy (Backend C#)

```csharp
// Base
public abstract class FinMateException : Exception
{
    public abstract string ErrorCode { get; }
    public abstract int HttpStatusCode { get; }
}

// 404
public class NotFoundException : FinMateException
{
    public override string ErrorCode => "NOT_FOUND";
    public override int HttpStatusCode => 404;

    public NotFoundException(string resourceName, Guid id)
        : base($"{resourceName} với id '{id}' không tồn tại.") { }
}

// 403
public class ForbiddenException : FinMateException
{
    public override string ErrorCode => "FORBIDDEN";
    public override int HttpStatusCode => 403;
}

// 409
public class ConflictException : FinMateException
{
    public override string ErrorCode { get; }
    public override int HttpStatusCode => 409;

    public ConflictException(string code, string message) : base(message)
        => ErrorCode = code;
}

// 422 — Business logic violations
public class BusinessRuleException : FinMateException
{
    public override string ErrorCode { get; }
    public override int HttpStatusCode => 422;

    public BusinessRuleException(string code, string message) : base(message)
        => ErrorCode = code;
}
```

**Error codes chuẩn:**
```
// Auth
AUTH_INVALID_CREDENTIALS
AUTH_ACCOUNT_LOCKED
AUTH_TOKEN_EXPIRED
AUTH_TOKEN_INVALID
AUTH_TOKEN_REUSE_DETECTED
AUTH_RATE_LIMITED

// Transactions
TRANSACTION_NOT_FOUND
TRANSACTION_ALREADY_CONFIRMED
TRANSACTION_NOT_DRAFT
TRANSACTION_BELONGS_TO_OTHER_USER

// Budgets
BUDGET_CATEGORY_ALREADY_EXISTS
BUDGET_CATEGORY_HAS_ACTIVE_BUDGET

// Notifications
NOTIFICATION_DUPLICATE_HASH
NOTIFICATION_AI_SERVICE_UNAVAILABLE
NOTIFICATION_AI_TIMEOUT

// General
VALIDATION_ERROR
RESOURCE_NOT_FOUND
PERMISSION_DENIED
INTERNAL_ERROR
```

### 2.2 ExceptionHandlingMiddleware

```csharp
// Tất cả exception đều được catch ở đây, không để leak ra ngoài
public class ExceptionHandlingMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (FinMateException ex)
        {
            // Known domain exceptions → trả về error response chuẩn
            await WriteErrorResponse(context, ex.HttpStatusCode, ex.ErrorCode, ex.Message);
        }
        catch (ValidationException ex) // FluentValidation
        {
            // Validation errors → 400 với details
            await WriteValidationErrorResponse(context, ex.Errors);
        }
        catch (Exception ex)
        {
            // Unexpected → log đầy đủ, trả về generic 500 (không lộ details)
            _logger.LogError(ex, "Unhandled exception");
            await WriteErrorResponse(context, 500, "INTERNAL_ERROR",
                "Đã xảy ra lỗi hệ thống. Vui lòng thử lại sau.");
        }
    }
}
```

### 2.3 AI Service Error Handling (Python)

```python
# app/core/exceptions.py
class FinMateAIException(Exception):
    def __init__(self, error_code: str, message: str, status_code: int = 500):
        self.error_code = error_code
        self.message = message
        self.status_code = status_code

class ClassifierException(FinMateAIException):
    def __init__(self, message: str):
        super().__init__("CLASSIFIER_ERROR", message, 500)

class ExtractionException(FinMateAIException):
    def __init__(self, message: str):
        super().__init__("EXTRACTION_ERROR", message, 422)

# Pipeline luôn return result, không throw exception ra ngoài:
# - Nếu classifier fail → pipeline_result = "error"
# - Nếu extractor fail → pipeline_result = "extraction_failed"
# AI Service KHÔNG để exception unhandled reach client
```

---

## 3. Validation Conventions

### 3.1 Backend Validation (FluentValidation)

```csharp
// Mỗi Command/Request có 1 Validator class riêng
public class CreateManualTransactionValidator
    : AbstractValidator<CreateManualTransactionCommand>
{
    public CreateManualTransactionValidator()
    {
        RuleFor(x => x.AmountCents)
            .GreaterThan(0)
            .WithMessage("Số tiền phải lớn hơn 0.")
            .LessThanOrEqualTo(100_000_000_000L) // 100 tỷ VND — sanity check
            .WithMessage("Số tiền không hợp lệ.");

        RuleFor(x => x.Type)
            .IsInEnum()
            .WithMessage("Loại giao dịch không hợp lệ.");

        RuleFor(x => x.AccountId)
            .NotEmpty()
            .WithMessage("Tài khoản không được để trống.");

        RuleFor(x => x.TransactedAt)
            .NotEmpty()
            .LessThanOrEqualTo(DateTimeOffset.UtcNow.AddDays(365))
            .WithMessage("Ngày giao dịch không được quá 1 năm trong tương lai.");

        // Transfer phải có destination
        When(x => x.Type == TransactionType.Transfer, () =>
        {
            RuleFor(x => x.DestinationAccountId)
                .NotEmpty()
                .WithMessage("Giao dịch chuyển khoản phải có tài khoản đích.")
                .NotEqual(x => x.AccountId)
                .WithMessage("Tài khoản nguồn và đích không được giống nhau.");
        });
    }
}
```

**Quy tắc validation:**
- Validate ở Application layer (Command/Query validators), không validate trong Controller
- Error message tiếng Việt — user-facing
- Không validate business rules trong FluentValidation — chỉ validate format/type/range
- Business rules validate trong Service/Handler và throw `BusinessRuleException`

### 3.2 AI Service Validation (Pydantic v2)

```python
from pydantic import BaseModel, Field, field_validator
from datetime import datetime

class AnalyzeRequest(BaseModel):
    backend_request_id: str = Field(..., description="UUID từ backend")
    user_id_hash: str = Field(..., min_length=64, max_length=64,
                              description="SHA-256 của user_id")
    package_name: str = Field(..., min_length=3, max_length=200)
    notification_title: str | None = Field(None, max_length=500)
    notification_body: str = Field(..., min_length=1, max_length=5000)
    received_at: datetime

    @field_validator('user_id_hash')
    @classmethod
    def validate_hash_format(cls, v: str) -> str:
        import re
        if not re.match(r'^[a-f0-9]{64}$', v):
            raise ValueError('user_id_hash phải là SHA-256 hex string')
        return v

    @field_validator('package_name')
    @classmethod
    def validate_package_name(cls, v: str) -> str:
        # Android package name format
        import re
        if not re.match(r'^[a-zA-Z][a-zA-Z0-9_]*(\.[a-zA-Z][a-zA-Z0-9_]*)+$', v):
            raise ValueError('package_name không đúng định dạng Android')
        return v
```

---

## 4. Database Conventions

### 4.1 EF Core Entity Configuration

```csharp
// Mỗi entity có file configuration riêng
// backend/FinMate.Infrastructure/Persistence/Configurations/TransactionConfiguration.cs

public class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("transactions");                // snake_case
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id)
               .HasColumnName("id")
               .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(t => t.AmountCents)
               .HasColumnName("amount_cents")
               .IsRequired();

        builder.Property(t => t.Type)
               .HasColumnName("type")
               .HasConversion<string>()               // enum → string
               .IsRequired();

        // Soft delete filter — áp dụng toàn bộ queries
        builder.HasQueryFilter(t => t.DeletedAt == null);

        // Indexes
        builder.HasIndex(t => new { t.UserId, t.TransactedAt })
               .HasDatabaseName("idx_txn_user_time")
               .IsDescending(false, true);            // transacted_at DESC

        // Relationships
        builder.HasOne(t => t.User)
               .WithMany(u => u.Transactions)
               .HasForeignKey(t => t.UserId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
```

**Naming conventions cho EF Core:**
- Bảng: `snake_case` (cấu hình `UseSnakeCaseNamingConvention()` trong Npgsql)
- Column: `snake_case`
- Index: `idx_{table}_{columns}` — ví dụ `idx_txn_user_time`
- Unique constraint: `uq_{table}_{columns}`
- FK constraint: `fk_{table}_{referenced_table}`
- Check constraint: `chk_{table}_{description}`

### 4.2 Migration Conventions

```bash
# Tên migration: PascalCase, mô tả rõ ràng
dotnet ef migrations add AddTransactionSavingGoalLink
dotnet ef migrations add CreateBudgetPeriodsTable
dotnet ef migrations add AddIndexOnNotificationLogsStatus

# KHÔNG dùng tên mơ hồ:
# ❌ dotnet ef migrations add UpdateTable
# ❌ dotnet ef migrations add Fix
# ❌ dotnet ef migrations add Changes
```

**Quy tắc migration:**
- Không sửa migration đã apply vào DB production
- Nếu cần sửa → tạo migration mới
- Mỗi migration có `Up()` và `Down()` hoàn chỉnh
- Migration không chứa data seeding (dùng seeder riêng)

### 4.3 Repository Pattern

```csharp
// Interface trong Application layer
public interface ITransactionRepository
{
    Task<Transaction?> GetByIdAsync(Guid id, Guid userId, CancellationToken ct = default);
    Task<PagedResult<Transaction>> GetListAsync(TransactionFilter filter, CancellationToken ct = default);
    Task<Transaction> CreateAsync(Transaction transaction, CancellationToken ct = default);
    Task UpdateAsync(Transaction transaction, CancellationToken ct = default);
    Task DeleteAsync(Transaction transaction, CancellationToken ct = default); // soft delete
}

// Implementation trong Infrastructure layer
public class TransactionRepository : ITransactionRepository
{
    private readonly FinMateDbContext _context;

    // Luôn filter theo userId — không bao giờ query cross-user
    public async Task<Transaction?> GetByIdAsync(Guid id, Guid userId, CancellationToken ct)
        => await _context.Transactions
            .Include(t => t.Category)
            .Include(t => t.Account)
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, ct);
            // soft delete filter tự động apply từ HasQueryFilter
}
```

---

## 5. Logging Conventions

### 5.1 Backend Logging (Serilog)

```csharp
// Log levels:
// Verbose  → chi tiết quá mức, chỉ bật khi debug cụ thể
// Debug    → debug info, tắt trong production
// Info     → business events quan trọng
// Warning  → unexpected nhưng recoverable
// Error    → unexpected, cần attention
// Fatal    → hệ thống không thể tiếp tục

// Structured logging — KHÔNG format string
// ❌ Sai:
_logger.LogInformation($"User {userId} confirmed transaction {transactionId}");

// ✅ Đúng:
_logger.LogInformation(
    "Transaction confirmed. {UserId} {TransactionId} {AmountCents}",
    userId, transactionId, amountCents);

// KHÔNG LOG các field nhạy cảm:
// ❌ password, token, refresh_token
// ❌ notification_body gốc
// ❌ amount_cents (chỉ log transaction ID, không log số tiền)
// ❌ full account_number
```

**Log template chuẩn:**
```csharp
// Business events
_logger.LogInformation("Transaction.Confirmed {TransactionId} {UserId}", id, userId);
_logger.LogInformation("Budget.Alert.Sent {BudgetId} {Percentage}", budgetId, 80);
_logger.LogWarning("Auth.RateLimit.Hit {Email} {IpAddress}", email, ip);
_logger.LogError("AI.Service.Timeout {RequestId} {DurationMs}", requestId, ms);
```

### 5.2 AI Service Logging (structlog)

```python
import structlog
log = structlog.get_logger()

# Structured — luôn dùng kwargs
log.info("pipeline.completed",
         backend_request_id=request_id,
         pipeline_result="financial",
         classifier_confidence=0.97,
         processing_ms=245)

log.warning("pipeline.uncertain",
            backend_request_id=request_id,
            classifier_confidence=0.41,
            reason="low_confidence")

# KHÔNG log:
# - user_id_hash đầy đủ (chỉ log 8 ký tự đầu nếu cần trace)
# - notification_body
# - predicted amount
```

---

## 6. Security Conventions

### 6.1 Authorization

```csharp
// Mọi Controller đều có [Authorize] — không có exception
[ApiController]
[Route("api/v1/[controller]")]
[Authorize]  // ← bắt buộc
public class TransactionsController : ControllerBase { }

// Chỉ Auth endpoints mới có [AllowAnonymous]
[AllowAnonymous]
[HttpPost("login")]
public async Task<IActionResult> Login([FromBody] LoginRequest request) { }

// Admin endpoints dùng policy
[Authorize(Policy = "AdminOnly")]
[HttpGet("admin/users")]
public async Task<IActionResult> GetUsers() { }
```

### 6.2 User Isolation — Critical

```csharp
// LUÔN lấy userId từ JWT claims, KHÔNG từ request body/path
private Guid GetCurrentUserId()
    => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

// Repository luôn filter theo userId
// ❌ Sai — có thể truy cập data của user khác:
var transaction = await _repo.GetByIdAsync(transactionId);

// ✅ Đúng:
var userId = GetCurrentUserId();
var transaction = await _repo.GetByIdAsync(transactionId, userId);
if (transaction == null)
    throw new NotFoundException("Transaction", transactionId);
```

### 6.3 Input Sanitization

```csharp
// Text fields — trim và limit length trước khi lưu
public record CreateTransactionCommand
{
    public string? Description { get; init; }
    public string? MerchantName { get; init; }
}

// Trong handler:
var description = command.Description?.Trim().TruncateTo(500);
var merchantName = command.MerchantName?.Trim().TruncateTo(200);
```

---

## 7. Async/Concurrency Conventions

### 7.1 CancellationToken

```csharp
// Luôn truyền CancellationToken xuống tất cả async calls
public async Task<TransactionDto> ConfirmTransactionAsync(
    Guid transactionId,
    CancellationToken cancellationToken = default)
{
    var transaction = await _repository
        .GetByIdAsync(transactionId, _userId, cancellationToken);
    // ...
    await _repository.UpdateAsync(transaction, cancellationToken);
}
```

### 7.2 DB Transaction cho cascade operations

```csharp
// Khi confirm transaction → nhiều bảng cập nhật → dùng DB transaction
public async Task ConfirmTransactionAsync(Guid id, CancellationToken ct)
{
    await using var dbTransaction = await _context.Database
        .BeginTransactionAsync(ct);
    try
    {
        // 1. Update transaction
        // 2. Update budget_periods.spent_cents
        // 3. Update user_gamification.exp_points
        // 4. Check và update streak
        // 5. Check mission conditions
        await _context.SaveChangesAsync(ct);
        await dbTransaction.CommitAsync(ct);
    }
    catch
    {
        await dbTransaction.RollbackAsync(ct);
        throw;
    }
}
```

---

## 8. Money Handling Convention

```csharp
// Tất cả amount đều là BIGINT cents (đồng VND)
// KHÔNG dùng decimal, double, float cho tiền

// Value Object để wrap
public readonly record struct Money(long Cents)
{
    public static Money Zero => new(0);

    public static Money FromDong(long dong) => new(dong);

    // Display: 75000 → "75.000 ₫"
    public string ToDisplayString()
        => $"{Cents:N0} ₫".Replace(",", ".");

    public Money Add(Money other) => new(Cents + other.Cents);
    public Money Subtract(Money other) => new(Cents - other.Cents);

    public bool IsPositive => Cents > 0;
    public bool IsZero => Cents == 0;
}

// Parse từ user input (string "75k", "75,000", "75000")
public static class MoneyParser
{
    public static long ParseVnd(string input)
    {
        // "75k" → 75000
        // "1.5tr" → 1500000
        // "75,000" → 75000
        // Implementation trong utils
    }
}
```

**Quy tắc tính toán:**
- Cộng/trừ: dùng `long` arithmetic bình thường
- Không bao giờ chia số tiền và làm tròn trừ khi có explicit business requirement
- Hiển thị: format VND chuẩn Việt Nam `75.000 ₫` (dấu chấm ngàn, ký hiệu ₫)
