# Local-first bước tiếp theo (tổng hợp chi tiêu ở máy) và sửa lỗi chat đo ngày 2026-10-16 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** (1) Sửa các lỗi chat/giọng nói mà lần đo lại ngày 2026-10-16 tìm thấy; (2) đưa thêm một lát có giá trị của local-first về máy: câu hỏi "tháng này tôi tiêu bao nhiêu" được tính từ database trên máy, server chỉ còn phân tích câu chữ.

**Architecture:** Giai đoạn A chỉ sửa `ai-service` (không đụng kiến trúc). Giai đoạn B làm trong app: kéo đủ lịch sử giao dịch về máy một lần (`historyComplete`), lưu danh mục ở máy, tính tổng hợp bằng SQL trên database máy với đúng quy tắc của server; chưa đủ điều kiện thì vẫn đi đường server như hiện nay. Mọi task B chạy đúng trong cả hai trạng thái cuối của kiến trúc (server có hoặc không còn giữ giao dịch đã đồng bộ), nên không phải chờ quyết định D1 ở cuối kế hoạch.

**Tech Stack:** FastAPI + pytest (ai-service, chạy trong container), Flutter + drift + Riverpod (app).

**Spec:** Không có spec riêng. Căn cứ: kế hoạch đã chạy xong `2026-10-13-local-first-and-chat-fixes.md` (Giai đoạn A, B của nó đã nằm trong git: `40c55a1`..`9fe0ca9` và các commit FE `d0615ec`..`7a02ebf`), hướng kiến trúc owner nêu ngày 2026-10-13 (bộ nhớ làm việc `finmate-local-first-architecture`), và số đo ngày 2026-10-16 ghi ở từng task. Mọi quyết định là provisional.

## Global Constraints

- Tiền luôn là `BIGINT` đồng (int64), không float. ID luôn là UUID. Thời gian luôn có múi giờ; ngày của kỳ tổng hợp tính theo giờ Việt Nam (UTC+7), như `VietnamTime` ở backend.
- Quy tắc tổng hợp của server là chuẩn: chỉ giao dịch `Confirmed`; chi = `Debit`, thu = `Credit`; bỏ giao dịch có danh mục slug `loan`; chuyển khoản nội bộ (`Transfer`) không tính (nguồn: `ReportRepository.GetDailySpendAsync`, `GetCategorySpendAsync`).
- Không log câu chat, số tiền, danh mục (AGENTS.md §3.2).
- Đường chat cũ (`POST /transactions/chat`) giữ nguyên làm đường lùi; không xóa trong kế hoạch này.
- Tiếng Việt có dấu khớp theo văn bản có dấu (bộ đọc số bằng chữ dựa vào dấu, xem CLAUDE.md Phase 10).
- **Cây làm việc đang có thay đổi chưa commit của phiên khác** trong cùng các tệp (`amount_normalizer.py`, `transaction_parser.py`, `number_words.py`, `category_resolver.py`, `dictionaries/*.json`, `chat_cases.jsonl`...). Chỉ `git add` tệp của mình; nếu một tệp có cả thay đổi của phiên khác thì stage đúng phần của mình từ blob HEAD; kiểm tra bằng một worktree sạch ở HEAD trước khi báo xong (đã từng có test chỉ xanh nhờ phần chưa commit). FE không phải repo git: dùng `fe()` = `git --git-dir="/e/Ki 9/Model AI/FinMate_BE/.superpowers/fe.git" --work-tree=.`.
- Giữ CRLF của tệp (không dùng `sed -i` trên tệp CRLF).
- Mọi commit kết thúc bằng `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`. Không push, không merge.

## Review Focus

- Tổng hợp ở máy lệch tổng hợp của server: giao dịch `Draft`/`Pending`, `Transfer`, danh mục `loan` hoặc giao dịch ở ranh giới nửa đêm giờ VN bị tính khác (Task 7 test `matches_server_rules`, `midnight_vietnam_boundary`).
- Lịch sử chưa kéo đủ mà vẫn trả lời "tháng này tiêu bao nhiêu" bằng số thiếu, im lặng: phải rơi về server (Task 7 test `incomplete_history_falls_back_to_server`).
- Kéo lịch sử giữa chừng mất mạng: lần sau tiếp tục từ con trỏ cũ, không ghi đè hay xóa giao dịch `pending`/`failed` chưa đồng bộ của người dùng (Task 5 test `interrupted_pull_resumes`, `pull_keeps_unsynced_rows`).
- Hai tài khoản trên một máy: cờ `historyComplete` và danh mục là của từng người, không dùng chung (Task 5, 6 test `flag_is_per_user_file`).
- Câu có số đọc dính "mươi" hoặc từ "tổng" cuối câu không được đổi nghĩa các câu bình thường (Task 1, 3 test phủ định giả).

---

## File Structure

- ai-service: `app/utils/number_words.py` hoặc nơi chuẩn hóa câu nói trước khi đọc số (Task 1: xác định bằng test đỏ), `dictionaries/*.json` (Task 2), `app/services/clauses.py`/`transaction_parser.py` (Task 3), `scripts/eval_chat_ai.py` + `tests/fixtures/eval/chat_ai_cases.jsonl` (Task 4); tests dưới `tests/unit/`.
- App: `lib/core/database/tables/sync_state_table.dart`, `local_categories_table.dart` (mới), `lib/core/database/app_database.dart` (schema v4), `lib/core/database/sync_service.dart` (+`pullHistory`), `lib/features/categories/data/categories_repository.dart` (+`refreshLocal`), `lib/features/assistant/data/local_summary.dart` (mới), `local_chat_decider.dart`, `assistant_controller.dart`, `local_first_chat.dart`; tests `test/core/database_test.dart`, `test/core/sync_service_test.dart`, `test/features/assistant/local_summary_test.dart`, `local_chat_decider_test.dart`.

---

## Giai đoạn A — sửa lỗi chat và giọng nói (ai-service)

### Task 1: "2 mươi lăm nghìn" là 25.000

**Số đo:** trong 53 câu giọng nói chạy qua `parse_chat`, số tiền đúng 52/53; câu sai là `"ăn phở 2 mươi lăm nghìn"` → 5.000 (STT đôi khi trả chữ số cho "hai" trước "mươi").

**Files:** Modify nơi chuẩn hóa văn bản nói trước khi đọc số (tìm bằng test đỏ; ứng viên `app/utils/number_words.py`, `app/services/amount_normalizer.py`); Test `tests/unit/test_spoken_digit_tens.py`.

**Interfaces:** Produces: chữ số 2–9 đứng ngay trước "mươi" được đọc như số chữ tương ứng ("2 mươi lăm" = "hai mươi lăm"); không đổi cách đọc ở chỗ khác.

- [ ] **Step 1: Viết test (RED)**: `parse_chat("ăn phở 2 mươi lăm nghìn").entry.amount == 25000`; `"mua 3 mươi nghìn"` → 30000; `"đổ xăng 5 mươi k"` → 50000. Giả: `"mua 2 cây kem 50k"` → 50000; `"ăn phở 25 nghìn"` → 25000; `"mua 3 mười"` không có "mươi" nên không đổi.
- [ ] **Step 2: Chạy** `docker compose exec -T ai-service sh -c 'cd /src && pytest tests/unit/test_spoken_digit_tens.py -q'`. Expected: FAIL (câu đầu ra 5000).
- [ ] **Step 3: Implement** thay `\b([2-9])\s+mươi\b` bằng số chữ tương ứng ở đúng một chỗ mà mọi đường (chat gõ, giọng nói) đều đi qua.
- [ ] **Step 4: Chạy** toàn bộ `pytest tests/unit -q` trong container và `python scripts/eval_flows.py --chat`. Expected: PASS, chat ≥ 65/66.
- [ ] **Step 5: Commit** `fix(chat): a digit before 'mươi' is a spoken tens ('2 mươi lăm nghìn' = 25,000)`.

### Task 2: 16 từ khóa thiếu

**Số đo:** 72 câu mới (nhãn do tôi viết, nên chỉ chứng minh độ phủ, không chứng minh độ đúng): luật trả lời đúng 58/72; cả 14 câu sai đều là "không biết" (không có câu trả lời sai): `cam, trứng, tôm (quy ký/chục/cân), ổ cắm, bóng đèn, pin, dầu gió, vá xe, bỉm, vở, gội đầu, làm nail, spa (thú cưng), photo`; thêm từ 53 câu giọng nói: `áo` đứng một mình, `laptop`.

**Files:** Modify `dictionaries/{groceries,shopping,healthcare,transport,family,education,other}.json` (hoặc bảng sản phẩm mà `category_resolver` nạp; xem `app/services/category_resolver.py`); Create `tests/fixtures/eval/chat_ai_cases.jsonl` (72 câu + 53 câu giọng nói kèm nhãn, trường `source: "author-labelled"`); Test `tests/unit/test_chat_keyword_gaps.py`.

**Interfaces:** Produces: mỗi từ trên được `resolve(...)` về slug đúng nhóm theo định nghĩa nhóm hiện hành: cam/trứng/tôm → `food`; ổ cắm/bóng đèn/pin/laptop/áo → `shopping`; dầu gió → `health`; vá xe → `transport`; bỉm → `family`; vở → `education`; gội đầu/nail/spa thú cưng/photo → `other`.

- [ ] **Step 1: Viết test (RED)**: bảng tham số hóa 16 câu (`"mua một cân cam 50k"`, `"mua chục trứng 35k"`, `"mua nửa ký tôm 90k"`, `"mua ổ cắm điện 60k"`, `"mua bóng đèn led 35k"`, `"mua pin AA 30k"`, `"mua dầu gió 25k"`, `"vá xe 20k"`, `"mua bỉm cho bé 200k"`, `"mua vở 5 quyển 50k"`, `"gội đầu 40k"`, `"làm nail 150k"`, `"đưa chó đi spa 200k"`, `"photo tài liệu 10k"`, `"áo 150k"`, `"mua laptop 23 triệu"`) → slug đúng như trên. Giả (không đổi): `"mua cam kết bảo hành 50k"` không phải `food`; `"đi spa 300k"` (người) giữ kết quả như trước Task này; `"pin điện thoại 200k"` vẫn `shopping`.
- [ ] **Step 2: Chạy** `pytest tests/unit/test_chat_keyword_gaps.py -q` trong container. Expected: FAIL 16 câu.
- [ ] **Step 3: Implement** thêm từ khóa; từ ngắn dễ trùng ("cam", "pin", "vở", "spa") phải ghép với từ đi kèm hoặc dùng ranh giới từ, không so chuỗi con (đã từng có lỗi "lau"/"lẩu").
- [ ] **Step 4: Chạy** `pytest tests/unit -q` và `python scripts/eval_flows.py` (chat, items, nhất quán chat/hóa đơn). Expected: PASS; luật trên 72 câu ≥ 70/72; chat/items/nhất quán không thấp hơn mốc trước (65/66, 107/127, 65/66).
- [ ] **Step 5: Commit** `fix(chat): keywords for 16 common products the rules did not know`.

### Task 3: "tổng" cuối mệnh đề không nằm trong mô tả

**Số đo:** `"mua 3 cái áo tổng hai trăm bảy mươi nghìn"` → số tiền đúng 270.000 nhưng mô tả thành `"mua 3 cái áo tổng"` nên mất danh mục.

**Files:** Modify `app/services/clauses.py` hoặc `transaction_parser.py` (nơi tách số tiền khỏi mô tả); Test `tests/unit/test_chat_parser.py`.

**Interfaces:** Produces: nhãn tổng ("tổng", "tổng cộng", "tất cả") đứng ngay trước số tiền bị bỏ khỏi `Entry.description`; `"mua bộ tổng hợp đề 150k"` giữ nguyên (đã có test của kế hoạch trước).

- [ ] **Step 1: Viết test (RED)**: câu trên → 1 entry 270000, `description == "mua 3 cái áo"`, `category == "shopping"` (cần Task 2). Giả: `"mua bộ tổng hợp đề 150k"` → mô tả còn nguyên chữ "tổng hợp"; `"ăn tối tổng cộng 200k"` → `description == "ăn tối"`.
- [ ] **Step 2: Chạy** `pytest tests/unit/test_chat_parser.py -k total_label -q`. Expected: FAIL.
- [ ] **Step 3: Implement**: cắt nhãn tổng đứng sát ngay trước cụm số tiền, theo ranh giới từ.
- [ ] **Step 4: Chạy** `pytest tests/unit -q`. Expected: PASS.
- [ ] **Step 5: Commit** `fix(chat): a 'tổng' label before the amount is not part of the description`.

### Task 4: Bộ đo AI-first tuần tự và biết khi nào số đo vô hiệu

**Số đo:** lần chạy 2026-10-16 với 3 lời gọi song song cho kết quả "AI-first 58/72 = bằng luật", với 66/72 lời gọi báo `ai_failed`. Gọi tuần tự từng câu thì Gemini trả đúng (3/3 thử tay, ~2 giây mỗi câu). Nghĩa là số đo song song vô hiệu (cầu chì mở), không phải AI-first kém; nhưng cũng nghĩa là **tải đồng thời cao làm chat tự rơi về luật** (đúng thiết kế, cần được nhìn thấy trong số đếm).

**Files:** Create `ai-service/scripts/eval_chat_ai.py`; Test `ai-service/tests/unit/test_eval_chat_ai.py`.

**Interfaces:** Produces: `score(cases, answers, failures) -> Report`; `Report.valid: bool` là `False` khi `failures > 0`; script chạy tuần tự (đồng thời = 1), in số đúng của luật và của AI-first, số lời gọi AI lỗi, và dòng `SỐ ĐO VÔ HIỆU` khi `valid` là False; nhãn trong `chat_ai_cases.jsonl` có ghi chú "do tác giả gán, có tính vòng tròn".

- [ ] **Step 1: Viết test (RED)**: `score` với 0 lỗi → `valid True` và đếm đúng; với 1 lỗi → `valid False` (không in tỷ lệ AI-first như một kết quả).
- [ ] **Step 2: Chạy** `pytest tests/unit/test_eval_chat_ai.py -q`. Expected: FAIL (chưa có module).
- [ ] **Step 3: Implement** script dùng `apply_ai_categories` với `GeminiClient` cấu hình như `app/api/v1/bill.py`, tuần tự.
- [ ] **Step 4: Chạy** test, rồi `python scripts/eval_chat_ai.py` trong container với khóa thật. Expected: test PASS; báo cáo `valid` và ghi số vào `docs/evaluation.md`.
- [ ] **Step 5: Commit** `test(chat): sequential AI-first evaluation that refuses to report a run where the AI failed`.

---

## Giai đoạn B — tổng hợp chi tiêu tính ở máy (app)

### Task 5: Cờ trạng thái đồng bộ và kéo đủ lịch sử

**Files:** Create `lib/core/database/tables/sync_state_table.dart`; Modify `lib/core/database/app_database.dart` (schema v4 + migration), `lib/core/database/sync_service.dart`; Test `test/core/database_test.dart`, `test/core/sync_service_test.dart`.

**Interfaces:**
- Consumes: `GET /api/v1/transactions?limit&cursor` trả `TransactionPage{items, nextCursor}` (đã có, `TransactionRepository.getTransactions`), `toCompanion(entity)` trong `local_transaction_mapper.dart`.
- Produces: bảng `SyncState(key text PK, value text)`; `AppDatabase.getState(String key) -> Future<String?>`, `setState(key, value)`; `AppDatabase.historyComplete -> Future<bool>`; `SyncService.pullHistory({int pageSize = 100}) -> Future<void>`: lưu con trỏ ở khóa `historyCursor` sau mỗi trang, chỉ đặt `historyComplete = 'true'` khi `nextCursor == null`; chèn dòng server với `syncStatus = 'synced'` nhưng KHÔNG đè dòng cùng `clientRequestId` đang `pending`/`failed`; mất mạng thì dừng lặng lẽ giữ con trỏ (như `processQueue`).

- [ ] **Step 1: Viết test (RED)**: `pull_sets_flag_only_at_last_page`, `interrupted_pull_resumes` (lần 1 hết mạng ở trang 2, lần 2 bắt đầu từ trang 2), `pull_keeps_unsynced_rows`, `flag_is_per_user_file` (hai `AppDatabase.forTesting` độc lập), `migration_v3_to_v4_keeps_rows`.
- [ ] **Step 2: Chạy** `flutter test test/core -r compact`. Expected: FAIL.
- [ ] **Step 3: Implement** theo Interfaces; migration v3→v4 chỉ thêm bảng.
- [ ] **Step 4: Chạy** `flutter test` và `flutter analyze`. Expected: PASS, analyze sạch. Gọi `pullHistory` từ nơi `processQueue` đang được gọi sau đăng nhập/mở app.
- [ ] **Step 5: Commit** (repo FE, `fe()`) `feat(db): pull the full transaction history once and remember it is complete`.

### Task 6: Danh mục lưu ở máy

**Files:** Create `lib/core/database/tables/local_categories_table.dart`; Modify `app_database.dart` (cùng schema v4 của Task 5; nếu Task 5 đã commit thì nâng v5), `categories_repository.dart`; Test `test/core/database_test.dart`.

**Interfaces:**
- Produces: bảng `LocalCategories(id text PK, slug text, name text)`; `AppDatabase.replaceCategories(List<...>)`, `AppDatabase.categoryNameBySlug / categoryByLocalId`; `CategoriesRepository.refreshLocal() -> Future<void>` (gọi `getCategories()` rồi ghi), được gọi cùng lúc với `pullHistory`.

- [ ] **Step 1: Viết test (RED)**: `refresh_replaces_the_set`, `lookup_by_id_returns_slug_and_name`, `flag_is_per_user_file`.
- [ ] **Step 2: Chạy** `flutter test test/core -r compact`. Expected: FAIL.
- [ ] **Step 3: Implement**; `Category.fromJson` có sẵn dùng lại.
- [ ] **Step 4: Chạy** `flutter test` và `flutter analyze`. Expected: PASS.
- [ ] **Step 5: Commit** `feat(db): keep the category list on the device`.

### Task 7: Tổng hợp tính ở máy, rơi về server khi chưa đủ điều kiện

**Files:** Create `lib/features/assistant/data/local_summary.dart`; Modify `local_chat_decider.dart` (+`Summary(ChatSummary)` trong `sealed ChatDecision`), `assistant_controller.dart` (xử lý `Summary` bằng cùng thẻ tóm tắt như `action == summary` hiện có); Test `test/features/assistant/local_summary_test.dart`, `local_chat_decider_test.dart`.

**Interfaces:**
- Consumes: `ChatAnalysis.query` (`metric` ∈ expense|income|balance, `start`, `end` nửa mở `[start, end)`, `periodLabel`, `categorySlug`), `AppDatabase.historyComplete`, `LocalCategories`, `ChatSummary`/`ChatSummaryItem` trong `assistant_models.dart`.
- Produces: `LocalSummary(db).compute(query) -> Future<ChatSummary?>` trả `null` khi `historyComplete` là false; theo quy tắc ở Global Constraints (dòng `pending`/`failed` của chính người dùng VẪN được tính); `items` chỉ có khi `metric == expense`, sắp giảm dần theo số tiền, `share` làm tròn 3 chữ số, danh mục không tra được → `slug ''`, tên "Chưa phân loại"; có `categorySlug` thì tổng là của danh mục đó. `LocalChatDecider.decide` trả `Summary` khi `compute` ra giá trị, ngược lại `NeedSummary` như hiện nay.

- [ ] **Step 1: Viết test (RED)** trên bộ dữ liệu cố định tính tay: `matches_server_rules` (có `Draft`, `Transfer`, danh mục `loan` để chứng minh bị bỏ), `midnight_vietnam_boundary` (giao dịch lúc 23:59 và 00:01 giờ VN ngày ranh giới rơi vào đúng kỳ), `category_filter_total_is_that_category`, `uncategorized_gets_default_name`, `incomplete_history_falls_back_to_server` (decider trả `NeedSummary`), `balance_is_income_minus_expense`.
- [ ] **Step 2: Chạy** `flutter test test/features/assistant -r compact`. Expected: FAIL.
- [ ] **Step 3: Implement** bằng truy vấn drift trên `LocalTransactions`; không thêm dịch vụ mới.
- [ ] **Step 4: Chạy** `flutter test` và `flutter analyze`; so sánh tay một câu "tháng này tôi tiêu bao nhiêu" giữa đường cũ và đường mới trên cùng tài khoản thử. Expected: PASS, hai đường cho cùng số.
- [ ] **Step 5: Commit** `feat(assistant): answer spending summaries from the device when the full history is there`.

### Task 8: Bật mặc định `localFirstChat` (cổng của owner)

**Files:** Modify `lib/features/assistant/data/local_first_chat.dart`; Test `test/features/assistant/local_first_chat_test.dart`.

**Interfaces:** `localFirstChatProvider` mặc định `true`; mất mạng hay `analyze` lỗi vẫn rơi về đường cũ như hiện tại (không đổi).

- [ ] **Step 1: Cổng thủ công, không tự làm**: owner thử trên máy Android thật các tình huống: ghi một khoản, nhiều khoản, hủy, mất mạng giữa chừng rồi có mạng lại, hỏi tổng tháng này, đăng xuất rồi đăng nhập lại. Chỉ làm các bước dưới khi owner báo đạt.
- [ ] **Step 2: Viết test (RED)**: `default_is_local_first`; `flag_off_uses_old_path` (đã có) vẫn xanh khi tắt tay.
- [ ] **Step 3: Chạy** `flutter test test/features/assistant -r compact`. Expected: FAIL.
- [ ] **Step 4: Implement** đổi mặc định, cập nhật chú thích của provider; chạy `flutter test` và `flutter analyze`. Expected: PASS.
- [ ] **Step 5: Commit** `feat(assistant): local-first chat is the default`.

---

## Không nằm trong kế hoạch này (mỗi mục là một kế hoạch riêng)

1. **Quyết định D1 của owner: sau khi đồng bộ, server có còn giữ giao dịch không?** Hiện có: backend giữ toàn bộ giao dịch để chạy ngân sách, báo cáo, nhiệm vụ, cảnh báo đẩy; hàng đợi đồng bộ luôn đẩy lên. Nếu D1 = "không giữ" thì ngân sách, báo cáo, mục tiêu tiết kiệm, gamification phải chuyển về máy hoặc bỏ: đây là phần lớn công sức và cần spec riêng.
2. **Ví, ngân sách, báo cáo, mục tiêu tiết kiệm ở máy.** Task 5-7 chỉ là nền (lịch sử và danh mục).
3. **Đồng bộ lên server thành tùy chọn.** Cần cờ người dùng và kết quả D1.
4. **Nhãn học do app gửi.** Chưa cần khi giao dịch vẫn đi qua `POST /transactions` của backend (nhãn cuối vẫn gắn được ở đó); chỉ cần khi D1 = "không giữ".
5. **Chat khi mất mạng:** hiện chỉ giữ câu trong `PendingMessages` và báo; phân tích câu vẫn cần server.
6. **Độ phủ chưa đo:** STT (chưa có bộ âm thanh có nhãn nên chưa biết sai số của tầng nghe), OCR trên ảnh thật (30 ảnh thật: tên + danh mục + tổng cùng đúng 83%, ảnh tổng hợp 100%), lời người dùng thật cho chat. Cần thu dữ liệu thật trước khi tin các số hiện có.

## Rủi ro đã biết

- Nhãn của bộ 72 câu và 53 câu do tác giả gán nên số "luật 81%" chỉ đo độ phủ; chỉ tin độ đúng khi có câu của người dùng thật.
- Tổng hợp ở máy chỉ đúng khi lịch sử đã kéo đủ; các giao dịch tạo từ thiết bị khác chưa kéo về (đồng bộ một chiều lên server hiện nay) sẽ thiếu cho đến lần kéo kế tiếp. Chấp nhận trong kế hoạch này vì app hiện chỉ dùng một thiết bị mỗi tài khoản (giả định chưa kiểm chứng).
- Cầu chì Gemini mở khi nhiều lời gọi đồng thời: chat rơi về luật trong thời gian chờ, không lỗi. Task 4 chỉ làm nó nhìn thấy, chưa đổi hành vi.
