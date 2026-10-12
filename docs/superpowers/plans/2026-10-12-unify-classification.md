# Unified Category Resolver Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Chat and receipt items decide a category with ONE shared resolver, so the same phrase gets the same category whichever way it enters (prompt `finance_ai_pipeline_prompt.md` §5.3: dictionary → alias/fuzzy → cache → optional LLM → unknown, shared by every source).

**Architecture:** A new `app/core/category_resolver.py` merges the three keyword sources that exist today (bill product keywords, chat vocabulary, `merchant_categories.json`) into one index of `keyword → backend slug` and answers `resolve(text)`. Chat description and per-item receipt classification both call it for their rule step; typo matching moves into it. The item cache is read by chat too (read-only). Receipt-level logic (brand, store keyword, vote across lines) and Gemini stay where they are.

**Tech Stack:** Python 3.11, FastAPI, pytest (`ai-service`). No backend, app or DB-schema change.

**Spec:** `C:\Users\Admin\Downloads\finance_ai_pipeline_prompt.md` §5.3 and §7 (`core/category_resolver.py`), plus `docs/superpowers/specs/receipt-item-rules-draft.md` (approved category rules: sữa bột/tã → `family`, bột giặt → `shopping`, etc.). Evidence that motivates the plan (run 2026-10-12, same phrase through both paths): 8 of 13 phrases differ, e.g. `tã quần Bobby` chat=`family` vs bill=`food`; `sữa bột Enfa` chat=`family` vs bill=`food`; `học phí`, `khám bệnh`, `sách giáo khoa` bill=none vs chat=`education`/`health`; `bột giặt`, `vitamin C`, `nước ngọt Pepsi`, `khăn ướt` chat=none vs bill=`shopping`/`health`/`food`.

**Branch:** `feat/unify-classification` from the tip of `feat/chat-ocr-alignment`. Nothing is pushed or merged.

## Global Constraints

- Output slugs are only the 11 in `app/core/enums.py` `CATEGORY_SLUGS`. `None` means "not sure" and is never forced to `other`.
- Do NOT change the DB tables `bill_brands` / `bill_keywords` or their 9-group `category_slug` values; map at index-build time with `SLUG_BY_LOCAL_CATEGORY` (`app/core/category_map.py`). DB edits must still take effect within the existing 60 s refresh.
- Keep `_PRODUCT_KEYWORDS` in `ocr-service/extract.py` and `ai-service/app/bill/classifier.py` identical; this plan does not edit either table's contents.
- Never log chat text, item names, amounts or matched keywords.
- Behavior must not regress: every existing ai-service test stays green, `python scripts/eval_flows.py` must not lower chat (62/63) or items (52/72) correct counts nor raise "sai mà tự tin" (chat 1, items 2).
- Chat never calls Gemini in this plan (the consent copy covers receipt item names only). A category that comes from the cache (Gemini-derived) never makes a chat entry `auto_save_ok`.
- Style: match surrounding code; no formatters run over untouched files.

## Review Focus

1. **Conflicting keywords across sources:** `ta quan`, `sua bot` (bill `groceries` vs chat `family`), `sua xe` vs `sua`, bare `nuoc` (`mua nước 20k` must stay `None`, `tiền nước` → `housing`, `nước ngọt` → `food`).
2. **Longest match across merged tables** must not let a short generic keyword beat a more specific one from another source.
3. **Cache poisoning reaching chat:** a wrong Gemini answer cached from a receipt must only suggest a category in chat, never auto-save.
4. **DB hot reload:** after `bill_keywords` changes, the next resolve uses the new data; a request during rebuild never sees a half-built index.
5. **Typo matching now reaches chat:** `nuoc muoi` ≠ `nuoc suoi`-style false positives must not appear in chat either.

---

## File Structure

| File | Responsibility |
|---|---|
| `ai-service/app/core/category_resolver.py` (new) | Merged keyword index, `resolve()`, typo matching |
| `ai-service/app/bill/knowledge.py` | Mark the index stale when DB keyword rows are applied |
| `ai-service/app/bill/item_classifier.py` | Rule/fuzzy step delegates to the resolver |
| `ai-service/app/chat/parser.py`, `app/api/v1/chat.py` | Chat category via the resolver; cache lookup |
| `ai-service/tests/fixtures/eval/consistency_cases.jsonl` (new), `scripts/eval_flows.py` | Same-phrase-same-category check across both paths |
| `.context/ARCHITECTURE.md`, `.context/TASKS.md`, `docs/evaluation.md` | Docs |

---

### Task 1: Frozen consistency set and baseline

**Files:**
- Create: `ai-service/tests/fixtures/eval/consistency_cases.jsonl`
- Modify: `ai-service/scripts/eval_flows.py`
- Test: `ai-service/tests/unit/test_eval_flows.py`

**Interfaces:**
- Produces: `eval_flows.run_consistency() -> dict` with keys `total`, `agree`, `agree_rate`, `disagreements: list[(phrase, chat_slug, item_slug, expected)]`, `correct` (both equal expected). Fixture line: `{"phrase": "ta quan Bobby", "expect": "family"|"unknown"}`. CLI flag `--consistency`.

- [ ] **Step 1:** Write ≥50 phrases BEFORE touching any rule (cover: ta/sữa bột/vitamin/thuốc/bột giặt/khăn ướt/nước ngọt/học phí/sách/khám bệnh/điện/internet/xăng/vé phim/cắt tóc/cà phê/bánh mì/ambiguous `nước`, `sửa xe`, `sửa nhà`, codes). `expect` follows the approved rules draft; never edit expectations to pass a run.
- [ ] **Step 2:** Test `test_consistency_fixture_is_valid_and_runner_reports` asserting every `expect` ∈ `CATEGORY_SLUGS ∪ {"unknown"}` and `run_consistency()` returns the keys above with `0 ≤ agree_rate ≤ 1`.
- [ ] **Step 3:** Implement `run_consistency()`: chat slug = `_slug(parse_chat(phrase + " 50k", None, fallback_category=_lexicon_category).entry.category)`; item slug = `classify_items` with a no-op knowledge and a Gemini that always answers `unknown`. Print agreement and each disagreement.
- [ ] **Step 4:** Run `pytest tests/unit/test_eval_flows.py -q` → PASS. Save the baseline output under "Baseline unify 2026-10-12" in `docs/evaluation.md`.
- [ ] **Step 5:** Commit `test: frozen cross-path consistency set and baseline`.

### Task 2: `category_resolver` core

**Files:**
- Create: `ai-service/app/core/category_resolver.py`
- Test: `ai-service/tests/unit/test_category_resolver.py`

**Interfaces:**
- Produces:
  - `@dataclass(frozen=True) class Resolution: slug: str | None; source: str  # "keyword" | "fuzzy" | "none"`
  - `def resolve(text: str) -> Resolution` — accent-folded, whole-word match, **longest keyword wins**; equal length → more specific slug wins by `_SPECIFIC_FIRST` = `("family", "education", "health", "housing", "bills", "entertainment", "transport", "shopping", "food", "other")`; no keyword → fuzzy (stdlib `difflib`, keyword ≥ 7 chars, ratio ≥ 0.85, only one OCR-confusable letter or one missing/extra letter — move `_fuzzy_rule`/`_ocr_typo` logic from `item_classifier.py` here unchanged); else `Resolution(None, "none")`.
  - `def invalidate() -> None` — drops the built index; the next `resolve` rebuilds it (build the new index fully, then assign once).
  - Sources merged at build time: `app.bill.classifier._PRODUCT_KEYWORDS` (local group → slug via `SLUG_BY_LOCAL_CATEGORY`), `app.chat.vocab.CHAT_KEYWORDS` expense keys only (local group or backend slug, same mapping tolerance as `app/api/v1/chat.py::_slug`), and `app/data/merchant_categories.json` via `load_lexicon()` excluding `income`.
- Consumes: nothing from earlier tasks.

- [ ] **Step 1: tests:**
  - `test_same_phrase_same_answer_for_the_phrases_that_disagreed_before`: `ta quan Bobby`→`family`, `sua bot Enfa`→`family`, `hoc phi`→`education`, `kham benh`→`health`, `bot giat Omo`→`shopping`, `vitamin C`→`health`, `nuoc ngot Pepsi`→`food`, `khan uot`→`shopping`, `sach giao khoa`→`education`.
  - `test_equal_length_tie_goes_to_the_specific_slug` (`sua bot` is `family`, not `food`).
  - `test_longest_keyword_wins_across_sources` (`mua sua bot` beats `mua sua`; `sua xe` beats any `sua`).
  - `test_ambiguous_words_stay_none`: bare `nuoc`, `mua nuoc`, `sua nha`, `SP002993`, `A12`, `""` → `slug is None`.
  - `test_tien_nuoc_is_housing_and_nuoc_ngot_is_food`.
  - `test_one_letter_typo_matches_but_two_real_words_do_not` (`kham uot`→`shopping`; `nuoc muoi`→`None`).
  - `test_invalidate_picks_up_edited_tables`: mutate `classifier._PRODUCT_KEYWORDS` with a new keyword, `invalidate()`, expect the new keyword resolves; restore in teardown.
  - `test_never_returns_income_or_unknown_slug`: every slug in the built index ∈ `CATEGORY_SLUGS - {"income"}`.
- [ ] **Step 2:** Run → FAIL (module missing).
- [ ] **Step 3:** Implement `app/core/category_resolver.py` per the Interfaces block.
- [ ] **Step 4:** Run the new file and the whole `tests/unit` → PASS.
- [ ] **Step 5:** Commit `feat(ai): shared category resolver`.

### Task 3: Receipt items use the resolver

**Files:**
- Modify: `ai-service/app/bill/item_classifier.py` (rule + fuzzy steps), `ai-service/app/bill/knowledge.py` (`apply_rows` ends with `category_resolver.invalidate()`)
- Test: `ai-service/tests/unit/test_item_classifier.py`, `ai-service/tests/integration/test_bill_knowledge.py`

**Interfaces:**
- Consumes: `resolve(text) -> Resolution`, `invalidate()`.
- Produces: no new public names. `classify_items` result for a phrase equals `resolve(...)` when the resolver has an answer (`source` stays `"rule"`).

- [ ] **Step 1: tests:** `test_item_rule_step_matches_the_resolver` (loop the Task 2 phrases through `classify_items` with `allow_ai=False`, no merchant: slug equals `resolve(phrase).slug`); `test_sua_bot_is_family_not_food_on_a_receipt`; keep every existing test in the file green; integration `test_database_keywords_reach_the_resolver_after_refresh` (insert a `bill_keywords` product row, `refresh_if_stale()`, expect `resolve(keyword).slug` to be the mapped slug).
- [ ] **Step 2:** Run → the first two FAIL.
- [ ] **Step 3:** In `classify_items`, replace the `_category_from_products([item.name], None)` rule step and the `_fuzzy_rule` step by one call to `resolve(item.name)`; delete `_fuzzy_rule`, `_ocr_typo`, `_OCR_PAIRS`, `_FUZZY_*` from this file (moved in Task 2). Keep `_category_from_products` itself untouched (whole-bill voting still uses it).
- [ ] **Step 4:** `pytest -q` (unit + integration) → PASS; `python scripts/eval_flows.py` → items correct ≥ 52, wrong-confident ≤ 2.
- [ ] **Step 5:** Commit `refactor(ai): receipt items resolve categories through the shared resolver`.

### Task 4: Chat uses the resolver

**Files:**
- Modify: `ai-service/app/chat/parser.py` (`category_from_text` expense path), `ai-service/app/api/v1/chat.py` (`_lexicon_category` removed from `parse_chat(..., fallback_category=...)`)
- Test: `ai-service/tests/unit/test_chat_parser.py`, `ai-service/tests/unit/test_chat_api.py`

**Interfaces:**
- Consumes: `resolve()`.
- Produces: `parse_chat(...)` keeps its signature; `fallback_category` stays optional and is simply no longer supplied by the route. Income types still come from `category_from_text(norm, "income")` (chat-only vocabulary).

- [ ] **Step 1: tests:** `test_chat_category_equals_the_resolver_for_known_phrases` (same Task 2 phrases through `parse_chat(phrase + " 50k")`); `test_bot_giat_and_vitamin_now_get_a_category_in_chat` (`bột giặt 80k`→`shopping`, `vitamin C 120k`→`health`); `test_mua_nuoc_20k_stays_unknown_and_not_auto_saved`; all existing chat tests green.
- [ ] **Step 2:** Run → new tests FAIL.
- [ ] **Step 3:** In `_entry_core`, expense category = `resolve(description).slug` with the existing 9-group vocabulary kept only as the source of income types; remove the route's `_lexicon_category` hand-off and keep `_slug` for the response mapping. Entry.category now holds a backend slug; update `_slug` to accept it (it already tolerates both kinds).
- [ ] **Step 4:** `pytest -q` → PASS; `python scripts/eval_flows.py --chat` → correct ≥ 62, wrong-confident ≤ 1.
- [ ] **Step 5:** Commit `refactor(ai): chat resolves categories through the shared resolver`.

### Task 5: Chat reads the item cache (suggest only)

**Files:**
- Modify: `ai-service/app/api/v1/chat.py` (inject `BillKnowledge`), `ai-service/app/chat/parser.py` (accept an already-resolved cache slug)
- Test: `ai-service/tests/unit/test_chat_api.py`

**Interfaces:**
- Consumes: `knowledge.cached_items(keys: list[str]) -> dict[str, str]` and `item_key(name) -> str` (`app/bill/item_classifier.py`).
- Produces: `parse_chat(..., cached_category: Callable[[str], str | None] | None = None)`; used only when `resolve()` returns `None`; an entry categorized this way has `auto_save_ok = False`.

- [ ] **Step 1: tests (API, override `get_bill_knowledge` with a fake):** cache hit fills `category_slug` for an unknown phrase and `auto_save_ok` is `False`; a rule hit never consults the cache; cache error/empty → category `None`, request still 200; the route works with no knowledge dependency override.
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3:** Implement: route awaits `cached_items([item_key(description)])` only when the rule step returned `None`; passes the slug into `parse_chat` as above.
- [ ] **Step 4:** `pytest -q` → PASS.
- [ ] **Step 5:** Commit `feat(ai): chat suggests categories from the item cache`.

### Task 6: Re-measure and document

**Files:**
- Modify: `docs/evaluation.md`, `.context/ARCHITECTURE.md` (§3.6 chat parser, §3.7 category vocabulary: one resolver, merge rules), `.context/TASKS.md`

- [ ] **Step 1:** Run `python scripts/eval_flows.py` (chat, items, `--consistency`). Append "After unify" next to the baseline with agreement before/after and every phrase that still disagrees (with the reason).
- [ ] **Step 2:** Update the docs to match code (resolver order, priority list, what is NOT unified: whole-bill brand/vote logic, DB tables still 9-group, ocr-service keeps its own copy).
- [ ] **Step 3:** Full `pytest -q`; rebuild `ai-service` and run the chat/scan smoke from `capture.py`-style requests on the Docker stack (multi-entry chat, `bột giặt 80k`, one split-bill scan).
- [ ] **Step 4:** Commit `docs: unified category resolver`.

---

## Not in this plan

- Gemini for chat (needs consent copy for chat text) and per-user preferences.
- Migrating `bill_brands`/`bill_keywords` to backend slugs, or deleting the ocr-service copies of the tables.
- Whole-bill classification (`classify_category_detailed`: brand, store keyword, line voting) stays as is.
- Voice: uses the chat path, so it benefits automatically when integrated.
