# Vòng tự học từ dữ liệu dùng thật Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Khép vòng "dùng thật → thu nhãn có đồng ý → đo → huấn luyện lại có cổng chặn → phục vụ" cho phân loại danh mục hóa đơn, thay cho tình trạng hiện nay (chỉ có bộ nhớ, không có dữ liệu thật để huấn luyện).

**Architecture:** Khi người dùng đồng ý (cờ riêng, mặc định tắt), backend gửi chữ hóa đơn ĐÃ lọc sang ai-service lưu thành mẫu "chờ nhãn"; khi người dùng lưu hoặc sửa danh mục, backend gắn nhãn cuối cho mẫu đó. Một script báo cáo cho biết đã đủ dữ liệu chưa; script huấn luyện chỉ tạo bản "candidate" khi vượt cổng và hơn luật hiện tại; ai-service nạp bản đã được duyệt làm một tầng nằm trước Gemini. Không đụng `ocr-service` (đang có phiên khác sửa).

**Tech Stack:** FastAPI + SQLAlchemy + alembic (ai-service), scikit-learn (TF-IDF + LogisticRegression, như `ocr-service/category_model.py`), ASP.NET Core 9 + EF Core + xUnit/Moq (backend), Flutter + Riverpod (app).

**Spec:** Không có tài liệu spec riêng. Căn cứ: phân tích hiện trạng ngày 2026-10-13 trong hội thoại (bảng `user_feedback` = 0 dòng, `bill_model_versions` 5 bản đều `rejected` do CV 71-79% < 80%, `ocr-service/main.py` không bao giờ nhận cờ `share_data` từ backend, `/merchant-category` không được backend gọi) và phần "tự học" của `finance_ai_pipeline_prompt.md`. Mọi quyết định bên dưới là provisional vì thiếu spec.

## Global Constraints

- Ảnh và âm thanh KHÔNG bao giờ được lưu hay gửi để học. Chỉ chữ đã lọc dữ liệu cá nhân của ocr-service.
- Đồng ý thu dữ liệu huấn luyện là cờ RIÊNG `ContributeDataForTraining`, mặc định `false`. Không dùng lại `ShareReceiptItemsWithAi` (cờ đó chỉ cho phép gửi tên món cho Gemini).
- Log không bao giờ ghi chữ hóa đơn, tên cửa hàng, số tiền hay danh mục (AGENTS.md §3.2).
- Người dùng ai-service luôn là `user_id_hash` (SHA-256), không phải userId.
- Nhãn hợp lệ là một trong `CATEGORY_SLUGS` của ai-service trừ `income` (11 slug backend: food, transport, shopping, education, housing, bills, entertainment, health, family, other + bỏ income).
- Model không bao giờ tự phục vụ: `train` → `candidate`; chỉ lệnh `promote` mới đổi sang `active`. Cổng tối thiểu: ≥300 mẫu có nhãn, ≥30 cửa hàng khác nhau, ≥3 danh mục mỗi danh mục ≥20 mẫu, CV nhóm theo cửa hàng ≥0,85 và hơn độ chính xác của luật hiện tại trên cùng mẫu.
- Mọi commit kết thúc bằng `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`. Không push, không merge.

## Review Focus

- Người dùng rút đồng ý giữa lúc quét và lúc lưu: không được gửi nhãn (Task 3 test `NoLabelWhenConsentWithdrawnBeforeSave`).
- Mẫu chờ nhãn của lần quét không bao giờ được lưu: phải bị xóa sau 7 ngày (Task 2 test `purge_removes_old_pending`).
- Gắn nhãn hai lần cho cùng lần quét (bấm lưu lại, sửa danh mục hai lần): nhãn cuối thắng, không tạo bản ghi thứ hai (Task 2 test `relabel_updates_same_row`).
- Chữ còn lọt số điện thoại/tài khoản (chuỗi ≥9 chữ số): bị từ chối khi lưu mẫu (Task 2 test `rejects_lines_with_long_digit_runs`).
- Xóa dữ liệu theo người dùng (luồng AdminDataDeletion): mẫu của người đó phải bị xóa theo (Task 2 test `delete_by_user_hash`).

---

## File Structure

- Backend: `Domain/Entities/User.cs` (+cờ), migration EF mới, `Auth/Commands/UpdateUserProfileCommand*.cs` + `UserProfileDto.cs` (+cờ), `Common/Interfaces/IAIServiceClient.cs` + `Infrastructure/ExternalServices/{AIServiceClient,IAIServiceApi,AIServiceApiContracts}.cs` (+2 lời gọi), `Transactions/Commands/{ScanReceiptCommandHandler,CreateManualTransactionCommandHandler,UpdateTransactionCommandHandler}.cs` (gửi mẫu, gắn nhãn).
- ai-service: `alembic/versions/0010_bill_training_samples.py`, `app/db/models/bill.py` (+`BillTrainingSample`), `app/bill/samples.py` (mới: ghi/gắn nhãn/xóa/kiểm tra PII), `app/api/v1/bill.py` (+2 route), `scripts/learning_report.py`, `scripts/train_bill_model.py`, `scripts/purge_training_samples.py`, `app/bill/slug_model.py` (nạp và dự đoán), `app/bill/item_classifier.py` + `app/api/v1/bill.py` (+tầng model).
- App: `lib/features/profile/presentation/screens/profile_screen.dart`, `lib/features/auth/{domain/entities/user_profile.dart,data/auth_repository.dart,presentation/controllers/auth_controller.dart}` (+cờ).
- Tài liệu: `docs/learning-loop.md` (runbook), `.context/TASKS.md`.

---

### Task 1: Cờ đồng ý `ContributeDataForTraining` (backend)

**Files:**
- Modify: `backend/FinMate.Domain/Entities/User.cs`, `backend/FinMate.Application/Auth/Commands/UpdateUserProfileCommand.cs`, `UpdateUserProfileCommandHandler.cs`, `backend/FinMate.Application/Common/Models/UserProfileDto.cs`, `GetUserProfileQueryHandler.cs`, `RegisterCommandHandler.cs` (chỗ dựng `UserProfileDto`)
- Create: migration EF `AddContributeDataForTraining` trong `backend/FinMate.Infrastructure/Persistence/Migrations/`
- Test: `backend/FinMate.Tests/Unit/Application/Auth/` (cạnh test của `ShareReceiptItemsWithAi` hiện có)

**Interfaces:**
- Produces: `User.ContributeDataForTraining: bool` (mặc định false); `UserProfileDto.ContributeDataForTraining`; `UpdateUserProfileCommand(..., bool? ContributeDataForTraining = null)`.

- [ ] **Step 1: Viết test (RED)** — theo đúng mẫu test của `ShareReceiptItemsWithAi`: `ContributeDataForTraining_DefaultsToFalse`, `UpdateProfile_SetsContributeFlag_WithoutTouchingShareReceiptItems` (đặt true, `ShareReceiptItemsWithAi` giữ nguyên), `UpdateProfile_NullLeavesFlagUnchanged`.
- [ ] **Step 2: Chạy** `dotnet test backend/FinMate.Tests --filter "FullyQualifiedName~ContributeDataForTraining"`. Expected: FAIL (biên dịch lỗi, thiếu thuộc tính).
- [ ] **Step 3: Implement** thuộc tính, tham số lệnh, DTO và xử lý cập nhật y hệt cách làm của `ShareReceiptItemsWithAi`; tạo migration bằng `dotnet ef migrations add AddContributeDataForTraining` (cột `boolean not null default false`).
- [ ] **Step 4: Chạy** bộ test ở Step 2 và toàn bộ `--filter "FullyQualifiedName~FinMate.Tests.Unit"`. Expected: PASS, không test cũ nào hỏng.
- [ ] **Step 5: Commit** `feat(be): separate consent flag for contributing training data`.

---

### Task 2: ai-service nhận mẫu chờ nhãn và gắn nhãn

**Files:**
- Create: `ai-service/alembic/versions/0010_bill_training_samples.py`, `ai-service/app/bill/samples.py`, `ai-service/scripts/purge_training_samples.py`, `ai-service/tests/unit/test_bill_samples.py`
- Modify: `ai-service/app/db/models/bill.py` (+`BillTrainingSample`), `ai-service/app/schemas/bill.py` (+schema), `ai-service/app/api/v1/bill.py` (+2 route)

**Interfaces:**
- Produces (bảng `bill_training_samples`): `scan_id uuid pk` (= `ReceiptScan.Id` của backend), `user_id_hash char(64)`, `merchant_key text null`, `lines text not null` (nối `\n`), `total_amount bigint null`, `predicted_slug varchar(32) null`, `predicted_source varchar(16) null`, `final_slug varchar(32) null` (CHECK ∈ slug trừ income), `label_kind varchar(12) null` (`confirmed`|`corrected`), `created_at`, `labeled_at null`.
- Produces (route, cùng `verify_internal_api_key`): `POST /api/v1/bill/samples` body `{scan_id, user_id_hash, merchant_name?, lines[], total_amount?, predicted_slug?, predicted_source?}` → `{stored: bool}`; `POST /api/v1/bill/samples/{scan_id}/label` body `{user_id_hash, final_slug}` → `{labeled: bool}` (`label_kind` = `confirmed` nếu `final_slug == predicted_slug` ngược lại `corrected`).
- Produces (`app/bill/samples.py`): `async def add_pending(session, sample: PendingSample) -> bool`, `async def label(session, scan_id: UUID, user_id_hash: str, final_slug: str) -> bool`, `async def delete_by_user_hash(session, user_id_hash: str) -> int`, `def has_long_digit_run(lines: list[str]) -> bool` (chuỗi ≥9 chữ số liên tiếp kể cả có khoảng trắng/chấm/gạch).

- [ ] **Step 1: Viết test (RED)** trong `test_bill_samples.py`: `add_pending_stores_row`, `rejects_lines_with_long_digit_runs` (`"0912 345 678"` → `stored False`, không có hàng), `add_pending_is_idempotent_on_scan_id`, `label_confirmed_when_equal_prediction`, `label_corrected_when_different`, `relabel_updates_same_row` (gắn nhãn hai lần, vẫn 1 hàng, `final_slug` là nhãn cuối), `label_requires_matching_user_hash` (hash sai → `labeled False`), `label_rejects_income_slug`, `delete_by_user_hash` (trả số hàng đã xóa), `purge_removes_old_pending` (chỉ hàng `final_slug IS NULL` và cũ hơn 7 ngày), `route_requires_internal_key`. Dùng fixture DB như các test bill hiện có.
- [ ] **Step 2: Chạy** `pytest tests/unit/test_bill_samples.py -q`. Expected: FAIL (module chưa có).
- [ ] **Step 3: Implement** migration 0010 (`down_revision = "0009"`), model, `samples.py` (upsert `ON CONFLICT (scan_id)`; giới hạn tối đa 200 dòng × 200 ký tự; log chỉ ghi số lượng), hai route trong `bill.py`, và `scripts/purge_training_samples.py --days 7 [--dry-run]` theo khuôn `purge_raw_samples.py`.
- [ ] **Step 4: Chạy** `alembic upgrade head` trên DB thử rồi `pytest tests/unit -q`. Expected: PASS toàn bộ.
- [ ] **Step 5: Commit** `feat(ai): consented pending training samples with final labels`.

---

### Task 3: Backend gửi mẫu khi quét và gắn nhãn khi lưu/sửa

**Files:**
- Modify: `backend/FinMate.Application/Common/Interfaces/IAIServiceClient.cs`, `backend/FinMate.Infrastructure/ExternalServices/{AIServiceClient,IAIServiceApi,AIServiceApiContracts}.cs`, `ScanReceiptCommandHandler.cs`, `CreateManualTransactionCommandHandler.cs` (quanh dòng 133-175, nơi đã nối `ReceiptScanId` và gọi `ReceiptMemory.ShouldLearn`), `UpdateTransactionCommandHandler.cs` (quanh dòng 181)
- Test: `backend/FinMate.Tests/Unit/Application/Transactions/TrainingSampleTests.cs`, `backend/FinMate.Tests/Unit/Infrastructure/AIServiceClientTrainingSampleTests.cs`

**Interfaces:**
- Consumes: Task 1 `User.ContributeDataForTraining`; Task 2 route.
- Produces: `IAIServiceClient.RecordTrainingSampleAsync(TrainingSampleRequest request, CancellationToken ct = default): Task` và `LabelTrainingSampleAsync(TrainingLabelRequest request, CancellationToken ct = default): Task`, cả hai best-effort (nuốt lỗi như `LearnMerchantAsync`); `record TrainingSampleRequest(Guid ScanId, Guid UserId, string? MerchantName, IReadOnlyList<string> Lines, long? TotalAmount, string? PredictedSlug, string? PredictedSource)`; `record TrainingLabelRequest(Guid ScanId, Guid UserId, string FinalSlug)`.

- [ ] **Step 1: Viết test (RED)**: `SendsPendingSampleWhenConsentedAndWholeBill` (đúng các trường, `Lines` là chữ đã lọc từ OCR), `NoSampleWhenNotConsented`, `NoSampleForOrderListOrSplitScan` (nhiều đơn hoặc tách món thì không gửi ở phiên bản này), `LabelsWhenReceiptTransactionSavedWithLinkedScan`, `RelabelsOnCategoryCorrection`, `NoLabelWhenConsentWithdrawnBeforeSave` (cờ về false giữa hai bước), `NoLabelWhenTransactionNotFromReceipt`, `FailureOfAiServiceNeverBreaksSaving`; test client: gửi đúng đường dẫn và JSON snake_case khớp Task 2, nuốt lỗi mạng.
- [ ] **Step 2: Chạy** `dotnet test backend/FinMate.Tests --filter "FullyQualifiedName~TrainingSample"`. Expected: FAIL.
- [ ] **Step 3: Implement** hai lời gọi (Refit + contracts), gửi mẫu trong `ScanReceiptCommandHandler` sau khi có `ReceiptScan.Id` (chỉ nhánh một nhóm, `Readable`, có `Lines`, user đồng ý); gắn nhãn trong hai handler ở điểm đã gọi `LearnMerchantAsync`, điều kiện thêm: user vẫn đồng ý, giao dịch nối với một lần quét. `PredictedSlug`/`PredictedSource` lấy từ `ScannedReceiptDto.CategorySlug`/`CategorySource`.
- [ ] **Step 4: Chạy** bộ test Step 2 + toàn bộ `FinMate.Tests.Unit`. Expected: PASS.
- [ ] **Step 5: Commit** `feat(be): send consented receipt samples and final labels to ai-service`.

---

### Task 4: Nút đồng ý trong app

**Files:**
- Modify: `lib/features/auth/domain/entities/user_profile.dart`, `lib/features/auth/data/auth_repository.dart`, `lib/features/auth/presentation/controllers/auth_controller.dart`, `lib/features/profile/presentation/screens/profile_screen.dart`
- Test: `test/features/auth/user_profile_test.dart`, `test/features/profile/profile_screen_test.dart`

**Interfaces:**
- Consumes: Task 1 trường JSON `contributeDataForTraining`.
- Produces: `UserProfile.contributeDataForTraining: bool` (mặc định false khi BE cũ không trả), `AuthController.setContributeDataForTraining(bool)` theo khuôn `ShareReceiptItemsWithAi`.

- [ ] **Step 1: Viết test (RED)**: parse có/không có trường; công tắc trong màn Cá nhân hiện lời giải thích ngắn ("Chỉ gửi chữ hóa đơn đã ẩn thông tin cá nhân, không gửi ảnh, tắt bất cứ lúc nào") và gọi cập nhật khi bật; mặc định tắt.
- [ ] **Step 2: Chạy** `flutter test test/features/auth/user_profile_test.dart test/features/profile/profile_screen_test.dart`. Expected: FAIL.
- [ ] **Step 3: Implement** theo khuôn công tắc `shareReceiptItemsWithAi` đang có (hai công tắc tách biệt, lời giải thích riêng).
- [ ] **Step 4: Chạy** `flutter test` và `flutter analyze`. Expected: toàn bộ pass, analyze sạch.
- [ ] **Step 5: Commit** (repo FE tách: `fe add lib test && fe commit`) `feat(profile): toggle for contributing anonymised receipt text`.

---

### Task 5: Báo cáo mức sẵn sàng và độ chính xác của luật trên dữ liệu thật

**Files:**
- Create: `ai-service/scripts/learning_report.py`, `ai-service/tests/unit/test_learning_report.py`

**Interfaces:**
- Consumes: Task 2 bảng `bill_training_samples`.
- Produces: `async def build_report(session) -> LearningReport` với `labeled`, `pending`, `merchants` (khác nhau), `per_slug: dict[str, int]`, `rule_accuracy: float | None` (luật hiện tại `classify_category_detailed` + `resolve` đổi sang slug, chấm trên các mẫu có nhãn), `ready: bool` (đủ ngưỡng Global Constraints), `missing: list[str]` (vd. "cần thêm 120 mẫu"). CLI in bảng tiếng Việt.

- [ ] **Step 1: Viết test (RED)**: `report_counts_labeled_and_pending`, `ready_false_below_thresholds_lists_what_is_missing`, `ready_true_when_all_thresholds_met`, `rule_accuracy_none_without_labels`, `rule_accuracy_matches_hand_computed`.
- [ ] **Step 2: Chạy** `pytest tests/unit/test_learning_report.py -q`. Expected: FAIL.
- [ ] **Step 3: Implement** `build_report` và CLI `python scripts/learning_report.py`.
- [ ] **Step 4: Chạy** test Step 2. Expected: PASS.
- [ ] **Step 5: Commit** `feat(ai): learning readiness report`.

---

### Task 6: Huấn luyện bản candidate có cổng chặn

**Files:**
- Create: `ai-service/scripts/train_bill_model.py`, `ai-service/app/bill/slug_model.py` (phần huấn luyện), `ai-service/tests/unit/test_train_bill_model.py`
- Modify: bảng `bill_model_versions` dùng `stage="bill_slug"` (không cần migration nếu cột `stage` là text; nếu có CHECK thì thêm vào 0010)

**Interfaces:**
- Consumes: Task 5 `build_report` (cổng), Task 2 dữ liệu.
- Produces: `train(rows: list[SampleRow]) -> TrainResult(status: "candidate"|"rejected", reason: str, cv_accuracy: float | None, rule_accuracy: float | None, pipeline: Pipeline | None)`; pipeline giống `ocr-service/category_model._pipeline()` (TF-IDF từ 1-2-gram + ký tự `char_wb` 3-5, `LogisticRegression(C=10, class_weight="balanced")`); CV theo nhóm `merchant_key` (không để cùng một cửa hàng nằm ở cả train lẫn test); `promote(version_id)` ghi `joblib` vào `{MODEL_REGISTRY_PATH}/bill_slug/<version>.joblib` rồi đặt `active` (và `retired` bản trước).

- [ ] **Step 1: Viết test (RED)**: `rejected_below_sample_gate`, `rejected_when_cv_below_0_85`, `rejected_when_not_better_than_rules`, `candidate_when_all_gates_pass` (dữ liệu giả lập có tín hiệu rõ), `cv_groups_by_merchant` (không rò rỉ: cùng cửa hàng không xuất hiện ở hai bên), `train_never_activates` (sau `train` không có hàng `active`), `promote_retires_previous_active`.
- [ ] **Step 2: Chạy** `pytest tests/unit/test_train_bill_model.py -q`. Expected: FAIL.
- [ ] **Step 3: Implement** `train`/`promote` và CLI (`train_bill_model.py --train`, `--promote <id>`); mỗi lần chạy (kể cả bị loại) ghi `bill_model_versions` + `bill_training_jobs` như `training_pg.py` đang làm.
- [ ] **Step 4: Chạy** test Step 2 rồi `pytest tests/unit -q`. Expected: PASS.
- [ ] **Step 5: Commit** `feat(ai): gated training of bill slug model`.

---

### Task 7: Phục vụ model đã duyệt trước Gemini

**Files:**
- Modify: `ai-service/app/bill/slug_model.py` (phần nạp/dự đoán), `ai-service/app/bill/item_classifier.py`, `ai-service/app/api/v1/bill.py` (`classify_bill`)
- Test: `ai-service/tests/unit/test_slug_model_serving.py`

**Interfaces:**
- Consumes: Task 6 file `.joblib` của bản `active`.
- Produces: `SlugModel.load(registry_path) -> SlugModel | None` (None khi không có bản active); `SlugModel.predict(merchant: str | None, lines: list[str]) -> tuple[str, float]`; hằng `MIN_PROBABILITY = 0.8`. Nguồn trả về là `"model"`. Thứ tự mới của `/bill/classify`: bộ nhớ người dùng → thương hiệu/từ khóa → **model (≥0,8)** → none. Thứ tự mới của `classify_items`: thừa kế → luật → bộ nhớ tên món → **model** → Gemini → nhóm mặc định → unknown (model chỉ áp cho món chưa quyết).

- [ ] **Step 1: Viết test (RED)**: `no_active_model_changes_nothing` (kết quả y hệt hiện tại), `model_used_only_when_rules_and_cache_miss`, `model_below_threshold_falls_through_to_gemini`, `model_hit_skips_gemini_call`, `health_bill_still_never_reaches_gemini_or_model_override` (hóa đơn thuốc: hành vi hiện tại giữ nguyên), `corrupt_model_file_is_ignored_with_warning`.
- [ ] **Step 2: Chạy** `pytest tests/unit/test_slug_model_serving.py -q`. Expected: FAIL.
- [ ] **Step 3: Implement** nạp model một lần khi khởi động (và nạp lại qua endpoint reload có sẵn nếu có), chèn tầng ở đúng thứ tự trên; không đổi chat (chat không dùng model này).
- [ ] **Step 4: Chạy** `pytest tests/unit -q` và `python scripts/eval_flows.py --items --consistency`. Expected: PASS, kết quả đánh giá không tệ hơn mốc trong `docs/evaluation.md`.
- [ ] **Step 5: Commit** `feat(ai): serve approved slug model before Gemini`.

---

### Task 8: Runbook và nhịp vận hành

**Files:**
- Create: `docs/learning-loop.md`; Modify: `.context/TASKS.md` (Phase 28), `docs/evaluation.md` (mốc "luật trên dữ liệu thật" khi có)

- [ ] **Step 1: Viết** runbook: bật cờ cho người thử, lệnh hằng tuần (`learning_report.py` → khi `ready` thì `train_bill_model.py --train` → xem lý do/độ chính xác → `--promote`), xóa mẫu chờ (`purge_training_samples.py --days 7` theo lịch), xóa theo người dùng, và cách quay lại (retire bản active).
- [ ] **Step 2: Chạy thử** toàn bộ quy trình trên DB thử với dữ liệu giả để chắc lệnh trong tài liệu đúng. Expected: mỗi lệnh chạy được như tài liệu mô tả.
- [ ] **Step 3: Commit** `docs: learning loop runbook`.

---

## Lộ trình theo hai sơ đồ mục tiêu

Hai sơ đồ bạn gửi ngày 13/10 (luồng hóa đơn và vòng học từ phản hồi) khớp với plan này như sau:

| Sơ đồ | Bước | Task | Trạng thái |
|---|---|---|---|
| Vòng học | Người dùng xác nhận/sửa | có sẵn | xong |
| Vòng học | Lưu dữ liệu đã xác nhận | 1, 2, 3, 4 | **Giai đoạn 1** |
| Vòng học | Kiểm duyệt, bổ sung dataset | 5 (báo cáo) | **Giai đoạn 1** |
| Vòng học | Retrain + Evaluate → triển khai | 6, 7, 8 | **Giai đoạn 2** |
| Hóa đơn | ML Category Classifier | 6, 7 | Giai đoạn 2 (chờ đủ dữ liệu) |
| Hóa đơn | Confidence & Conflict Checker | Giai đoạn 3 | chưa làm |
| Hóa đơn | Số lượng, đơn giá từng món | Giai đoạn 3 | chưa làm |

**Giai đoạn 1 (Task 1 đến 5): thu dữ liệu.** Tự nó đã đưa vào dùng được và là thứ có thời hạn: mỗi tuần chưa bật thu là mất một tuần dữ liệu. Làm trước và cho vài người thử bật cờ ngay sau khi xong. Task 5 cho biết khi nào đủ để sang giai đoạn 2.

**Giai đoạn 2 (Task 6 đến 8): huấn luyện và phục vụ.** Chỉ bắt đầu khi `learning_report.py` báo `ready = true`. Nếu báo chưa đủ thì không làm, và việc đó không phải là sự cố.

**Giai đoạn 3: bổ sung cho khớp sơ đồ hóa đơn, mỗi mục là một plan riêng.**
1. *Điểm tin cậy từng món:* thay nhãn nguồn (`rule`, `cache`, `ai`...) bằng thêm một điểm số, để "Confidence & Conflict Checker" quyết định theo ngưỡng thay vì chỉ theo nguồn. Cần có model ở Task 7 trước vì chỉ model mới có xác suất.
2. *Nhãn cấp món cho hóa đơn tách nhiều nhóm:* Task 3 cố ý bỏ qua hóa đơn tách nhóm. Sơ đồ phân loại từng dòng sản phẩm nên cuối cùng cần nhãn cấp món. Mở rộng `bill_training_samples` thêm một bảng món con.
3. *Số lượng và đơn giá:* mở rộng `Item` ở `ocr-service/items.py` (hiện chỉ `name`, `amount`, `parent`, `is_discount`). Chạm vào `ocr-service` nên chờ phiên khác sửa xong `main.py`/`ocr.py`, và cần ví dụ hóa đơn thật có cột số lượng.
4. *Chat dùng bộ nhớ riêng của người dùng:* hiện `_with_cached_categories` chỉ tra bảng chung `bill_item_cache`.

## Ngoài phạm vi (cố ý)

- **Rasa học từ chat:** cần quyết định riêng về lưu câu chat (câu chat chưa qua bộ lọc dữ liệu cá nhân như hóa đơn) và nhãn ý định. Làm kế hoạch riêng sau khi vòng hóa đơn chạy.
- **STT học từ giọng người dùng:** lưu âm thanh nặng về quyền riêng tư; không làm.
- **Nhãn cấp món cho hóa đơn tách nhiều nhóm và danh sách đơn:** phiên bản này bỏ qua (Task 3). Mở rộng sau khi có dữ liệu cả hóa đơn.
- **Gửi `share_data` sang ocr-service / dùng `/merchant-category` / dataset JSONL cũ:** không dùng; kế hoạch này không đụng `ocr-service`.
- **Bảng `user_feedback` cho khoản từ hóa đơn/chat:** không cần; nhãn nằm ở `bill_training_samples`.

## Rủi ro đã biết

- Với ít người dùng, 300 mẫu và 30 cửa hàng có thể mất nhiều tuần; trong lúc đó Task 5 cho thấy tiến độ và luật + bộ nhớ vẫn làm việc như hiện nay. Nếu sau khi đủ dữ liệu mà model không hơn luật, kết quả đúng là "không bật model", không phải lỗi.
- Mẫu chờ nhãn chỉ nằm trong DB của ai-service tối đa 7 ngày nếu không được lưu; mẫu đã có nhãn giữ cho đến khi người dùng yêu cầu xóa dữ liệu.
- Các ngưỡng cổng (300/30/20/0,85) là giả định chưa kiểm chứng; chỉnh khi có dữ liệu thật.
