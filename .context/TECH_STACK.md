# TECH_STACK.md — FinMate Technology Stack

> Mọi dependency mới phải được approve trước khi thêm vào project.
> Không tự ý thêm package ngoài danh sách này.

---

## 1. Backend — ASP.NET Core 9

### 1.1 Runtime & Framework

| Thành phần | Version | Ghi chú |
|---|---|---|
| .NET | 9.0 | LTS |
| ASP.NET Core | 9.0 | Web API, không dùng MVC views |
| C# | 13 | Nullable reference types BẬT |
| Target framework | `net9.0` | |

### 1.2 NuGet Packages — ĐƯỢC DÙNG

**Core / Framework:**
```xml
<PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="9.*" />
<PackageReference Include="Swashbuckle.AspNetCore" Version="7.*" />
```

**Authentication:**
```xml
<PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="9.*" />
<PackageReference Include="System.IdentityModel.Tokens.Jwt" Version="8.*" />
<PackageReference Include="Google.Apis.Auth" Version="1.*" />
<!-- Verify Google ID token (mobile Sign-In flow) cho POST /api/v1/auth/google -->
```

**ORM & Database:**
```xml
<PackageReference Include="Microsoft.EntityFrameworkCore" Version="9.*" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="9.*" />
<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="9.*" />
<!-- Không cần Dapper — dùng EF Core cho tất cả -->
```

**Validation:**
```xml
<PackageReference Include="FluentValidation.AspNetCore" Version="11.*" />
```

**Mapping:**
```xml
<PackageReference Include="AutoMapper.Extensions.Microsoft.DependencyInjection" Version="12.*" />
<!-- KHÔNG dùng Mapster, KHÔNG dùng manual mapping trong Controller -->
```

**Caching:**
```xml
<PackageReference Include="StackExchange.Redis" Version="2.*" />
<PackageReference Include="Microsoft.Extensions.Caching.StackExchangeRedis" Version="9.*" />
```

**Background Jobs:**
```xml
<PackageReference Include="Hangfire.AspNetCore" Version="1.*" />
<PackageReference Include="Hangfire.PostgreSql" Version="1.*" />
<!-- Hangfire dùng PostgreSQL làm storage, không dùng SQL Server -->
```

**HTTP Client (gọi AI Service):**
```xml
<!-- Dùng IHttpClientFactory built-in, không cần package riêng -->
<!-- Refit nếu cần strongly-typed HTTP client -->
<PackageReference Include="Refit.HttpClientFactory" Version="7.*" />
```

**Logging:**
```xml
<PackageReference Include="Serilog.AspNetCore" Version="8.*" />
<PackageReference Include="Serilog.Sinks.Console" Version="6.*" />
<PackageReference Include="Serilog.Sinks.File" Version="6.*" />
<!-- Cấu hình: structured JSON logging, không dùng plain text -->
```

**Rate Limiting:**
```xml
<!-- Dùng built-in ASP.NET Core Rate Limiting (không cần package) -->
<!-- Microsoft.AspNetCore.RateLimiting — có sẵn trong ASP.NET Core 7+ -->
```

**Push Notification:**
```xml
<PackageReference Include="FirebaseAdmin" Version="3.*" />
<!-- Duyệt ngày 2026-09-14. Không có provider nào khác gửi được xuống Android khi app đã
     đóng: Android chỉ giữ MỘT kết nối thường trực cho cả máy, và kết nối đó là của Google.
     Kéo theo Google.Api.Gax.Rest + Google.Apis.Auth (đã duyệt sẵn cho Google login).
     API lưu ý: MulticastMessage.Tokens đã deprecated ở 3.6 — dùng Fids (cùng giá trị, đổi
     tên), và SendEachForMulticastAsync chứ không phải SendMulticastAsync để biết ĐÍCH DANH
     token nào chết mà xoá. -->
```

**Security:**
```xml
<PackageReference Include="BCrypt.Net-Next" Version="4.*" />
<!-- Dùng BCrypt cho password hashing, cost factor = 12 -->
```

**Testing:**
```xml
<PackageReference Include="xunit" Version="2.*" />
<PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
<PackageReference Include="Moq" Version="4.*" />
<PackageReference Include="FluentAssertions" Version="6.*" />
<PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="9.*" />
<PackageReference Include="Testcontainers.PostgreSql" Version="3.*" />
<!-- Testcontainers để spin up real PostgreSQL trong integration tests -->
```

### 1.3 NuGet Packages — CẤM DÙNG

| Package | Lý do cấm |
|---|---|
| `Dapper` | Đã quyết định dùng EF Core thuần |
| `Newtonsoft.Json` | Dùng `System.Text.Json` built-in thay thế |
| `AutoFac` | Dùng Microsoft DI built-in |
| `MediatR` | CQRS implement thủ công — tránh magic |
| `RestSharp` | Dùng `IHttpClientFactory` + Refit |
| `EntityFramework` (v6) | Chỉ dùng EF Core |
| `log4net`, `NLog` | Chỉ dùng Serilog |

---

## 2. AI Service — Python 3.11 FastAPI

### 2.1 Runtime

| Thành phần | Version | Ghi chú |
|---|---|---|
| Python | 3.11 | Dùng pyenv để manage version |
| Package manager | pip + `requirements.txt` | Không dùng Poetry hay Conda |

### 2.2 requirements.txt — ĐƯỢC DÙNG

**Web framework:**
```
fastapi==0.115.*
uvicorn[standard]==0.32.*
```

**Database:**
```
sqlalchemy==2.0.*
asyncpg==0.30.*           # async PostgreSQL driver
alembic==1.14.*           # migrations cho AI DB
```

**Pydantic:**
```
pydantic==2.10.*          # dùng Pydantic v2, không phải v1
pydantic-settings==2.*    # settings từ env vars
```

**ML / NLP:**
```
scikit-learn==1.6.*       # Classifier, Categorizer
transformers==4.47.*      # PhoBERT cho Vietnamese NLP
torch==2.5.*              # PyTorch backend (CPU build cho dev)
underthesea==6.*          # Vietnamese text processing
regex==2024.*             # Advanced regex cho amount extraction
```

> **Tách làm hai file từ Phase 9.** `requirements.txt` là thứ image PHỤC VỤ cài;
> `requirements-training.txt` thêm `torch`/`transformers`/`underthesea` cho training offline.
> Không gói nào bị gỡ khỏi danh sách đã duyệt — chỉ tách chỗ cài, vì máy phục vụ không cần
> thư viện training và cài vào làm image phồng thêm ~2.5GB.

**OCR — thêm ở Phase 10 (docx phương thức 3):**
```
pytesseract==0.3.*        # lớp bọc mỏng gọi binary `tesseract`
Pillow==11.*              # mở ảnh + tiền xử lý (xám hoá, tăng tương phản, xoay theo EXIF)
```
Kèm hai gói hệ điều hành cài trong `ai-service/Dockerfile`: `tesseract-ocr` và
`tesseract-ocr-vie`. Thiếu gói `-vie` thì OCR vẫn chạy nhưng đọc hóa đơn tiếng Việt ra ký tự
rác mà không báo lỗi gì.

**Vì sao Tesseract chứ không phải EasyOCR/PaddleOCR:** hai thư viện đó chính xác hơn trên ảnh
chụp nghiêng/mờ, nhưng chúng kéo `torch` ngược vào image **phục vụ** (~2.5GB) — xoá đúng cái
việc tách requirements vừa làm ở Phase 9. Tesseract thêm ~60MB và không cần GPU.

**Vì sao không dùng OCR đám mây (Google Vision, Azure):** ảnh hóa đơn chứa tên cửa hàng, số
tiền, đôi khi cả bốn số cuối thẻ. Gửi chúng ra dịch vụ bên thứ ba đi ngược toàn bộ nguyên tắc
riêng tư đã dựng (AI DB không giữ `user_id` thật, không log `notification_body`, che số tài
khoản trong `raw_samples`). Ảnh được xử lý trong bộ nhớ rồi bỏ, không lưu ở đâu cả.

**HTTP client (gọi về backend nếu cần):**
```
httpx==0.28.*             # async HTTP client
```

**Caching:**
```
redis==5.*
```

**Utilities:**
```
python-multipart==0.0.*   # file upload support
python-jose[cryptography]==3.*  # JWT verify internal API key
structlog==24.*           # structured logging
```

**Testing:**
```
pytest==8.*
pytest-asyncio==0.24.*
pytest-cov==6.*
httpx==0.28.*             # test client cho FastAPI
```

### 2.3 Packages — CẤM DÙNG

| Package | Lý do cấm |
|---|---|
| `flask` | Đã chọn FastAPI |
| `django` | Đã chọn FastAPI |
| `celery` | Background jobs xử lý ở backend (Hangfire) |
| `tensorflow` | Dùng PyTorch |
| `pandas` (trong production pipeline) | Dùng numpy/stdlib — pandas quá nặng cho inference |
| `requests` (sync) | Dùng `httpx` async thay thế |
| `easyocr`, `paddleocr` | Kéo `torch` vào image phục vụ — xem mục OCR ở §2.2 |
| OCR đám mây (`google-cloud-vision`…) | Ảnh hóa đơn không được rời khỏi hệ thống |

---

## 3. Database

### 3.1 PostgreSQL

| Config | Value |
|---|---|
| Version | 16 |
| Backend DB name | `finmate_main` |
| AI DB name | `finmate_ai` |
| Connection pooling | Pgbouncer (production) / EF Core pool (dev) |
| Extensions | `uuid-ossp`, `pg_trgm` (full-text search) |

**EF Core conventions:**
- Code First — Entity → Migration → DB
- `snake_case` cho tên bảng và cột (dùng Npgsql naming convention)
- Mỗi entity có 1 `IEntityTypeConfiguration<T>` riêng
- Không dùng Data Annotations — dùng Fluent API trong configuration
- Global query filter cho soft delete: `.HasQueryFilter(e => e.DeletedAt == null)`

### 3.2 Redis

| Config | Value |
|---|---|
| Version | 7.x |
| Serialization | `System.Text.Json` |
| Key format | `{resource}:{id}:{sub}` — ví dụ `user:uuid:profile` |
| Default TTL | Xem bảng trong ARCHITECTURE.md |

---

## 4. Infrastructure

### 4.1 Docker

```yaml
# Services trong docker-compose.yml (local dev)
services:
  backend:      # ASP.NET Core 9
  ai-service:   # Python FastAPI
  postgres-main: # PostgreSQL 16 — Backend DB
  postgres-ai:   # PostgreSQL 16 — AI DB
  redis:         # Redis 7
  hangfire-dashboard: # Exposed port cho dev monitoring
```

### 4.2 Environment Variables

**Backend (.env):**
```
DATABASE_URL=postgresql://...
REDIS_URL=redis://...
JWT_SECRET=...              # min 64 chars
JWT_ACCESS_TTL_MINUTES=15
JWT_REFRESH_TTL_DAYS=30
AI_SERVICE_URL=http://ai-service:8000
AI_SERVICE_API_KEY=...      # internal key, min 32 chars
HANGFIRE_DASHBOARD_USER=...
HANGFIRE_DASHBOARD_PASS=...

# Push notification — TUỲ CHỌN, chọn một trong hai. Trống cả hai thì chạy log-only.
FCM_CREDENTIALS_PATH=/run/secrets/fcm.json   # file service-account mount vào container
FCM_CREDENTIALS_JSON={"type":"service_account",...}   # hoặc dán thẳng nội dung file

# Rate limiting — TUỲ CHỌN, trống thì dùng mặc định trong code.
RATE_LIMIT_DEFAULT_PER_MINUTE=120   # mọi request; phân vùng theo user id, hoặc IP khi ẩn danh
RATE_LIMIT_AUTH_PER_MINUTE=10       # login/register/google; phân vùng theo IP

# Reverse proxy — TUỲ CHỌN. Trống = KHÔNG bật, X-Forwarded-For bị bỏ qua (mặc định an toàn).
# Danh sách IP proxy khi chạy sau nginx/ALB; "*" để tin mọi forwarder (một số PaaS bắt buộc),
# chỉ dùng khi nền tảng ĐÃ tự ghi đè header ở biên — nếu không, client tự đặt IP giả để thoát
# rate limit. Bắt buộc phải đặt khi có proxy: thiếu nó thì rate limit đăng nhập đếm theo IP
# của proxy, tức là cả hệ thống dùng chung một ngăn 10 lần/phút.
TRUSTED_PROXIES=
```

**AI Service (.env):**
```
DATABASE_URL=postgresql://...  # AI DB
REDIS_URL=redis://...
INTERNAL_API_KEY=...           # phải match AI_SERVICE_API_KEY của backend
MODEL_REGISTRY_PATH=/models
LOG_LEVEL=INFO
```

---

## 5. Code Style

### 5.1 C# (.NET Backend)

- `nullable enable` — bật toàn bộ project
- `implicit usings` — bật
- Async/await xuyên suốt — không dùng `.Result` hay `.Wait()`
- Record types cho immutable DTOs: `public record LoginRequest(string Email, string Password);`
- Pattern matching thay vì if/else chain khi có thể
- `ArgumentNullException.ThrowIfNull()` thay vì manual null check

**Naming:**
```csharp
// Classes, Methods, Properties: PascalCase
public class TransactionService { }
public async Task<TransactionDto> ConfirmAsync(Guid id) { }

// Private fields: _camelCase
private readonly ITransactionRepository _repository;

// Local variables, params: camelCase
var transactionId = Guid.NewGuid();

// Constants: PascalCase
public const int MaxRetryCount = 3;

// Async methods: luôn suffix Async
public async Task<Result> CreateAsync(CreateCommand command) { }
```

### 5.2 Python (AI Service)

- Type hints bắt buộc cho tất cả function signatures
- `black` formatter — line length 88
- `ruff` linter
- `isort` cho import ordering
- Docstring cho tất cả public functions (Google style)

```python
# Đúng
async def analyze_notification(
    request: AnalyzeRequest,
    db: AsyncSession = Depends(get_db),
) -> AnalyzeResponse:
    """Phân tích notification và trả về pipeline result.

    Args:
        request: Notification data từ backend.
        db: Database session.

    Returns:
        Pipeline result với classifier, extraction và categorization output.
    """

# Sai — không có type hints, không có docstring
def analyze(req, db):
    pass
```

---

## 6. Testing Standards

### 6.1 Backend Tests

```
Tests/
├── Unit/
│   ├── Application/    # Test handlers, business logic
│   └── Domain/         # Test domain entities, value objects
└── Integration/
    ├── Controllers/     # Test HTTP endpoints với Testcontainers
    └── Repositories/   # Test EF Core queries với real DB
```

**Coverage targets:**
- Application layer (handlers): ≥ 80%
- Domain layer (entities): ≥ 90%
- Infrastructure (repos): ≥ 70% (integration tests)

### 6.2 AI Service Tests

```
tests/
├── unit/
│   ├── test_classifier.py
│   ├── test_extractor.py
│   └── test_categorizer.py
└── integration/
    └── test_pipeline.py   # End-to-end pipeline với sample notifications
```

**Test data:** Mỗi provider phải có ít nhất 10 sample notifications trong `tests/fixtures/`
