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

> **Cập nhật 2026-10-11.** Bảng trên là của `ai-service` (`FROM python:3.11-slim`). `ocr-service` chạy **Python 3.10**
> (`ocr-service/Dockerfile`: `FROM python:3.10-slim`), cài `requirements.txt` kèm `constraints.txt` ghim đúng phiên bản đã chạy thử
> (numpy, opencv, paddle…). `rasa` dùng image chính thức `rasa/rasa:3.6.21`, không build từ repo.

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

**Cập nhật 2026-10-10 — OCR tách thành container riêng `ocr-service` (đọc hóa đơn, tách từng món).**
`ocr-service` là một dịch vụ Python riêng, image riêng, chỉ backend/ai-service gọi nội bộ bằng khóa
`OCR_SERVICE_API_KEY`. Nó dùng:
```
paddleocr==2.7.3          # đọc chữ + hộp tọa độ từng dòng (ghép tên món với giá theo vị trí)
paddlepaddle==2.6.2
onnxruntime               # VietOCR (ONNX) đọc lại các dòng đầu phiếu để giữ dấu tiếng Việt
opencv (qua paddleocr)    # cắt tờ hóa đơn khỏi nền, làm thẳng ảnh nghiêng
scikit-learn, psycopg[binary], pillow, numpy, httpx, fastapi, uvicorn, python-multipart
```
Vì sao PaddleOCR giờ được dùng: lý do cấm ở trên là nó kéo `torch` vào image **phục vụ** của
ai-service. Ở đây nó nằm trong image riêng của `ocr-service`, nên image ai-service **vẫn không có**
`paddleocr`/`torch` và quy tắc tách requirements của Phase 9 vẫn đúng. PaddleOCR đọc ảnh nghiêng/mờ
tốt hơn Tesseract và cho hộp tọa độ, thứ bắt buộc để ghép tên món với giá.
Đường cũ `POST /api/v1/ocr` (Tesseract trong ai-service) **vẫn còn**, nhưng là **lựa chọn cấu hình**, không phải đường dự
phòng lúc chạy (sửa 2026-10-11, bản trước gọi nhầm là "dự phòng"): backend chỉ đi đường này khi **không** đặt `OCR_SERVICE_URL`;
có URL mà ocr-service lỗi thì lần quét thất bại, không quay về Tesseract. Chưa quyết định gỡ Tesseract.
Ảnh vẫn chỉ đi qua bộ nhớ, không lưu ở đâu. `ocr-service` chỉ trả về chữ **đã lọc** dữ liệu cá nhân
(số điện thoại, email, tên người, địa chỉ, số tài khoản) và các món đã tách; chữ gốc không rời nó.

**Dịch vụ bên thứ ba: Gemini API (TUỲ CHỌN, mặc định TẮT) — phân loại tên món (2026-10-10), đường ai-service `classify-items`.**
Gọi bằng `httpx` có sẵn (`generateContent`), **không thêm package nào**. Trên đường này chỉ `ai-service` gọi,
và chỉ khi **cả hai** điều sau đúng: cờ `Receipt:SplitByItemsEnabled` của backend bật **và** người dùng
bật công tắc đồng ý (`users.share_receipt_items_with_ai`, mặc định tắt cho mọi người). Không có khóa
`GEMINI_API_KEY` thì tính năng vẫn chạy bằng luật, món chưa biết thành `unknown`.
Thứ gửi ra: **chỉ tên món** của các món luật không nhận ra (tối đa 20 món, mỗi tên tối đa 60 ký tự,
đã cắt các cụm trông như giá tiền). **Không gửi**: ảnh, chữ OCR thô, giá, tên cửa hàng, ngày giờ, ID
người dùng. Bill nhận ra là bill nhà thuốc/y tế thì không gửi gì. Dùng bản **trả phí/Vertex** (điều
khoản bản miễn phí có thể dùng dữ liệu để cải thiện mô hình; cần đọc lại điều khoản hiện hành trước
khi dùng thật). Mô hình mặc định `gemini-3.5-flash-lite`. Lỗi/quá hạn → rơi về luật: timeout 5 giây
mỗi pha và 8 giây cho cả lời gọi, khóa mạch 30 giây sau lỗi, không bao giờ làm hỏng việc quét.
Đây **không** phải "OCR đám mây": nó chỉ nhận văn bản đã lọc, không nhận ảnh.
**Cập nhật 2026-10-11 — còn một đường AI thứ hai, đang TẮT.** `ocr-service/ai_refine.py` (chỉ khi request `/analyze-bill` có
`use_ai=true`) gửi **tên cửa hàng, các dòng mặt hàng đã lọc và tổng tiền** (không ảnh) tới Gemini, hoặc tới một API tương thích
OpenAI khi `FINMATE_AI_PROVIDER=openai`. Backend không bao giờ gửi `use_ai` nên đường này không chạy; không được bật khi chưa rà soát
riêng tư. Câu "chỉ tên món, không tên cửa hàng" ở trên chỉ đúng cho đường ai-service.

**Cập nhật 2026-10-11 — đối chiếu requirements/Dockerfile/compose với code.**
- `ai-service/requirements.txt` **không đổi** so với danh sách ở trên (vẫn `pytesseract`, `Pillow`, không có `paddleocr`, `torch`, `rasa`);
  `ai-service/Dockerfile` vẫn cài `tesseract-ocr` + `tesseract-ocr-vie`. Tesseract trong ai-service là **lựa chọn cấu hình**, không
  phải dự phòng lúc chạy: route `/api/v1/ocr` chỉ dùng nó khi ai-service **không** có `OCR_SERVICE_URL`; có URL thì route luôn gọi
  ocr-service (`remote_ocr.py`), ocr-service lỗi hoặc thiếu `OCR_SERVICE_API_KEY` → 503, không quay về Tesseract. Backend cũng không
  quay về: ocr-service lỗi → `AIServiceUnavailableException`, lần quét thất bại. Compose đặt `OCR_SERVICE_URL` cho cả backend và
  ai-service, nên bộ chạy mặc định không dùng Tesseract. Hai bộ đọc OCR: PaddleOCR + VietOCR trong `ocr-service` (khi có URL) và
  Tesseract trong `ai-service` (khi không có URL). Chưa quyết định gỡ Tesseract.
- **Rasa NLU (chat, Phase 19–20)** — ghi vào stack với sự đồng ý của chủ dự án ngày 2026-10-11 — chạy thành container riêng từ image `rasa/rasa:3.6.21` — **không** nằm trong requirements của
  ai-service. ai-service gọi nó bằng `httpx` có sẵn (`POST {RASA_URL}/model/parse`, timeout 2 giây, cầu dao 30 giây). Không có
  `RASA_URL` thì chat chạy bằng luật. Model đã huấn luyện (`ai-service/rasa/models/finmate-nlu-v3.tar.gz`) bị git bỏ qua; dữ liệu
  huấn luyện (`ai-service/rasa/data/nlu.yml`) và bộ đo (`ai-service/rasa/tests/nlu_test.yml`) nằm trong git.
- `ocr-service/requirements.txt` thực tế không ghim phiên bản (`fastapi`, `uvicorn`, `python-multipart`, `pillow`, `numpy`,
  `paddleocr==2.7.3`, `paddlepaddle==2.6.2`, `onnxruntime`, `scikit-learn`, `psycopg[binary]`, `pytest`, `httpx`); phiên bản thật do
  `constraints.txt` ghim. Model PaddleOCR được tải sẵn vào image lúc build. `ai_refine.py` của ocr-service gọi Gemini (hoặc API
  tương thích OpenAI) bằng `httpx`, chỉ khi request có `use_ai=true` — backend hiện không gửi cờ này.
- Backend: không thêm/bớt NuGet nào cho chat, OCR hay Rasa (`OcrServiceClient` dùng `HttpClient` qua `AddHttpClient`, chat/bill
  dùng Refit có sẵn).

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
| `easyocr`, `paddleocr` (trong image **ai-service**) | Kéo `torch` vào image phục vụ — xem mục OCR ở §2.2. `paddleocr` chỉ được dùng trong image riêng của `ocr-service` |
| OCR đám mây (`google-cloud-vision`…) | Ảnh hóa đơn không được rời khỏi hệ thống. Gemini trên đường ai-service `classify-items` (§2.2) chỉ nhận **tên món đã lọc**, không nhận ảnh; đường `use_ai` của ocr-service (đang tắt) gửi thêm tên cửa hàng và tổng tiền |

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
  ocr-service:  # Python FastAPI + PaddleOCR — đọc hóa đơn, lọc dữ liệu cá nhân, tách từng món (nội bộ)
  rasa:         # Rasa NLU cho chat — chỉ ai-service gọi; tắt thì ai-service trả lời bằng luật
  postgres-main: # PostgreSQL 16 — Backend DB
  postgres-ai:   # PostgreSQL 16 — AI DB
  redis:         # Redis 7
```

**Cập nhật 2026-10-11 (đối chiếu `docker-compose.yml`).** Không có service `hangfire-dashboard`: dashboard `/hangfire` chạy ngay
trong tiến trình `backend` (cổng 8080). Cổng mở ra máy: `backend` 8080, `ai-service` 8000, `postgres-main` 5433, `postgres-ai` 5434,
`redis` 6379. `ocr-service` và `rasa` **không** mở cổng (chỉ mạng nội bộ). `ocr-service` không có Postgres riêng: dùng `finmate_ai`
của `postgres-ai` (`FINMATE_DATABASE_URL`) và chờ `ai-service` healthy (ai-service chạy `alembic upgrade head` lúc khởi động).
`backend` chờ `ocr-service` đã chạy. `rasa` chạy `rasa/rasa:3.6.21` với model `models/finmate-nlu-v3.tar.gz` mount từ `ai-service/rasa/`.

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

# Email qua Brevo — TUỲ CHỌN, gọi bằng HttpClient trần nên KHÔNG thêm package nào. Thiếu key
# thì mã OTP in ra log thay vì gửi đi (máy dev, bộ test). Sender phải xác minh SPF/DKIM bên
# Brevo, nếu không thư rơi vào Spam — mà OTP trong Spam thì coi như không gửi.
#
# BẪY, gặp thật ngày 2026-09-15: Brevo mặc định chặn API key theo IP ("Authorised IPs"). Key
# đúng vẫn trả 401 kèm "unrecognised IP address" nếu IP gọi chưa được khai báo. Khi deploy lên
# server, IP CỦA SERVER sẽ bị chặn y hệt. Sửa ở https://app.brevo.com/security/authorised_ips —
# thêm IP, hoặc tắt hẳn ràng buộc nếu IP động.
BREVO_API_KEY=
BREVO_SENDER_EMAIL=
BREVO_SENDER_NAME=FinMate

# Quét hóa đơn qua ocr-service. Có OCR_SERVICE_URL thì backend điều phối; trống thì quay về POST /api/v1/ocr
# của ai-service (Tesseract; hoặc ocr-service nếu chính ai-service có OCR_SERVICE_URL — cập nhật 2026-10-11). Đây là lựa chọn
# cấu hình: có URL mà ocr-service lỗi thì lần quét thất bại, KHÔNG quay về /api/v1/ocr. Chưa quyết định gỡ Tesseract.
# Khóa phải giống OCR_SERVICE_API_KEY trong ocr-service/.env. Có URL mà khóa:
#   - THIẾU hẳn biến → backend ném lỗi lúc khởi động;
#   - để TRỐNG (OCR_SERVICE_API_KEY=) → vẫn khởi động, mọi lần quét nhận 401 từ ocr-service → AIServiceUnavailableException;
#   - còn giá trị MẪU "change-me…" → StartupSecretGuard từ chối khởi động ngoài Development.
OCR_SERVICE_URL=http://ocr-service:8000
OCR_SERVICE_API_KEY=change-me-min-32-chars-local-dev-ocr-key   # giá trị mẫu như backend/.env.example; thay khi deploy
# Tách hóa đơn theo từng món thành nhiều nhóm. Mặc định TẮT (false); bật thì bill có từ 2 danh mục thật
# tách thành nhiều giao dịch khi lưu. Không bật thì quét hóa đơn chạy y như trước.
Receipt__SplitByItemsEnabled=false

# Chặn đăng nhập khi email chưa xác minh. Mặc định BẬT; AuthApiFactory đặt false cho bộ test.
REQUIRE_EMAIL_VERIFICATION=

# Google OAuth qua trình duyệt nhúng — TUỲ CHỌN. Thiếu thì chỉ 3 endpoint /auth/google/* tắt,
# POST /auth/google (đường native) vẫn chạy. GOOGLE_CLIENT_ID phải là client loại **Web**.
GOOGLE_CLIENT_SECRET=
GOOGLE_REDIRECT_URI=http://localhost:8080/api/v1/auth/google/callback
MOBILE_DEEP_LINK=finmate://auth   # trống = callback hiện mã ra trang HTML để thử không cần app
```

**AI Service (.env):**
```
DATABASE_URL=postgresql://...  # AI DB
REDIS_URL=redis://...
INTERNAL_API_KEY=...           # phải match AI_SERVICE_API_KEY của backend
MODEL_REGISTRY_PATH=/models
LOG_LEVEL=INFO

# Phân loại tên món bằng Gemini — TUỲ CHỌN. Trống GEMINI_API_KEY = không gọi Gemini, món lạ thành unknown.
# Khóa nằm trong ai-service/.env (bị git bỏ qua), tuyệt đối không commit. Dùng bản trả phí.
GEMINI_API_KEY=
GEMINI_MODEL=gemini-3.5-flash-lite
GEMINI_TIMEOUT_SECONDS=5

# Thêm 2026-10-11 (đối chiếu app/core/config.py, tên biến = tên trường viết hoa).
# OCR service: có URL thì POST /api/v1/ocr luôn đọc qua ocr-service (lỗi → 503, không quay về Tesseract); trống thì Tesseract
# tại chỗ (lựa chọn cấu hình). Compose đặt URL.
OCR_SERVICE_URL=http://ocr-service:8000
OCR_SERVICE_API_KEY=...        # phải giống khóa ở backend/.env và ocr-service/.env
OCR_SERVICE_TIMEOUT_SECONDS=30
# Rasa cho POST /api/v1/chat/parse. Trống = chỉ dùng luật. Compose đặt http://rasa:5005.
RASA_URL=http://rasa:5005
# Khoản chi dưới mức này (đồng) mới được gắn auto_save_ok; 0 = không bao giờ.
CHAT_AUTO_SAVE_BELOW=2000000
```

**OCR Service (.env):**
```
OCR_SERVICE_API_KEY=...        # chỉ backend/ai-service gọi được; phải giống khóa ở hai nơi kia. Trống = từ chối mọi request (503)
FINMATE_DATABASE_URL=...       # DB của AI (finmate_ai): luật, thương hiệu, bill đã gắn nhãn. Compose tự đặt; trống = dùng file cạnh code

# Thêm 2026-10-11 — TUỲ CHỌN (đối chiếu os.environ.get trong ocr-service/*.py):
FINMATE_VIETOCR=1              # 0 = tắt bước VietOCR đọc lại đầu phiếu
FINMATE_ADMIN_TOKEN=           # bật POST /admin/reload-knowledge (header X-Admin-Token); trống = tắt hẳn
FINMATE_USER_HASH=             # mã người dùng mặc định khi request không có X-User-Hash (máy dev); mặc định "local"
FINMATE_AI_PROVIDER=gemini     # ai_refine.py, chỉ khi request có use_ai=true (backend không gửi); gemini | openai
GEMINI_API_KEY=                # như trên; không commit
GEMINI_MODEL=gemini-3.5-flash-lite
FINMATE_AI_BASE_URL=           # provider "openai" (API tương thích OpenAI); TUỲ CHỌN
FINMATE_AI_API_KEY=            # như trên; không commit
FINMATE_AI_MODEL=              # như trên
# Chỉ cho script chạy tay (db_setup.py), service không đọc:
FINMATE_IMPORT_DIR=            # thư mục nguồn của --import (mặc định: thư mục ocr-service)
FINMATE_GOLD_DIR=              # thư mục ảnh bộ chuẩn của --gold (mặc định: thư mục cha)
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

### 6.3 OCR Service Tests (thêm 2026-10-11)

```
ocr-service/tests/
├── conftest.py      # autouse: bộ nhớ cửa hàng + dataset ghi ra file tạm; bỏ kiểm khóa nội bộ (trừ test_internal_key.py)
├── pg_fake.py       # giả PostgreSQL bằng SQLite cho các test *_pg (không thay được thử trên Postgres thật)
├── fixtures/
└── test_*.py        # theo module (test_extract, test_items, test_privacy, test_dates, test_api…) VÀ theo hành vi
                     # (test_header_rescue, test_receipt_detection, test_databill_regressions…); vài module như
                     # vietocr_onnx.py không có file test riêng
```
- `pytest`, test nằm cạnh code của service. Thay bộ đọc OCR bằng hàm giả (`conftest.boxes_patch` thay `main.read_boxes`), nên phần
  lớn test không cần model PaddleOCR. Chạy trong image: xem `ocr-service/README.md`.
- Backend: test tích hợp đi qua HTTP với Testcontainers PostgreSQL (`Testcontainers.PostgreSql` trong `FinMate.Tests.csproj`); ai-service
  và ocr-service được thay bằng client giả (`FakeAIServiceClient`; `IOcrServiceClient` giả khai báo trong từng file test), ví dụ `ScanReceiptViaOcrServiceControllerTests`, `ChatControllerTests`.
