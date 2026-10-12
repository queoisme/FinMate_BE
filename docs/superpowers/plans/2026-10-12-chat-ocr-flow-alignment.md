# Chat + OCR Flow Alignment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the gaps between FinMate's chat and receipt-OCR flows and the pipeline in `C:\Users\Admin\Downloads\finance_ai_pipeline_prompt.md` (sections 5.1, 5.2, 5.3, 6), without rebuilding anything that already exists.

**Architecture:** Two tracks on top of the current services. OCR track: smarter per-item classification order, a persisted item-name cache, fuzzy matching, and a `needs_review` signal. Chat track: split multi-transaction sentences, carry a date, pin ambiguity behaviour, and warn on duplicates. A frozen evaluation set is written first so every later task is measured, not asserted.

**Tech Stack:** Python 3.11 FastAPI + SQLAlchemy/Alembic + pytest (`ai-service`), ASP.NET Core 9 + xUnit + Testcontainers (`backend`), Docker Compose.

**Spec:** the prompt above (flows 5.1 / 5.2 / 5.3 / section 6), plus `docs/superpowers/specs/2026-10-09-receipt-item-split-design.md` and `docs/superpowers/specs/receipt-item-rules-draft.md`. Decisions made in conversation 2026-10-12: Gemini is asked before the store default; voice/ASR is out of scope (another person owns it, integrated later through the same text path); the 11 BE category slugs stay as they are.

**Branch:** create `feat/chat-ocr-alignment` from `feat/receipt-item-split`. Nothing is pushed or merged yet (owner's decision).

## Global Constraints

- Voice/ASR is out of scope. Chat must keep accepting plain text from any source so voice can plug into `POST /transactions/chat` later.
- Categories are the 11 slugs in `app/core/enums.py` `CATEGORY_SLUGS`. Do not add, rename or re-map any slug. "Chưa phân loại" is `None`/`unknown`, never forced to `other`.
- Money is `BIGINT` VND; ids are `Guid`/UUID; time is `DateTimeOffset`/`TIMESTAMPTZ`.
- AI DB never holds a real user id (SHA-256 hash only). Never log item names, merchant names, chat text, amounts or Gemini answers.
- Gemini only receives item names with money stripped, only with consent (`allow_ai`), never for health bills; unchanged by this plan.
- Every new failure path falls back to the existing behaviour. New contract fields are additive: an old client or old BE reading the response must keep working.
- Keep `_PRODUCT_KEYWORDS` in `ocr-service/extract.py` and `ai-service/app/bill/classifier.py` identical if either is touched.
- Style: `black . && ruff check . && isort .` in `ai-service`; `dotnet build` warning-free in `backend`.

## Review Focus

1. **Comma decimals and thousands are not separators:** `"mua 1,5tr đồ"`, `"đi taxi 1.250.000"` must stay one entry (Task 5).
2. **A quantity is not a second transaction:** `"mua 2 lít sữa 50k"` is one entry of 50.000 (Task 5).
3. **Cache poisoning:** a hostile or junk item name must never be stored, and a stored answer must be a valid slug (Task 3).
4. **Two identical coffees are two purchases:** the duplicate guard warns and asks, it never blocks or deletes (Task 9).
5. **Consent off means no outbound call even when the cache is cold:** cache reads send nothing out and are allowed; Gemini is not called (Task 2/3).

---

## File Structure

| File | Responsibility |
|---|---|
| `ai-service/scripts/eval_flows.py` (new) | Runs frozen cases through chat parser and item classifier, prints accuracy, abstention rate, confusion matrix |
| `ai-service/tests/fixtures/eval/chat_cases.jsonl`, `item_cases.jsonl` (new) | Frozen cases, never used to tune rules |
| `ai-service/app/bill/item_classifier.py` | Order of steps, cache lookup/write, fuzzy match |
| `ai-service/app/bill/knowledge.py` | Item-name cache read/write methods on `BillKnowledge` |
| `ai-service/alembic/versions/0009_bill_item_cache.py` (new) | Table `bill_item_cache` |
| `ai-service/app/chat/parser.py`, `app/chat/clauses.py` (new), `app/chat/dates.py` (new) | Clause splitting, date parsing, multi-entry parse |
| `ai-service/app/schemas/chat.py`, `app/api/v1/chat.py` | Additive `entries`, `occurred_at` |
| `backend/.../IAIServiceClient.cs`, `ChatDto.cs`, `ChatCommandHandler.cs` | Multi-entry drafts, date, duplicate warning |
| `backend/.../ScanReceiptCommandHandler.cs`, `ReceiptSplitter.cs` | `NeedsReview`, duplicate flag per group |

---

### Task 1: Frozen evaluation set and baseline report

**Files:**
- Create: `ai-service/tests/fixtures/eval/chat_cases.jsonl`, `ai-service/tests/fixtures/eval/item_cases.jsonl`, `ai-service/scripts/eval_flows.py`
- Test: `ai-service/tests/unit/test_eval_flows.py`

**Interfaces:**
- Produces: `python scripts/eval_flows.py [--chat|--items]` printing `accuracy`, `abstain_rate`, per-class confusion matrix; fixtures one JSON object per line: chat `{"text","expect":{"kind","amount","category"|null}}` or multi `{"text","expect":[...]}`; items `{"name","merchant"|null,"expect":"<slug>|unknown"}`.

- [ ] **Step 1:** Write ≥60 chat cases (every case in prompt section 6: `ăn cơm 20k`, three-item sentence, `an com 20k`, `hai mươi nghìn`, `20.000đ`, `2tr5`, `mua nước 20k` expect category null, `chuyển khoản 200k` expect kind transfer, no amount, nhiều số tiền, out-of-scope) and ≥80 item cases (Ministop-style: khăn ướt, bột giặt, sữa chua, Sting, Oreo, thuốc, tã, SP002993 → unknown). **Do this before touching any rule; never edit expectations later to make a run pass.**
- [ ] **Step 2:** Test `test_eval_flows.py::test_fixture_lines_are_valid_json_and_slugs_exist` asserting every expected category is in `CATEGORY_SLUGS ∪ {"unknown", None}` and every line parses.
- [ ] **Step 3:** Implement `scripts/eval_flows.py` (`main() -> int`), chat via `parse_chat(text, None)`, items via `classify_items` with an in-memory fake knowledge and a fake Gemini that always answers `unknown`.
- [ ] **Step 4:** Run `cd ai-service && pytest tests/unit/test_eval_flows.py -v` → PASS. Run `python scripts/eval_flows.py` and save output to `docs/evaluation.md` under heading "Baseline 2026-10-12" (numbers as printed, failures listed).
- [ ] **Step 5:** Commit `test: frozen chat/item evaluation set and baseline`.

### Task 2: Gemini before the store default

**Files:**
- Modify: `ai-service/app/bill/item_classifier.py:56-82`
- Test: `ai-service/tests/unit/test_item_classifier.py`

**Interfaces:**
- Consumes: `classify_items(items, merchant, knowledge, gemini, instruction, allow_ai) -> tuple[list[ClassifyItemOut], bool]` (unchanged signature).
- Produces: new order per item: parent → rule → (cache, Task 3) → Gemini (if allowed) → store default → unknown. `source` `"merchant"` now only for items Gemini did not decide (or was not allowed to).

- [ ] **Step 1: tests** (names and assertions):
  - `test_gemini_beats_store_default_for_unmatched_items`: merchant `"Ministop"`, items `["Khan uot Bobby"]`, FakeGemini answers `shopping` → `[(0,"shopping","ai")]`.
  - `test_store_default_used_when_ai_not_allowed`: same input, `allow_ai=False` → `[(0,"food","merchant")]`.
  - `test_store_default_used_when_gemini_says_unknown`: FakeGemini answers `unknown` → `source == "merchant"`.
  - `test_rule_hit_never_reaches_gemini`: `["Sting 330ml"]` → `source "rule"`, `gemini.calls == []`.
  - `test_health_bill_still_never_sent`: existing health-bill test stays green.
- [ ] **Step 2:** Run `pytest tests/unit/test_item_classifier.py -v`; the first and third new tests FAIL.
- [ ] **Step 3:** Reorder in `classify_items`: compute `default = _merchant_default(merchant)` but apply it **after** the Gemini block, only to items still `None`. Health-bill detection must not treat a store-default `health` as a reason that depends on order: compute it from rules + bill category as today, and also from `default == "health"`.
- [ ] **Step 4:** Run the file → PASS. Run the Ministop bill through the live stack (`E:\Ki 9\Model AI\_test_bills` Ministop image, flag on, consent on): wet wipes must land in `shopping`, drinks in `food`, groups sum to the bill total.
- [ ] **Step 5:** Commit `fix(ai): ask Gemini before falling back to the store default`.

### Task 3: Persist Gemini answers (item-name cache)

**Files:**
- Create: `ai-service/alembic/versions/0009_bill_item_cache.py`
- Modify: `ai-service/app/bill/knowledge.py` (Protocol + DB class), `ai-service/app/bill/item_classifier.py`
- Test: `ai-service/tests/unit/test_item_classifier.py`, `ai-service/tests/integration/test_bill_item_cache.py`

**Interfaces:**
- Produces on `BillKnowledge`: `async def cached_item(self, name: str) -> str | None` and `async def remember_item(self, name: str, category_slug: str) -> bool`. Key: `item_key(name) -> str` = folded lowercase, money stripped, whitespace collapsed (reuse `merchant_key`'s `classifier._norm`).
- Table `bill_item_cache(item_key text primary key, category_slug text not null, hits integer not null default 1, created_at timestamptz default now(), updated_at timestamptz default now())`. No user id column.

- [ ] **Step 1: tests:**
  - `test_cached_answer_is_used_without_calling_gemini`: FakeKnowledge returns `"shopping"` for the key → `source "rule"`-equivalent new value `"cache"`; add `"cache"` to `ClassifyItemOut.source` Literal (additive) and BE enum mapping if any.
  - `test_gemini_answer_is_remembered`: after a Gemini hit, `knowledge.remembered_items == {key: slug}`.
  - `test_unknown_answer_is_never_remembered`.
  - `test_junk_names_are_never_remembered`: names shorter than 3 chars, longer than 60 chars, only digits/codes (`SP002993`), or containing newline → not stored.
  - `test_cache_read_works_with_ai_not_allowed`.
- [ ] **Step 2:** Run → FAIL. **Step 3:** Migration + implement; `remember_item` is an upsert (`hits = hits + 1`), validates `category_slug in CATEGORY_SLUGS`, and swallows DB errors (log warning without the name). Order in `classify_items`: parent → rule → cache → Gemini → store default → unknown. **Step 4:** `pytest tests/unit tests/integration/test_bill_item_cache.py -v` → PASS; `alembic upgrade head` on the dev DB and `alembic downgrade -1` round-trip clean. **Step 5:** Commit `feat(ai): remember Gemini item answers in bill_item_cache`.

### Task 4: Fuzzy alias match for OCR-damaged item names

**Files:**
- Modify: `ai-service/app/bill/item_classifier.py`
- Test: `ai-service/tests/unit/test_item_classifier.py`

**Interfaces:**
- Produces: `_fuzzy_rule(name: str) -> str | None` (BE slug), stdlib `difflib.get_close_matches` over the product-keyword table that `_category_from_products` already uses, `cutoff=0.88`, only for folded names of ≥6 characters, compared word-wise (a single changed letter in a keyword). Sits between rule and cache; `source "rule"`.

- [ ] **Step 1: tests:** `"KHAM UOT"` → shopping; `"SUA CHUA VINAMLK"` stays whatever the exact rule gives; `"SP002993"` → no fuzzy hit; `"nuoc"` (short/ambiguous) → no fuzzy hit; `"banh quy"` must not be pulled into another group by a near keyword (assert unchanged result).
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement; no new dependency. **Step 4:** Run the file and `python scripts/eval_flows.py --items`; accuracy must not drop vs. Task 1 baseline, and wrong-confident count must not rise. If it does, raise the cutoff rather than loosening tests. **Step 5:** Commit `feat(ai): fuzzy keyword match for OCR typos`.

### Task 5: Split multi-transaction sentences (chat)

**Files:**
- Create: `ai-service/app/chat/clauses.py`
- Modify: `ai-service/app/chat/parser.py` (`parse_chat`), `ai-service/app/chat/amount.py` only if a helper is needed
- Test: `ai-service/tests/unit/test_chat_clauses.py`, `ai-service/tests/unit/test_chat_parser.py`

**Interfaces:**
- Produces: `split_clauses(text: str) -> list[str]` and `ChatParse.entries: tuple[Entry, ...]` (`entry` remains `entries[0]` or `None`). A separator (`,` `;` ` và ` ` rồi ` ` + `) splits only when **both sides contain a parse_amount match** and the comma is not between digits.

- [ ] **Step 1: tests:**
  - `test_three_expenses_split`: `"ăn cơm 20k, uống nước 10k, đổ xăng 50k"` → three entries, amounts 20000/10000/50000, categories `dining`, None (bare "nước" ambiguous), `transport`.
  - `test_and_then_splits`: `"ăn phở 45k và cà phê 30k"` → two.
  - `test_decimal_comma_is_not_a_separator`: `"mua đồ 1,5tr"` → one entry 1.500.000.
  - `test_thousands_dot_is_not_a_separator`: `"đi taxi 1.250.000"` → one.
  - `test_quantity_is_not_a_second_transaction`: `"mua 2 lít sữa 50k"` → one, 50000.
  - `test_mixed_kinds`: `"nhận lương 12 triệu, ăn cơm 20k"` → income + expense.
  - `test_multi_entries_are_never_auto_save_ok`.
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement `split_clauses`; in `parse_chat`, when it returns >1 clause and the intent is an entry intent, build one `Entry` per clause via the existing `_entry`, with `auto_save_ok=False` for all. Rasa is run once on the whole text; per-clause kind comes from `detect_kind(clause)`. **Step 4:** `pytest tests/unit/test_chat_clauses.py tests/unit/test_chat_parser.py tests/unit/test_chat_api.py -v` → PASS (existing single-entry tests unchanged). **Step 5:** Commit `feat(ai): split multi-transaction chat sentences`.

### Task 6: Date in chat (`occurred_at`)

**Files:**
- Create: `ai-service/app/chat/dates.py`
- Modify: `ai-service/app/chat/parser.py`, `ai-service/app/schemas/chat.py`, `ai-service/app/api/v1/chat.py`
- Test: `ai-service/tests/unit/test_chat_dates.py`

**Interfaces:**
- Produces: `parse_when(text: str, now: datetime) -> datetime | None` (VN time, `hôm qua`, `hôm kia`, `hôm nay`, `sáng/trưa/chiều/tối nay` → 08:00/12:00/17:00/19:00, `ngày 5/10`, `5/10`, `ngày 5 tháng 10`; future date → `None`); `Entry.occurred_at: datetime | None`; `ChatEntry.occurred_at: datetime | None = None`. The matched date phrase is removed from `description`.

- [ ] **Step 1: tests:** each phrase above with `now = 2026-10-12 10:00+07`; `"ăn phở 45k hôm qua"` → description `"ăn phở"`; `"5/13"` and `"ngày 32/1"` → `None`; a date later than `now` → `None`; `"20/10"` inside `"giảm 20/10"` ignored when no day-month word precedes (assert chosen rule).
- [ ] **Step 2:** FAIL. **Step 3:** implement and wire through `_entry`. **Step 4:** PASS. **Step 5:** Commit `feat(ai): parse transaction date from chat`.

### Task 7: Ambiguity behaviour pinned

**Files:**
- Modify (only if a test fails): `ai-service/app/chat/vocab.py`, `ai-service/app/chat/parser.py`
- Test: `ai-service/tests/unit/test_chat_parser.py`

- [ ] **Step 1: tests:** `"mua nước 20k"` → entry category `None`, `auto_save_ok False`; `"đóng tiền nước 200k"` → `housing`; `"uống nước 10k"` → `dining`; `"chuyển khoản 200k"` → kind `transfer`, never `auto_save_ok`; `"20k"` alone → entry with no description, `auto_save_ok False`; `"mua nước"` → `needs_amount`; `"-50k ăn cơm"` → amount 50000 (sign ignored, expense).
- [ ] **Step 2:** Run, list failures. **Step 3:** Fix only failing cases with the smallest vocab/rule change; do not touch passing behaviour. **Step 4:** PASS + `python scripts/eval_flows.py --chat`, no regression vs. baseline. **Step 5:** Commit `fix(ai): keep ambiguous chat categories unknown`.

### Task 8: Backend carries multi-entry and date

**Files:**
- Modify: `backend/FinMate.Application/Common/Interfaces/IAIServiceClient.cs`, `backend/FinMate.Infrastructure/ExternalServices/AIServiceApiContracts.cs`, `backend/FinMate.Application/Common/Models/ChatDto.cs`, `backend/FinMate.Application/Transactions/Commands/ChatCommandHandler.cs`
- Test: `backend/FinMate.Tests/Unit/Application/Transactions/ChatCommandHandlerTests.cs`, `backend/FinMate.Tests/Unit/Infrastructure/ChatParseContractTests.cs`, `backend/FinMate.Tests/Integration/Controllers/ChatControllerTests.cs`

**Interfaces:**
- Consumes: AI response additive `entries[]` and `entry.occurred_at`.
- Produces: `ChatParseEntry` gains `DateTimeOffset? OccurredAt`; `ChatParseResponse` gains `IReadOnlyList<ChatParseEntry>? Entries`; `ChatResultDto` gains `IReadOnlyList<ChatDraftDto>? Drafts` (`Draft` stays = first). `ChatDraftDto` gains `DateTimeOffset? OccurredAt`.

- [ ] **Step 1: tests:** `Handle_MultipleEntries_ReturnsConfirmWithAllDrafts_AndSavesNothing`; `Handle_SingleEntry_Unchanged` (existing); `Handle_AutoSave_UsesOccurredAtWhenPresent` (passes `OccurredAt` instead of `UtcNow` to `CreateManualTransactionCommand`); `ContractTest_OldResponseWithoutEntries_StillDeserializes`.
- [ ] **Step 2:** `dotnet test backend --filter "FullyQualifiedName~Chat"` → FAIL. **Step 3:** implement; when `Entries.Count > 1` return `ChatActions.Confirm` with `Drafts` and never auto-save. **Step 4:** run the filter, then full `dotnet test backend` (Docker Desktop must be running) → all green. **Step 5:** Commit `feat(be): multi-draft chat result and transaction date`.

### Task 9: Duplicate guard (chat and scan)

**Files:**
- Modify: `backend/FinMate.Application/Common/Interfaces/ITransactionRepository.cs` (or the existing repository interface the chat handler can reach), its implementation, `ChatCommandHandler.cs`, `ScanReceiptCommandHandler.cs`, DTOs
- Test: `ChatCommandHandlerTests.cs`, `ScanReceiptCommandHandlerTests.cs`, repository integration test

**Interfaces:**
- Produces: `Task<Guid?> FindPossibleDuplicateAsync(Guid userId, long amountCents, DateTimeOffset at, TimeSpan window, CancellationToken ct)` filtered by `userId` (rule: never fetch by id alone), `window = 10 minutes`, debit only, not deleted. `ChatDraftDto.PossibleDuplicateTransactionId: Guid?`; group DTO of scan result gets the same field.

- [ ] **Step 1: tests:** `AutoSave_DowngradedToConfirm_WhenSameAmountWithinWindow` (draft carries the existing id, nothing created); `OutsideWindow_StillAutoSaves`; `OtherUsersTransaction_IsNeverAMatch`; `SameClientRequestId_ReplayStillReturnsExistingTransaction` (idempotency path untouched); scan group with a matching transaction gets the field set and is still returned.
- [ ] **Step 2:** FAIL. **Step 3:** implement; this is a warning plus a downgrade from auto-save, never a rejection. **Step 4:** `dotnet test backend` → green. **Step 5:** Commit `feat(be): warn on possible duplicate from chat or receipt scan`.

### Task 10: `needs_review` on scan results

**Files:**
- Modify: `backend/FinMate.Application/Transactions/Commands/ScanReceiptCommandHandler.cs`, `ReceiptSplitter.cs`, scan DTO
- Test: `ScanReceiptCommandHandlerTests.cs`

**Interfaces:**
- Produces: `NeedsReview: bool` on the scan-receipt result. True when the unclassified (unknown) amount is ≥ 20% of the bill total, or when the bill fell back to the single-group path because `items_ok` was false. Value `0.20` is a named constant.

- [ ] **Step 1: tests:** unknown share 19% → false; 20% → true; items_ok false fallback → true; fully classified → false.
- [ ] **Step 2:** FAIL. **Step 3:** implement. **Step 4:** `dotnet test backend --filter "FullyQualifiedName~Scan"` → PASS. **Step 5:** Commit `feat(be): flag scans that need user review`.

### Task 11: Re-measure and document

**Files:**
- Modify: `docs/evaluation.md`, `.context/ARCHITECTURE.md` (§3.3 chat contract, §3.4 item order, §3.6 chat parser, §4 new table), `.context/TASKS.md`, `.context/TECH_STACK.md` only if a dependency changed (none expected)

- [ ] **Step 1:** Run `python scripts/eval_flows.py` and append "After 2026-10-12" beside the baseline with the same cases, listing every case that got worse, if any.
- [ ] **Step 2:** Update the docs to match code (new order: rule → fuzzy → cache → Gemini → store default → unknown; `entries`, `occurred_at`, `possible_duplicate`, `needs_review`, `bill_item_cache`; 26 tables).
- [ ] **Step 3:** Full runs: `cd ai-service && pytest`, `dotnet test backend`, then one real chat session and one real Ministop scan on the Docker stack.
- [ ] **Step 4:** Commit `docs: chat/OCR flow alignment`.

---

## Not in this plan (decided or deferred)

- Voice/ASR: separate owner; integrate when the codebase arrives (text path stays unchanged).
- Flutter chat UI for `Drafts`, `occurred_at`, `possible_duplicate`, `needs_review`: needs a look at the current FE chat screen first; until then the old client still works and shows only the first draft.
- Per-user item memory, calibrated confidence thresholds, PhoBERT comparison, dataset registry/licences, Gemini full-text mode: later, after Task 1/11 numbers say which matters.
- Pushing or merging to `queoisme/FinMate_BE`: not until the owner finishes testing in Model AI.
