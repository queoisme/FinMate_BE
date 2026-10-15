# Local-first cho chat và sửa lỗi chat Implementation Plan

> **Đã chạy xong** (Giai đoạn A, B nằm trong git). Kế hoạch tiếp theo: `2026-10-16-local-first-summary-and-chat-fixes.md`.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** (1) Sửa các lỗi chat đã tái hiện được; (2) đưa phần quyết định và dữ liệu cá nhân của chat về database trên máy, để server chỉ còn phân tích câu chữ, theo hướng "dữ liệu cá nhân ở máy, server giữ tài khoản, đồng bộ là bước riêng".

**Architecture:** Giai đoạn A sửa `ai-service` (độc lập kiến trúc). Giai đoạn B thêm một endpoint backend KHÔNG đọc/ghi dữ liệu cá nhân (chỉ chuyển câu sang `ai-service`), rồi app tự ghi giao dịch vào database trên máy và đẩy lên server qua hàng đợi đồng bộ có sẵn. Giai đoạn B chạy được trong cả hai trạng thái cuối của kiến trúc nên không cần chờ quyết định "server có giữ giao dịch đã đồng bộ hay không".

**Tech Stack:** FastAPI (ai-service), ASP.NET Core 9 + xUnit/Moq (backend), Flutter + drift + Riverpod (app).

**Spec:** Không có spec riêng. Căn cứ: hướng kiến trúc owner nêu ngày 2026-10-13 (ghi trong bộ nhớ làm việc `finmate-local-first-architecture`), các lỗi tái hiện bằng `parse_chat` cùng ngày (xem Giai đoạn A), và đọc code: app chỉ có 2 bảng drift (`LocalTransactions`, `SyncQueue`), `ChatCommandHandler` ghi giao dịch trên server, `AuthController.onSignedOut` gọi `clearAll()` xóa database trên máy. Mọi quyết định là provisional.

## Global Constraints

- Tiền luôn là `BIGINT` đồng (int64), không float. ID luôn là UUID. Thời gian luôn có múi giờ.
- Endpoint mới của backend không được nhận repository nào của giao dịch, ví hay báo cáo (kiểm bằng test cấu trúc).
- Không log câu chat, số tiền hay danh mục (AGENTS.md §3.2).
- Giao dịch ghi ở máy phải có `clientRequestId` và vào `SyncQueue`; server vẫn chống trùng bằng `UNIQUE (user_id, client_request_id)` có sẵn.
- Đường chat cũ (`POST /transactions/chat`) giữ nguyên cho đến khi đường mới được bật mặc định; không xóa trong kế hoạch này.
- Tiếng Việt có dấu khớp theo văn bản có dấu (bộ đọc số bằng chữ dựa vào dấu, xem CLAUDE.md Phase 10).
- Mọi commit kết thúc bằng `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`. Không push, không merge.

## Review Focus

- Đăng xuất làm mất giao dịch chưa đồng bộ: `clearAll()` hiện xóa cả hàng đợi. Phải giữ dữ liệu chưa đồng bộ của người dùng đó (Task 6 test `logout_keeps_unsynced_rows_for_that_user`).
- Hai tài khoản dùng chung một máy: dữ liệu của người này không được hiện cho người kia (Task 6 test `other_user_never_sees_rows`).
- Mất mạng giữa lúc ghi bằng giọng nói hoặc chat: giao dịch đã ghi ở máy không bị mất và không bị ghi hai lần khi đồng bộ lại (Task 8 test `replay_does_not_duplicate`).
- Câu "không cần hủy, cứ ghi" hay tên món có chữ "tổng" ("tổng hợp"): không bị coi là hủy hoặc tổng tiền (Task 1 và 2 test phủ định giả).
- Server mới chưa hỗ trợ trường `category_source`: app phải chạy bình thường khi trường vắng (Task 4 test `app_tolerates_missing_category_source`).

---

## File Structure

- ai-service: `app/chat/clauses.py`, `app/chat/parser.py`, `app/chat/amount.py`, `app/schemas/chat.py`, `app/api/v1/chat.py`; tests `tests/unit/test_chat_clauses.py`, `test_chat_parser.py`, `test_chat_api.py` (+ `tests/unit/test_chat_amount.py` nếu chưa có).
- Backend: `FinMate.API/Controllers/ChatAnalyzeController.cs` (mới), `FinMate.Application/Transactions/Commands/ChatAnalyzeCommandHandler.cs` (mới); tests `FinMate.Tests/Unit/Application/Transactions/ChatAnalyzeTests.cs`, `FinMate.Tests/Integration/Controllers/ChatAnalyzeControllerTests.cs`.
- App: `lib/core/database/tables/` (+`user_category_memory_table.dart`), `lib/core/database/app_database.dart` (schema v2, bộ nhớ theo người dùng), `lib/features/assistant/data/local_chat_decider.dart` (mới), `lib/features/assistant/data/assistant_repository.dart` (+`analyze`), `lib/features/assistant/presentation/controllers/assistant_controller.dart`, `lib/features/auth/presentation/controllers/auth_controller.dart`.

---

## Giai đoạn A — sửa lỗi chat (không phụ thuộc kiến trúc)

### Task 1: Phủ định và hủy ("rồi hủy", "nhầm")

**Files:** Modify `ai-service/app/chat/parser.py`, `ai-service/app/chat/clauses.py`; Test `ai-service/tests/unit/test_chat_parser.py`.

**Interfaces:**
- Produces: hàm `is_cancelled(clause: str) -> bool` trong `clauses.py`; `parse_chat` bỏ khoản bị hủy; khi mọi khoản đều bị hủy trả `intent = "out_of_scope"` với `unclear_reason = "cancelled"` (không thêm intent mới để backend và app cũ không phải đổi).

- [ ] **Step 1: Viết test (RED)**: `"mua cà phê 30k rồi hủy"` → không có entry nào, `intent == "out_of_scope"` và `unclear_reason == "cancelled"`; `"mua cà phê 30k, nhầm, là 35k"` → một entry 35000; `"ăn phở 40k rồi hủy, mua cà phê 30k"` → một entry 30000; giả: `"không cần hủy, ghi cà phê 30k"` → một entry 30000; `"mua hủ tiếu 30k"` → một entry 30000 (không bắt nhầm chữ "hủ").
- [ ] **Step 2: Chạy** `pytest tests/unit/test_chat_parser.py -k cancel -q`. Expected: FAIL (hiện ghi 30000 cho câu đầu).
- [ ] **Step 3: Implement** `is_cancelled` bằng cụm từ đứng sau số tiền trong cùng mệnh đề ("hủy", "huỷ", "thôi", "không mua nữa", "bỏ") và `parse_chat` loại mệnh đề đó; "nhầm ... là X" lấy X là số tiền cuối. Từ khóa dùng ranh giới từ, không so chuỗi con.
- [ ] **Step 4: Chạy** `pytest tests/unit -q`. Expected: PASS.
- [ ] **Step 5: Commit** `fix(chat): drop cancelled and corrected items`.

### Task 2: "Tổng cộng" không phải một khoản chi

**Files:** Modify `ai-service/app/chat/clauses.py`, `parser.py`, `app/schemas/chat.py` (+`ChatParseResponse.total_amount: int | None`, `total_mismatch: bool`); Test `test_chat_parser.py`, `test_chat_api.py`.

**Interfaces:**
- Produces: mệnh đề chứa "tổng cộng"/"tổng"/"tất cả" + số tiền không thành `Entry`; số đó vào `ChatParse.total_amount`; `total_mismatch = True` khi tổng khác tổng các khoản.

- [ ] **Step 1: Viết test (RED)**: `"mua bánh mì 25k và cà phê 35k tổng cộng 60k"` → 2 entry (25000, 35000), `total_amount == 60000`, `total_mismatch False`; cùng câu với "tổng cộng 70k" → `total_mismatch True`; giả: `"mua bộ tổng hợp đề 150k"` → 1 entry 150000, `total_amount None`.
- [ ] **Step 2: Chạy** `pytest tests/unit/test_chat_parser.py -k total -q`. Expected: FAIL.
- [ ] **Step 3: Implement** nhận diện nhãn tổng đứng ĐẦU hoặc CUỐI mệnh đề như một từ riêng (không phải chuỗi con của từ khác); response API chuyển hai trường mới.
- [ ] **Step 4: Chạy** `pytest tests/unit -q`. Expected: PASS.
- [ ] **Step 5: Commit** `fix(chat): treat stated totals as a check, not an expense`.

### Task 3: "Hai trăm rưỡi" và mệnh đề thiếu tiền

**Files:** Modify `ai-service/app/chat/amount.py`, `parser.py`, `app/schemas/chat.py` (+`ChatParseResponse.skipped: list[str]`); Test `tests/unit/test_chat_amount.py` (tạo nếu thiếu), `test_chat_parser.py`.

**Interfaces:**
- Produces: `parse_amount` đọc "N trăm rưỡi" bằng đúng giá trị của "N trăm năm mươi"; `ChatParse.skipped: tuple[str, ...]` = các mệnh đề trong câu nhiều khoản không có số tiền; không còn gộp im lặng.

- [ ] **Step 1: Viết test (RED)**: `parse_amount("ăn cơm hai trăm rưỡi").value == parse_amount("ăn cơm hai trăm năm mươi").value`; `"một triệu rưỡi"` vẫn 1.500.000 (không đổi); `"ăn phở 40k và cà phê"` → 1 entry 40000 mô tả "ăn phở" và `skipped == ("cà phê",)`.
- [ ] **Step 2: Chạy** `pytest tests/unit/test_chat_amount.py tests/unit/test_chat_parser.py -k "ruoi or skipped" -q`. Expected: FAIL.
- [ ] **Step 3: Implement** theo bảng trên; `skipped` chỉ có khi có ≥1 entry và ≥1 mệnh đề thiếu tiền.
- [ ] **Step 4: Chạy** `pytest tests/unit -q` và `python scripts/eval_flows.py --chat`. Expected: PASS, độ chính xác không thấp hơn mốc trong `docs/evaluation.md`.
- [ ] **Step 5: Commit** `fix(chat): half-hundreds and amount-less clauses`.

---

## Giai đoạn B — quyết định và dữ liệu cá nhân về máy

### Task 4: `ai-service` báo nguồn của danh mục

**Files:** Modify `app/schemas/chat.py` (+`ChatEntry.category_source: str | None`), `app/chat/parser.py` (`Entry.category_source`), `app/api/v1/chat.py` (`_with_cached_categories` đặt `"cache"`); Test `test_chat_parser.py`, `test_chat_api.py`.

**Interfaces:** Produces `category_source ∈ {"rule", "cache", None}`; `"rule"` khi từ `expense_category`, `"cache"` khi từ `_with_cached_categories`, `None` khi chưa có danh mục.

- [ ] **Step 1: Viết test (RED)**: `"ăn phở 45k"` → `"rule"`; mục đã có trong `bill_item_cache` → `"cache"` và `auto_save_ok False`; không đoán được → `None`.
- [ ] **Step 2: Chạy** `pytest tests/unit/test_chat_parser.py tests/unit/test_chat_api.py -k category_source -q`. Expected: FAIL.
- [ ] **Step 3: Implement** như Interfaces.
- [ ] **Step 4: Chạy** `pytest tests/unit -q`. Expected: PASS.
- [ ] **Step 5: Commit** `feat(chat): report where a category came from`.

### Task 5: Endpoint backend không dữ liệu cá nhân

**Files:** Create `ChatAnalyzeCommandHandler.cs`, `ChatAnalyzeController.cs`; đăng ký DI trong `Program.cs`; Test `ChatAnalyzeTests.cs`, `ChatAnalyzeControllerTests.cs`.

**Interfaces:**
- Consumes: `IAIServiceClient.ParseChatAsync(ChatParseRequest, ct)` (có sẵn), `ISttServiceClient.TranscribeAsync` (có sẵn).
- Produces: `POST /api/v1/chat/analyze` body `{text}` → `ApiResponse<ChatParseResponse>` (nguyên kết quả `ai-service`, thêm `total_amount`, `total_mismatch`, `skipped`, `category_source`); `POST /api/v1/chat/analyze-voice` (multipart `file`) → `{transcript, parse}`. `ChatAnalyzeCommandHandler` chỉ nhận `IAIServiceClient` và `ISttServiceClient` trong constructor.

- [ ] **Step 1: Viết test (RED)**: `Handler_ConstructorTakesNoRepositories` (phản chiếu: không tham số nào có tên kết thúc `Repository`), `Analyze_ReturnsParseAsIs`, `AnalyzeVoice_EmptyTranscript_Throws VoiceNoSpeech`, tích hợp: cần đăng nhập (401 khi không có token), giới hạn câu 1–1000 ký tự, **không tạo giao dịch** (số hàng `transactions` không đổi sau gọi).
- [ ] **Step 2: Chạy** `dotnet test backend/FinMate.Tests --filter "FullyQualifiedName~ChatAnalyze"`. Expected: FAIL.
- [ ] **Step 3: Implement** handler và controller theo khuôn `VoiceChatCommandHandler`/`ChatCommandHandler` (kiểm âm thanh dùng lại hằng và logic chuẩn hóa kiểu MIME của `VoiceChatCommandHandler`); không đụng đường chat cũ.
- [ ] **Step 4: Chạy** bộ test Step 2 + `--filter "FullyQualifiedName~FinMate.Tests.Unit"`. Expected: PASS.
- [ ] **Step 5: Commit** `feat(be): stateless chat analysis endpoints`.

### Task 6: Database máy theo từng người dùng, đăng xuất không mất dữ liệu

**Files:** Modify `lib/core/database/app_database.dart` (schema v2 + migration), `lib/features/auth/presentation/controllers/auth_controller.dart`; Create `lib/core/database/tables/user_category_memory_table.dart`; Test `test/core/database/app_database_test.dart`.

**Interfaces:**
- Produces: bảng `UserCategoryMemory(merchantKey text, itemKey text, categorySlug text, confirmCount int, updatedAt datetime)`, khóa chính `(merchantKey, itemKey)`; `databaseProvider` trả database của người dùng hiện tại (tệp riêng theo hash userId); `onSignedOut` chỉ đóng kết nối, KHÔNG gọi `clearAll()`; hàm mới `AppDatabase.deleteAllForAccountDeletion()` dùng khi người dùng xóa tài khoản.

- [ ] **Step 1: Viết test (RED)**: `logout_keeps_unsynced_rows_for_that_user`, `other_user_never_sees_rows` (hai userId, hai tệp), `account_deletion_removes_file`, `migration_v1_to_v2_keeps_transactions`, `memory_upsert_increments_confirm_count`.
- [ ] **Step 2: Chạy** `flutter test test/core/database -r compact`. Expected: FAIL.
- [ ] **Step 3: Implement** theo Interfaces; migration v1→v2 chỉ thêm bảng. Dữ liệu v1 hiện có (một tệp chung) được gắn cho người dùng đăng nhập lần đầu sau nâng cấp.
- [ ] **Step 4: Chạy** `flutter test` và `flutter analyze`. Expected: PASS, analyze sạch.
- [ ] **Step 5: Commit** (repo FE) `feat(db): per-user local database that survives logout`.

### Task 7: Bộ quyết định chat chạy ở máy

**Files:** Create `lib/features/assistant/data/local_chat_decider.dart`; Modify `assistant_repository.dart` (+`analyze(String text) -> ChatAnalysis`); Test `test/features/assistant/local_chat_decider_test.dart` (dùng phản hồi thật của endpoint Task 5 làm fixture).

**Interfaces:**
- Consumes: Task 4/5 response; `UserCategoryMemory`; `LocalTransactions`.
- Produces: `class LocalChatDecider { Future<ChatDecision> decide(ChatAnalysis analysis, {required String accountId, required DateTime now}) }` với `sealed ChatDecision`: `SaveNow(DraftTransaction)`, `Drafts(List<DraftTransaction>, {duplicateOf: Map<int, String>})`, `Reply(String)`, `NeedSummary` (tổng hợp vẫn đi đường server, xem Ngoài phạm vi). Quy tắc y hệt `ChatCommandHandler`: ghi ngay khi `auto_save_ok` và không có khoản trùng (cùng số tiền, ±10 phút, trong database máy); nhiều khoản luôn nháp; bộ nhớ người dùng thắng `category_source == "cache"` và thắng `"rule"` chỉ khi `confirmCount >= 2`.

- [ ] **Step 1: Viết test (RED)**: `single_confident_expense_saves_now`, `multi_always_drafts`, `duplicate_within_10_minutes_asks`, `memory_overrides_cache_source`, `memory_overrides_rule_only_after_two_confirmations`, `app_tolerates_missing_category_source`, `skipped_clause_adds_reply_note`, `total_mismatch_adds_warning`.
- [ ] **Step 2: Chạy** `flutter test test/features/assistant/local_chat_decider_test.dart -r compact`. Expected: FAIL.
- [ ] **Step 3: Implement** `decide` và `AssistantRepository.analyze` (gọi `POST /api/v1/chat/analyze`).
- [ ] **Step 4: Chạy** `flutter test`. Expected: PASS.
- [ ] **Step 5: Commit** `feat(assistant): decide chat saves on the device`.

### Task 8: Nối bộ quyết định vào chat và giọng nói, ghi ở máy

**Files:** Modify `assistant_controller.dart`, `assistant_repository.dart` (+`analyzeVoice(path)`), nơi tạo giao dịch cục bộ của `transaction_repository.dart` (dùng hàm ghi hàng đợi có sẵn); Test `test/features/assistant/assistant_controller_test.dart`, `test/integration/app_flow_test.dart`.

**Interfaces:**
- Consumes: Task 5-7. Cờ `AppConfig.localFirstChat` (mặc định `false`); bật thì `send`/`sendVoice` đi `analyze` → `LocalChatDecider`; `SaveNow`/xác nhận nháp ghi `LocalTransactions` (`syncStatus = pending`, `clientRequestId` mới) và `SyncQueue`; mỗi lần xác nhận hoặc sửa danh mục cập nhật `UserCategoryMemory`.

- [ ] **Step 1: Viết test (RED)**: `local_save_writes_row_and_queue_item`, `replay_does_not_duplicate` (đồng bộ lại cùng `clientRequestId` không tạo hàng thứ hai), `offline_save_survives_and_syncs_later`, `confirming_draft_updates_memory`, `flag_off_uses_old_path` (hành vi cũ nguyên vẹn), `voice_uses_analyze_voice_when_flag_on`.
- [ ] **Step 2: Chạy** `flutter test test/features/assistant test/integration -r compact`. Expected: FAIL.
- [ ] **Step 3: Implement** nối controller; giữ nguyên đường cũ khi cờ tắt.
- [ ] **Step 4: Chạy** `flutter test` và `flutter analyze`; thử tay trên máy thật với cờ bật rồi tắt. Expected: PASS.
- [ ] **Step 5: Commit** `feat(assistant): local-first chat and voice behind a flag`.

---

## Giai đoạn C — kế hoạch riêng (cố ý không làm ở đây)

1. **Tính tổng ("tháng này tôi tiêu bao nhiêu") ở máy:** cần toàn bộ lịch sử giao dịch nằm ở máy; hiện database máy chỉ là bộ đệm từng trang. Cần bước kéo đầy đủ một lần và cờ "lịch sử đã đủ". Đến lúc đó câu hỏi tổng vẫn đi đường server.
2. **Ví, danh mục, ngân sách, báo cáo, mục tiêu tiết kiệm ở máy:** hiện chỉ có ở server. Đây là phần lớn công sức của hướng local-first.
3. **Đồng bộ lên server thành tùy chọn có mã hóa:** hiện hàng đợi luôn đẩy lên. Cần cờ người dùng và quyết định "server có được giữ giao dịch đã đồng bộ để chạy cảnh báo ngân sách hay không".
4. **Nhãn học do app gửi:** kế hoạch vòng học (`2026-10-13-learning-loop.md`) Task 3 gắn nhãn ở backend lúc giao dịch được lưu; nếu giao dịch không còn qua server thì nhãn cuối phải do app gửi (có đồng ý, ẩn danh). Task 1, 2, 4, 5 của kế hoạch đó không đổi.
5. **Chat khi mất mạng:** phân tích câu cần server (Rasa, luật); ngoại tuyến hiện chỉ báo lỗi.

## Rủi ro đã biết

- Chuyển hẳn sang local-first là việc nhiều tuần; kế hoạch này chỉ chuyển phần chat và giữ mọi thứ khác chạy như cũ nhờ cờ `localFirstChat`.
- Số dư ví trên server sẽ chậm cập nhật cho đến khi hàng đợi đồng bộ chạy; ví và báo cáo vẫn do server tính trong giai đoạn này.
- Ngưỡng "bộ nhớ thắng luật khi xác nhận ≥ 2 lần" là giả định, chỉnh khi có dữ liệu dùng thật.
