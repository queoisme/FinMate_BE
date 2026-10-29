# Tách nội dung thông báo + phân loại bằng model AI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Giao dịch tạo từ thông báo ngân hàng chỉ lưu phần nội dung (ví dụ "mua quan ao") thay vì cả đoạn text, và khi từ điển không chắc thì Gemini đoán danh mục từ phần nội dung đó (chỉ khi người dùng đồng ý AI).

**Architecture:** (A) Tài liệu cấu hình `narrative_labels` trong DB (cơ chế `config_documents` có sẵn) chứa nhãn "nội dung"; `extractor` cắt phần sau nhãn. (B) Bước async sau `Categorizer` trong pipeline: nếu từ điển không khớp, là khoản chi, đã có phần nội dung tách được và `allow_ai=true`, gọi Gemini của chat qua `classify_items`. Cờ `allow_ai` đi từ backend (đọc `IAiConsentReader`) sang ai-service.

**Tech Stack:** Python 3.11 / FastAPI / pytest / SQLAlchemy async (ai-service); ASP.NET Core 9 / xUnit / Moq (backend). Không thêm dependency, không đổi schema DB.

**Spec:** `docs/superpowers/specs/2026-10-29-notification-narrative-and-ai-category-design.md`

**Thư mục làm việc:** `E:\Ki 9\Model AI\FinMate_BE` (repo git thật). Lệnh Python chạy trong `ai-service/`, lệnh dotnet trong `backend/`.

## Global Constraints

- AGENTS.md §3.2: KHÔNG log `notification_body`, nội dung phần tách, `amount_cents`; chỉ log mã lỗi/loại lỗi.
- Chỉ gửi cho bên thứ ba **một cụm nội dung đã tách bằng nhãn**, đã qua `anonymize_body` (che dãy số ≥ 8 chữ số) và bỏ số tiền; KHÔNG gửi số tiền, số dư, số tài khoản, người dùng. Không gửi khi người dùng chưa đồng ý AI.
- Điều kiện gọi AI (tất cả): kết quả luật là `other` do không khớp từ điển (không phải `income`), `transaction_type == DEBIT`, `allow_ai == true`, phần nội dung do **nhãn hoặc trường mô tả của định dạng** tách ra (không bao giờ gửi cả đoạn thông báo), không rỗng sau khi bỏ số tiền, không phải sức khỏe (`resolve(phrase).slug != "health"`).
- Client Gemini: `get_chat_gemini_client` (không OpenRouter), trần riêng `NOTIFICATION_AI_TIMEOUT_SECONDS` mặc định **6.0** giây trong `Settings`; mọi lỗi/quá hạn/`unknown` → giữ kết quả luật (`other`), `/analyze` không bao giờ lỗi vì AI.
- Độ tin cậy kết quả AI: khóa mới `categorizer.ai` = **0.70** (phải **< 0.85**, ngưỡng một chạm `backend.one_tap_threshold`).
- Response `/analyze` KHÔNG đổi cấu trúc. `allow_ai` là trường tùy chọn mặc định `false` ở cả hai phía (tương thích ngược hai chiều).
- Nhãn "nội dung" sống trong DB (`config_documents`, tên `narrative_labels`); file `app/data/narrative_labels.json` chỉ là seed. Bản mặc định gồm: `nd`, `noi dung`, `ndung`, `content`.
- AGENTS.md: không đổi schema DB, không thêm dependency, không sửa `.context/ARCHITECTURE.md` / `TECH_STACK.md`, không push `main`/`develop`; code mới có test; C# không magic string, không warning mới, method ≤ 50 dòng; Python theo `black`/`ruff`/`isort`.
- Không stage `backend/FinMate.Infrastructure/Persistence/Seed/ProviderConfigSeeder.cs` và các file không thuộc kế hoạch này (đang có thay đổi chưa commit của người khác/việc khác trong cây làm việc).

## Review Focus

- Tên người có chữ "ND" lẫn trong nội dung ("NGUYEN VAN ND chuyen tien") không bị cắt sai; chỉ nhãn đứng **đầu một mệnh đề** (đầu chuỗi hoặc sau `.`/`|`/`;`) mới tính (Task 1).
- Thông báo KHÔNG có nhãn hoặc định dạng không có trường mô tả: `description` giữ cả đoạn như cũ nhưng AI **không** được gọi (không bao giờ gửi cả đoạn có số dư/số tài khoản) (Task 3, 4).
- Gemini lỗi, quá hạn, trả `unknown`, hoặc cầu dao mở → `/analyze` vẫn 200 với `other` (Task 4, 5).
- Backend cũ không gửi `allow_ai` → không bao giờ gọi AI; thu nhập, tiền vào, nội dung sức khỏe không bao giờ gửi (Task 4, 5).
- Seed `narrative_labels` chạy lại nhiều lần không tạo trùng phiên bản và DB thắng khi đã có phiên bản; lỗi seed không làm sập khởi động (Task 2).
- `RetryFailedNotificationJob` dựng `AnalyzeRequest` không có đồng ý → `AllowAi=false` (an toàn); backend KHÔNG gửi `true` khi không có người dùng/đồng ý (Task 6).

---

### Task 0: Chuẩn bị nhánh và mốc kiểm tra

**Files:** không sửa mã.

- [ ] **Step 1:** Trong `E:\Ki 9\Model AI\FinMate_BE` chạy `git status --short` và `git branch --show-current`; ghi lại các file đang bị sửa/chưa theo dõi sẵn có (đặc biệt `ProviderConfigSeeder.cs`, `docs/diagrams/*.bkp`, `revise_flows.py`) — **không** đụng, **không** stage.
- [ ] **Step 2:** `git switch -c feat/notification-narrative-ai-category` (từ HEAD hiện tại; không dùng `main`/`develop`).
- [ ] **Step 3: Mốc.** Run (trong `ai-service/`): `python -m pytest tests/unit -q -x -p no:cacheprovider` (Python 3.11 theo `TECH_STACK`; nếu chỉ có bản khác thì ghi rõ). Run (trong `backend/`): `dotnet test --filter "FullyQualifiedName~AnalyzeNotification|FullyQualifiedName~ChatParseConsent|FullyQualifiedName~ClassifyItemsClient" --nologo -v q`. Expected: ghi lại số pass/fail/skip có sẵn (tests integration cần Postgres sẽ skip) làm mốc; lỗi có sẵn không thuộc kế hoạch này.

### Task 1: Tài liệu `narrative_labels` và hàm tách nội dung

**Files:**
- Create: `ai-service/app/data/narrative_labels.json`, `ai-service/app/services/narrative.py`
- Modify: `ai-service/app/services/config_documents.py` (thêm `_validate_narrative_labels`, đăng ký `SPECS["narrative_labels"]`)
- Test: `ai-service/tests/unit/test_narrative_labels_document.py`; sửa tập tên tài liệu trong `ai-service/tests/integration/test_config_documents_db.py:195` (thêm `"narrative_labels"`) và mọi test khác liệt kê đúng tập `SPECS` (grep `"merchant_lexicon"` trong `tests/` để tìm)

**Interfaces:**
- Produces (`app/services/narrative.py`):
```python
def default_data() -> dict[str, list[str]]          # đọc app/data/narrative_labels.json: {"labels": [...]}
def build(content: dict) -> tuple[str, ...]         # nhãn đã fold(), không trùng, dài trước
def apply(content: dict) -> None                    # đổi nhãn đang chạy trong tiến trình (dựng xong mới gán)
def current_data() -> dict[str, list[str]]
def extract_description(text: str) -> str | None    # phần sau nhãn; None nếu không có nhãn hợp lệ
```
- Quy tắc `extract_description`: so khớp không phân biệt hoa thường/dấu (dùng `app.services.vi_text.fold`, ánh xạ vị trí về chuỗi gốc); nhãn phải là **nguyên từ** và đứng **đầu mệnh đề** (đầu chuỗi hoặc sau `.`, `|`, `;`, xuống dòng + khoảng trắng), theo sau bởi `:` hoặc khoảng trắng; lấy lần xuất hiện **đầu tiên**; trả phần còn lại đã bỏ khoảng trắng và ký tự ` :.-|` ở hai đầu, tối đa 255 ký tự; rỗng → `None`.
- Validate (`RuleError` qua `_invalid`, giống `_validate_merchant_lexicon`): `content == {"labels": [...]}` đúng một khóa; mỗi nhãn là chuỗi đã fold (chữ thường, không dấu, gọn khoảng trắng), 2–20 ký tự, không trùng, tối đa 30 nhãn.
- Đăng ký: `Spec("narrative_labels", "Nhãn 'nội dung' trong thông báo ngân hàng", ("ai-service",), narrative.default_data, _validate_narrative_labels, narrative.apply, None)`.

- [ ] **Step 1: Viết test (thất bại)** trong `test_narrative_labels_document.py`: `extract_description` với: `"TK 1234 GD: -1,000,000VND luc 29-10-2026 09:15. SD: 4,915,000VND. ND: mua quan ao"` → `"mua quan ao"`; `"...SD: 4,915,000VND. ND: Thanh toan ca phe"` → `"Thanh toan ca phe"`; `"So du 1,750,000 VND. Ref MBVCB.987654321. ND THANH TOAN GRAB"` → `"THANH TOAN GRAB"`; `"TK 0123|GD:-75,000VND|SD:2,500,000VND|Nội dung: Ăn trưa"` → `"Ăn trưa"`; `"NGUYEN VAN ND chuyen tien"` → `None`; `"TK 1 GD: -5,000VND ND:"` → `None`; không nhãn → `None`; kết quả dài 400 ký tự bị cắt còn 255. Validate: nhãn trùng, có dấu/chữ hoa, 1 ký tự, hơn 30 nhãn, thiếu khóa `labels`, thêm khóa lạ → `RuleError`; bản mặc định hợp lệ. `apply` đổi hành vi (thêm nhãn `ghi chu` thì `"...SD: 1. Ghi chu: cafe"` → `"cafe"`) và khôi phục bằng `apply(default_data())` trong `finally`/fixture.
- [ ] **Step 2:** Run: `python -m pytest tests/unit/test_narrative_labels_document.py -q`. Expected: FAIL (module chưa có).
- [ ] **Step 3:** Cài đặt `narrative.py`, file JSON seed và đăng ký Spec như Interfaces; cập nhật các test liệt kê tập `SPECS`.
- [ ] **Step 4:** Chạy lại test mới + `python -m pytest tests/unit/test_config_documents.py tests/unit/test_merchant_lexicon_document.py -q`. Expected: PASS.
- [ ] **Step 5: Checkpoint:** `black --check`, `ruff check`, `isort --check` trên các file đã đổi sạch; commit (`git add` đúng các file của task, thông điệp `feat(ai-service): narrative_labels config document and description extraction`).

### Task 2: Seed `narrative_labels` vào DB lúc khởi động

**Files:**
- Modify: `ai-service/app/services/config_documents.py` (thêm trường `seed: bool = False` vào `Spec`, hàm `seed_defaults`), `ai-service/app/core/config.py` (`seed_config_documents: bool = True`), `ai-service/app/main.py` (gọi trong `lifespan` TRƯỚC `config_runtime.refresh()`)
- Test: `ai-service/tests/integration/test_config_documents_db.py` (cùng kiểu test hiện có; cần Postgres, skip nếu không có) + `ai-service/tests/unit/test_config_documents.py` (phần không cần DB)

**Interfaces:**
- Consumes: `Spec`, `SPECS`, `ConfigDocument` (cột `name, version, status, content, note, created_by`), `narrative_labels` Spec (Task 1; đặt `seed=True` cho riêng nó).
- Produces:
```python
async def seed_defaults(session: AsyncSession) -> dict[str, int]
# Với mỗi Spec có seed=True: nếu CHƯA có dòng nào cùng tên thì thêm version 1, status="active",
# content=spec.default(), note="seed từ bản mặc định", created_by="system"; có rồi thì bỏ qua.
# Trả {tên: 1 nếu vừa thêm, 0 nếu bỏ qua}. Chạy lại bao nhiêu lần cũng không đổi gì; commit một lần.
```

- [ ] **Step 1: Viết test (thất bại).** Unit: `Spec` có trường `seed` mặc định `False` và chỉ `narrative_labels` có `seed=True`. Integration (kiểu test hiện có): DB trống → `seed_defaults` thêm đúng 1 dòng active version 1 cho `narrative_labels`; gọi lần hai → `{"narrative_labels": 0}` và vẫn 1 dòng; đã có phiên bản (do admin) → không thêm; `active_document` trả `source == "db"`.
- [ ] **Step 2:** Run: `python -m pytest tests/unit/test_config_documents.py -q` và test integration mới. Expected: FAIL.
- [ ] **Step 3:** Cài đặt `seed_defaults`; trong `lifespan` bọc `try/except Exception` như các bước seed khác (`if settings.seed_config_documents:` mở `AsyncSessionLocal`, ghi log `config_documents_seeded` với số đếm, lỗi chỉ log `error_type`), đặt TRƯỚC lệnh `config_runtime.refresh()` hiện có.
- [ ] **Step 4:** Chạy lại các test trên. Nếu không có Postgres, nói rõ test integration bị skip và chứng minh bằng test unit; ghi lệnh thủ công để chạy sau (`docker compose up -d` rồi `pytest tests/integration/test_config_documents_db.py`).
- [ ] **Step 5: Checkpoint:** công cụ định dạng sạch; commit (`feat(ai-service): seed narrative_labels into config_documents at startup`).

### Task 3: Extractor dùng phần nội dung làm `description`

**Files:**
- Modify: `ai-service/app/pipeline/stages/extractor.py` (`ExtractionOutcome`, `extract_generic`, `_from_pattern`)
- Test: `ai-service/tests/unit/test_extractor.py` (thêm ca mới, giữ nguyên ca cũ)

**Interfaces:**
- Consumes: `narrative.extract_description(text: str) -> str | None` (Task 1).
- Produces: `ExtractionOutcome` thêm trường `description_is_narrative: bool = False` (cuối dataclass, có mặc định). Nghĩa: `description` đúng là phần nội dung (do nhãn hoặc trường mô tả của định dạng tách ra), an toàn để gửi AI.
- Hành vi: `extract_generic`: `narrative = extract_description(normalized)`; `description = narrative or normalized[:255]`, `description_is_narrative = narrative is not None`; `merchant` giữ cách tính cũ trên cả đoạn (`_merchant_after_preposition`). `_from_pattern`: nếu `matched.description` có → giữ, `description_is_narrative = True`; nếu không → `narrative = extract_description(normalize_whitespace(body))`, `description = narrative or normalize_whitespace(body)[:255]`, cờ = `narrative is not None`; `merchant` giữ như cũ.

- [ ] **Step 1: Viết test (thất bại):** `extract_generic("TK 1234 GD: -1,000,000VND luc 29-10-2026 09:15. SD: 4,915,000VND. ND: mua quan ao", now)` cho `amount_cents == 1_000_000`, `description == "mua quan ao"`, `description_is_narrative is True`; cùng mẫu với `"... ND: Thanh toan ca phe"`; thông báo generic không nhãn (ví dụ `"Ban da chi 250,000 VND tai Highlands"`) giữ `description` = cả đoạn và `description_is_narrative is False`; nhánh pattern có `matched.description` giữ nguyên và cờ `True` (dùng `matcher` fixture, mẫu MB sẵn có); ca cũ trong file vẫn pass.
- [ ] **Step 2:** Run: `python -m pytest tests/unit/test_extractor.py -q`. Expected: ca mới FAIL.
- [ ] **Step 3:** Cài đặt như Interfaces.
- [ ] **Step 4:** Chạy lại `tests/unit/test_extractor.py tests/unit/test_provider_format_matching.py tests/unit/test_provider_patterns.py -q`. Expected: PASS.
- [ ] **Step 5: Checkpoint:** công cụ định dạng sạch; commit (`feat(ai-service): extractor keeps only the narrative as description when a label is found`).

### Task 4: Bước phân loại bằng AI (hàm thuần, chưa nối pipeline)

**Files:**
- Create: `ai-service/app/pipeline/stages/ai_categorizer.py`
- Modify: `ai-service/app/data/confidence.json` (khóa `categorizer.ai`), `ai-service/app/core/config.py` (`notification_ai_timeout_seconds: float = 6.0`)
- Test: `ai-service/tests/unit/test_notification_ai_category.py`, `ai-service/tests/unit/test_confidence.py` (thêm ràng buộc)

**Interfaces:**
- Consumes: `CategorizationOutcome(category_slug, confidence, method, model_version=None)` từ `categorizer.py`; `ExtractionOutcome` (`description`, `description_is_narrative`, `transaction_type`); `classify_items(items, merchant, knowledge, gemini, instruction, allow_ai, stats=None)` từ `app/bill/item_classifier.py`; `ClassifyItemIn(name=..., parent=None)` từ `app/schemas/bill.py`; `anonymize_body` (`app/core/anonymizer.py`); `resolve` (`app/services/category_resolver.py`); `FALLBACK_CATEGORY_SLUG`, `CATEGORY_SLUGS`, `TransactionType` (`app/core/enums.py`); `INCOME_SLUG` (`app/core/category_map.py`); `table.get` (`app/core/confidence.py`).
- Produces:
```python
@dataclass(frozen=True)
class AiDeps:
    gemini: GeminiClient
    knowledge: BillKnowledge
    instruction: str

async def categorize_with_ai(
    outcome: CategorizationOutcome, extraction: ExtractionOutcome, allow_ai: bool,
    deps: AiDeps | None, *, timeout: float,
) -> CategorizationOutcome
```
- Hành vi: trả lại `outcome` nguyên vẹn nếu bất kỳ điều kiện nào trong Global Constraints không thỏa. Ngược lại: `phrase = anonymize_body(extraction.description)`; gọi `asyncio.wait_for(classify_items([ClassifyItemIn(name=phrase, parent=None)], None, deps.knowledge, deps.gemini, deps.instruction, True), timeout)`; kết quả có `source in ("ai", "cache")`, slug thuộc `CATEGORY_SLUGS`, khác `INCOME_SLUG` → `dataclasses.replace(outcome, category_slug=slug, confidence=table.get("categorizer.ai"))` (giữ `method`/`model_version`); mọi ngoại lệ → trả `outcome`, ghi log `notification_ai_failed` chỉ với `error_type`. Không log nội dung.
- `confidence.json`: `"categorizer.ai": {"value": 0.70, "doc": "<lý do: AI khác luật → luôn qua hộp thoại, dưới ngưỡng một chạm 0,85>"}`.

- [ ] **Step 1: Viết test (thất bại)** với `FakeGemini`/`FakeKnowledge` (import từ `tests.unit.test_item_classifier`, như `test_chat_ai.py`) và `outcome = CategorizationOutcome("other", 0.3, ExtractionMethod.RULE)`: gọi 1 lần với cụm `"mua quan ao"` → danh mục `shopping`, độ tin cậy `0.70`, `gemini.calls == [["mua quan ao"]]` (không có số tiền/dãy số); không gọi khi: `allow_ai=False`, `deps=None`, `outcome.category_slug != "other"` (từ điển đã khớp), `transaction_type == CREDIT`, `description_is_narrative=False`, nội dung sức khỏe (`"mua vi thuoc ha sot"`), cụm rỗng sau khi bỏ số tiền (`"50,000"`); dãy số dài trong cụm bị che trước khi gửi (`"ck 0123456789012 mua quan ao"` → gửi `"ck xxxxxxxxx9012 mua quan ao"` hoặc tương đương đã che); Gemini trả `unknown` → giữ `other`; `FakeGemini` ném `GeminiFailed`/`Exception` → giữ `other`, không ném; chậm quá `timeout` (`asyncio.sleep`) → giữ `other`; slug `income` hoặc ngoài `CATEGORY_SLUGS` → giữ `other`. `test_confidence.py`: thêm khẳng định `categorizer.ai < backend.one_tap_threshold` và `categorizer.ai > categorizer.fallback`.
- [ ] **Step 2:** Run: `python -m pytest tests/unit/test_notification_ai_category.py tests/unit/test_confidence.py -q`. Expected: FAIL.
- [ ] **Step 3:** Cài đặt `ai_categorizer.py`, khóa độ tin cậy và `Settings.notification_ai_timeout_seconds`.
- [ ] **Step 4:** Chạy lại hai file test trên. Expected: PASS.
- [ ] **Step 5: Checkpoint:** công cụ định dạng sạch; commit (`feat(ai-service): AI category step for notification narratives (pure function)`).

### Task 5: Nối vào pipeline và hợp đồng `allow_ai` phía ai-service

**Files:**
- Modify: `ai-service/app/schemas/request.py` (`AnalyzeRequest.allow_ai: bool = False`), `ai-service/app/pipeline/orchestrator.py` (`Pipeline.analyze(session, request, ai: AiDeps | None = None)`), `ai-service/app/api/v1/pipeline.py` (route nhận `AiDeps` qua `Depends`)
- Test: `ai-service/tests/integration/` — theo test `/api/v1/analyze` hiện có (grep `/api/v1/analyze` trong `tests/integration`); thêm `ai-service/tests/unit/test_analyze_request_schema.py`

**Interfaces:**
- Consumes: `AiDeps`, `categorize_with_ai` (Task 4); `get_chat_gemini_client`, `get_bill_knowledge`, `get_ai_instruction` từ `app/api/v1/bill.py`; `settings.notification_ai_timeout_seconds`.
- Produces: `async def analyze(self, session, request, ai: AiDeps | None = None) -> AnalyzeResponse` (mặc định `None` → không AI, mọi lời gọi cũ không đổi). Trong `analyze`, ngay sau `Categorizer(...).categorize(...)` và TRƯỚC `duplicate_detector.detect`: `categorization = await categorize_with_ai(categorization, extraction, request.allow_ai, ai, timeout=settings.notification_ai_timeout_seconds)`. Route: tham số `gemini`, `knowledge`, `instruction` qua `Depends(get_chat_gemini_client)`, `Depends(get_bill_knowledge)`, `Depends(get_ai_instruction)` rồi dựng `AiDeps` truyền vào `pipeline.analyze`.

- [ ] **Step 1: Viết test (thất bại).** Unit: `AnalyzeRequest` không có `allow_ai` → `False`; có `allow_ai=True` → `True`; JSON cũ (không trường này) vẫn hợp lệ. Integration (Postgres; skip nếu không có; dùng `app.dependency_overrides` thay `get_chat_gemini_client`, `get_bill_knowledge`, `get_ai_instruction` bằng bản giả như `test_chat_ai.py`): thông báo `"TK 1234 GD: -1,000,000VND luc 29-10-2026 09:15. SD: 4,915,000VND. ND: mua quan ao"` gói chưa có định dạng, `allow_ai=true`, AI giả trả `shopping` → response `description == "mua quan ao"`, `categorization.category_slug == "shopping"`, `confidence == 0.70`; cùng thông báo với `allow_ai` vắng → `other` và AI giả không được gọi; AI giả ném lỗi → vẫn 200 với `other`; cùng nội dung lần hai (khác `backend_request_id`) → bộ nhớ trả lời, AI giả không bị gọi lại (nếu `FakeKnowledge` hỗ trợ); khoản tiền vào + `allow_ai=true` → `income`, AI không gọi.
- [ ] **Step 2:** Run: `python -m pytest tests/unit/test_analyze_request_schema.py -q` và test integration mới. Expected: FAIL.
- [ ] **Step 3:** Cài đặt như Interfaces; giữ nguyên cấu trúc `AnalyzeResponse`.
- [ ] **Step 4:** Chạy lại các test trên cùng `python -m pytest tests/unit -q` (không có test mới nào đỏ so với mốc Task 0); nếu không có Postgres ghi rõ test integration bị skip.
- [ ] **Step 5: Checkpoint:** công cụ định dạng sạch; commit (`feat(ai-service): wire AI category step and allow_ai into /analyze`).

### Task 6: Backend gửi cờ đồng ý AI (`allow_ai`)

**Files:**
- Modify: `backend/FinMate.Application/Common/Interfaces/IAIServiceClient.cs` (record `AnalyzeRequest`), `backend/FinMate.Infrastructure/ExternalServices/AIServiceApiContracts.cs` (`AnalyzeApiRequest`), `backend/FinMate.Infrastructure/ExternalServices/AIServiceClient.cs` (map), `backend/FinMate.Application/Notifications/Commands/AnalyzeNotificationCommandHandler.cs`
- Test: `backend/FinMate.Tests/Unit/Application/Notifications/AnalyzeNotificationCommandHandlerTests.cs`; test hợp đồng Infrastructure theo mẫu `backend/FinMate.Tests/Unit/Infrastructure/ChatParseConsentClientTests.cs` và `ClassifyItemsClientTests.cs`

**Interfaces:**
- Produces:
```csharp
public record AnalyzeRequest(Guid BackendRequestId, Guid UserId, string PackageName, string? NotificationTitle,
    string NotificationBody, DateTimeOffset ReceivedAt, bool AllowAi = false);          // tham số cuối, có mặc định
public record AnalyzeApiRequest(/* các trường cũ */, [property: JsonPropertyName("allow_ai")] bool AllowAi = false);
// AnalyzeNotificationCommandHandler: thêm tham số CUỐI của constructor `IAiConsentReader? consent = null`
// (giống ChatCommandHandler); trước khi gọi IAIServiceClient.AnalyzeAsync:
//   var allowAi = _consent is not null && await _consent.HasAiConsentAsync(command.UserId, ct);
```
- `RetryFailedNotificationJob` và mọi nơi khác dựng `AnalyzeRequest` giữ nguyên (nhận mặc định `false`). Đăng ký DI không đổi (`IAiConsentReader` đã được đăng ký trong `Program.cs`).

- [ ] **Step 1: Viết test (thất bại):** trong `AnalyzeNotificationCommandHandlerTests` (dùng `Mock<IAiConsentReader>`, thêm vào constructor của handler trong fixture): có đồng ý → `AnalyzeRequest.AllowAi == true` (bắt bằng `Mock.Verify`/`Callback`); không đồng ý → `false`; không truyền bộ đọc (`null`) → `false`; log trùng/Ignored → không gọi bộ đọc đồng ý (không đọc DB thừa). Hợp đồng: `AnalyzeApiRequest` tuần tự hóa ra `"allow_ai":true/false` và client map đúng `AllowAi`.
- [ ] **Step 2:** Run: `dotnet test --filter "FullyQualifiedName~AnalyzeNotification|FullyQualifiedName~AIServiceClient" --nologo -v q` (trong `backend/`). Expected: FAIL (biên dịch/khẳng định).
- [ ] **Step 3:** Cài đặt như Interfaces; giữ method ≤ 50 dòng (tách `private` nếu cần), không magic string.
- [ ] **Step 4:** Chạy lại bộ lọc trên và `dotnet build --nologo -v q` (0 warning mới). Expected: PASS.
- [ ] **Step 5: Checkpoint:** commit (`feat(backend): send allow_ai consent flag to ai-service /analyze`).

### Task 7: Tổng kiểm tra và cập nhật TASKS.md

**Files:**
- Modify: `.context/TASKS.md` (đánh dấu và ghi chú ngắn theo AGENTS.md §6)

- [ ] **Step 1:** Run (trong `ai-service/`): `python -m pytest tests/unit -q`; so với mốc Task 0, không test mới nào đỏ. Nếu có Postgres: `python -m pytest tests/integration -q`.
- [ ] **Step 2:** Run (trong `backend/`): `dotnet build --nologo -v q` (0 warning mới) và `dotnet test --nologo -v q` (hoặc bộ lọc các test liên quan nếu tests integration cần Docker không chạy được; ghi rõ).
- [ ] **Step 3:** Chạy thử tay (nếu có Postgres/ai-service): gửi `POST /api/v1/analyze` với `notification_body` mẫu của người dùng và `allow_ai` true/false, ghi lại `description`/danh mục; ghi lại việc `GEMINI_API_KEY` có hay không (không có khóa → AI bị bỏ qua, đúng thiết kế).
- [ ] **Step 4:** Cập nhật `.context/TASKS.md` (một mục mới ngắn: tính năng, các file chính, mốc kiểm tra, các điều chưa kiểm chứng như Gemini thật). KHÔNG sửa `ARCHITECTURE.md`; nếu thấy §3.3 (hợp đồng analyze) cần thêm `allow_ai`, ghi thành đề xuất cho người dùng duyệt.
- [ ] **Step 5: Checkpoint:** commit (`docs: update TASKS.md for notification narrative and AI category`); báo cáo gồm danh sách file đã đổi, kết quả test và những gì chưa chạy được.
