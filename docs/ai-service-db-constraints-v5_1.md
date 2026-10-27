# AI Service DB: ràng buộc còn thiếu theo ERD v5_1

Nguồn: ERD `docs/diagrams/erd-ai-service-optimized-v5_1.drawio` (31 bảng, 2 view). Cột "Đã có" lấy từ model SQLAlchemy và migration hiện tại (tên bảng cũ ghi trong ngoặc). Bảng mới hoặc bảng đã gộp chỉ có cột "Cần thêm", suy ra từ thiết kế.

**Đây là đặc tả, chưa phải migration.** Chưa có dòng DDL nào được chạy.

Mức ưu tiên:
- **P0**: thêm được ngay, không cần sửa dữ liệu cũ, không phá code đang chạy.
- **P1**: cần chuyển hoặc làm sạch dữ liệu trước, hoặc cần sửa code đi kèm.
- **P2**: phụ thuộc quyết định chưa chốt hoặc tính năng chưa làm.

## Quy tắc chung (áp dụng cho nhiều bảng)

| Quy tắc | Nội dung | Mức |
|---|---|---|
| Định dạng mã băm | Mọi cột `user_id_hash`, `backend_transaction_id_hash`, `actor_hash`, `created_by`, `dataset_hash`, `prompt_hash` thêm CHECK `~ '^[0-9a-f]{64}$'` (cột cho phép NULL thì `IS NULL OR ...`) | P0 |
| Danh mục | Mọi cột `category_slug`, `predicted_*`, `final_slug`, `corrected_category` là FK tới `kb_categories(slug)` `ON UPDATE CASCADE ON DELETE RESTRICT`, thay cho các CHECK liệt kê slug | P1 (chặn bởi quyết định về 9 nhóm cũ ở `kb_brands`, `kb_keywords`) |
| Danh mục chi tiêu | Các bảng chỉ nhận chi tiêu (`kb_item_categories`, `user_merchant_memory`, `ml_training_samples.final_slug`) giữ thêm CHECK `<> 'income'`, hoặc thêm cột `kind` vào `kb_categories` rồi dùng chung | P1 |
| Khóa đã chuẩn hóa | `merchant_key`, `item_key`, `text_key`: CHECK `= lower(...)` và độ dài tối thiểu; app đã chuẩn hóa nhưng DB chưa chặn | P0 |
| Cột kiểu JSON | CHECK `jsonb_typeof(...) = 'object'` cho `content`, `expected_fields`, `extra_fields`, `params`, `metrics` | P0 |
| Khoảng thời gian | CHECK `finished_at >= started_at` cho `ml_training_jobs`, `ml_feedback_batches` | P0 |
| Chỉ ghi thêm | `kb_audit_log` và `ml_label_events` không được UPDATE hay DELETE: thu quyền của vai trò ứng dụng hoặc dùng trigger | P1 |
| Chỉ mục khóa ngoại | Mọi cột FK mà bảng cha có `ON DELETE CASCADE` hoặc `SET NULL` cần có chỉ mục, nếu không mỗi lần xóa cha phải quét toàn bảng con | P0 |

## Ngăn Kiến thức (kb_)

| Bảng (tên cũ) | Đã có | Cần thêm | Mức |
|---|---|---|---|
| `kb_categories` (mới) | không | PK `slug`; CHECK `slug ~ '^[a-z_]+$'`; CHECK `taxonomy_version >= 1`; FK tự tham chiếu `replaced_by` `ON DELETE SET NULL`; CHECK `replaced_by <> slug`; cột `kind` hoặc cờ chi tiêu (xem quy tắc chung) | P1 |
| `kb_brands` (`bill_brands`) | UQ `brand_key` | FK `category_slug` (chặn bởi 9 nhóm cũ); CHECK `brand_key = lower(brand_key)`; CHECK `source IN ('seed','admin','vote')`; `mixed` NOT NULL DEFAULT false; chỉ mục `category_slug` | P0 trừ FK (P1) |
| `kb_keywords` (`bill_keywords`) | UQ `(kind, category_slug, keyword)`; CHECK `kind` | FK `category_slug` (chặn bởi 9 nhóm); CHECK `source IN (...)`; CHECK `category_priority >= 0`; chỉ mục từng phần `(category_priority, category_slug, keyword) WHERE is_active` cho truy vấn nạp kiến thức | P0 trừ FK (P1) |
| `kb_item_categories` (`bill_item_cache`) | PK `item_key`; CHECK danh mục chi tiêu | FK `category_slug`; CHECK `hits >= 1`; CHECK `source IN ('ai','admin','consensus')`; CHECK `char_length(item_key) BETWEEN 3 AND 60`; chỉ mục `hits DESC` cho màn hình quản trị | P0 |
| `kb_ai_instructions` (`bill_ai_instructions`) | UQ từng phần: một dòng active | CHECK `char_length(instruction_text) > 0`. Nếu gộp vào `config_documents` thì bảng này biến mất | P0 / P2 |
| `kb_provider_patterns` (`provider_patterns`) | UQ `(package_name, pattern_name)`; chỉ mục tra cứu; CHECK `transaction_type` | `format_id` NOT NULL sau khi nạp lại dữ liệu cũ; UQ `(format_id, pattern_name)`; chỉ mục `(format_id, is_active, priority)`; CHECK `priority >= 0`; bỏ `package_name` và `provider_key` (trùng với bảng định dạng); giới hạn thời gian chạy regex ở tầng code (nguy cơ ReDoS) | P1 |
| `kb_audit_log` (`bill_rule_audit`) | chỉ mục theo thời gian và `(resource, resource_key)` | CHECK `resource IN ('brand','keyword','item','instruction','provider_format')`; CHECK `action IN ('create','update','activate','deactivate','delete','revert')`; CHECK `actor_hash` là hex 64; chỉ ghi thêm (quy tắc chung) | P0 |
| `kb_provider_formats` (mới) | không | UQ `(package_name, version)`; CHECK `status IN ('draft','active','archived')`; CHECK `version >= 1`, `priority >= 0`; chỉ mục `(package_name, status, priority)` cho bộ khớp | P2 (tính năng chưa làm) |
| `kb_provider_format_fields` (mới) | không | UQ `(format_id, field_key)`; CHECK `maps_to IS NULL OR maps_to IN (6 trường của hợp đồng)`; UQ từng phần `(format_id, maps_to) WHERE maps_to IS NOT NULL` để hai trường không cùng ánh xạ vào một trường chuẩn; CHECK `source_kind IN ('regex_group','literal')`; CHECK `NOT (is_sensitive AND maps_to IS NULL)` (trường nhạy cảm không được đi vào `extra_fields`; **cần xác nhận với đặc tả F1-04**); chỉ mục `format_id` | P2 |
| `kb_provider_format_samples` (mới) | không | chỉ mục `format_id`; CHECK `jsonb_typeof(expected_fields) = 'object'`; CHECK `char_length(sample_text)` có trần; quy tắc ở tầng ứng dụng: mẫu phải đã ẩn dữ liệu cá nhân | P2 |

## Ngăn Trí nhớ riêng (user_)

| Bảng | Đã có | Cần thêm | Mức |
|---|---|---|---|
| `user_merchant_memory` (`bill_user_merchant_choice`) | UQ `(user_id_hash, merchant_key)`; CHECK danh mục chi tiêu | FK `category_slug`; CHECK mã băm hex 64; CHECK `merchant_key` đã chuẩn hóa; bỏ cột `display_name` (chỉ ghi, không đọc). Chỉ mục theo `user_id_hash` đã nằm trong UQ nên xóa theo người dùng chạy được | P0 / P1 |

## Ngăn Học máy (ml_)

| Bảng | Đã có | Cần thêm | Mức |
|---|---|---|---|
| `ml_models` (mới) | không | UQ `code`; CHECK `kind IN ('sklearn','rasa','llm','rules')` | P2 |
| `ml_model_versions` (`model_versions` + `bill_model_versions`) | UQ `(stage, version)`; UQ từng phần một bản active; CHECK `stage`, `status` | đổi sang UQ `(model_id, version)` và UQ từng phần `model_id WHERE status='active'`; FK `model_id` `ON DELETE RESTRICT`; chỉ mục `training_job_id`; CHECK `status IN ('candidate','active','retired','rejected')`; CHECK `prompt_hash` hex 64 khi không NULL; quy tắc ở code: `artifact_path` được NULL khi `ml_models.kind='llm'` (CHECK chéo bảng không làm được, cần trigger hoặc kiểm tra ở ứng dụng) | P1 |
| `ml_training_jobs` (`training_jobs` + `bill_training_jobs`) | CHECK `stage`, `status`; chỉ mục `(stage, created_at)` | FK `model_id`; chỉ mục `(model_id, created_at)` thay cho chỉ mục theo `stage`; CHECK `sample_count >= 0`; CHECK `dataset_hash` hex 64; CHECK thời gian | P1 |
| `ml_evaluation_runs` | FK `model_version_id` CASCADE; CHECK `split`; chỉ mục `(model_version_id, created_at)` | CHECK `accuracy` và `macro_f1` trong [0,1]; CHECK `sample_count >= 0` | P0 |
| `ml_evaluation_category_metrics` | UQ `(evaluation_run_id, label)` | CHECK `precision`, `recall`, `f1` trong [0,1]; CHECK `support >= 0`. Có thể gộp vào `ml_evaluation_runs.metrics` | P0 / P2 |
| `ml_evaluation_predictions` | FK CASCADE; chỉ mục `(evaluation_run_id, is_correct)` | UQ `(evaluation_run_id, labeled_sample_id)`; CHECK `confidence` trong [0,1]; chỉ mục `labeled_sample_id`; có thể biến `is_correct` thành cột sinh từ `predicted_label = true_label` | P0 |
| `ml_raw_samples` (`raw_samples`) | UQ `content_hash` (chống trùng bộ dữ liệu); CHECK `source`; chỉ mục | CHECK `user_id_hash` hex 64 khi không NULL; CHECK độ dài `notification_body` có trần; script dọn 90 ngày đã có | P0 |
| `ml_labeled_samples` (`labeled_samples`) | UQ `raw_sample_id`; CHECK `labeled_by`, `transaction_type`, hình dạng "không phải giao dịch"; chỉ mục theo danh mục | FK `category_slug`; nếu gộp `ml_sample_splits`: `split` NOT NULL kèm CHECK, `split_seed` có khoảng; chỉ mục từng phần `WHERE is_gold` | P1 |
| `ml_sample_splits` (`sample_splits`) | UQ `labeled_sample_id`; CHECK `split`; chỉ mục `split` | Ứng viên gộp vào `ml_labeled_samples` | P2 |
| `ml_user_feedback` (`user_feedback`) | CHECK `feedback_type`, hai CHECK danh mục; chỉ mục từng phần "chưa xử lý"; chỉ mục `pipeline_request_id` | FK hai cột danh mục; chỉ mục `batch_id`; CHECK mã băm hex 64; khóa chống gửi trùng phản hồi (BE hiện chưa gửi id yêu cầu, nên đây là đề xuất, cần BE gửi thêm) | P0 / P2 |
| `ml_feedback_batches` (`feedback_batch_jobs`) | CHECK `status` | CHECK các cột đếm `>= 0`; CHECK thời gian. Ứng viên gộp vào `ml_training_jobs` | P0 |
| `ml_label_events` (mới) | không | FK `labeled_sample_id` CASCADE; FK `feedback_id` SET NULL; FK `category_slug`; CHECK `source IN ('feedback','gold','import','user','system')`; CHECK `(is_financial OR category_slug IS NULL)`; chỉ mục `(labeled_sample_id, created_at DESC)` để lấy nhãn mới nhất; chỉ ghi thêm | P2 |
| `ml_training_samples` (`bill_training_samples`) | PK `scan_id`; CHECK `final_slug`; chỉ mục từng phần "chưa có nhãn"; chỉ mục `user_id_hash` | `sample_unit` NOT NULL DEFAULT 'bill' kèm CHECK `IN ('bill','item')`; FK `predicted_slug`, `final_slug`; CHECK `(final_slug IS NULL) = (labeled_at IS NULL)`; CHECK độ dài `lines` có trần; chỉ mục `(merchant_key, final_slug) WHERE final_slug IS NOT NULL` cho view đồng thuận | P1 |
| `ml_item_feedback` (mới) | không | PK `(item_key, user_id_hash)`; FK `category_slug`; CHECK mã băm; CHECK `char_length(item_key) BETWEEN 3 AND 60`; chỉ mục `(item_key, category_slug)` cho view đồng thuận. Không đặt FK tới `kb_item_categories` vì món có thể chưa có trong kiến thức | P2 |
| `v_merchant_consensus`, `v_item_consensus` (view mới) | không | Phụ thuộc hai chỉ mục ở trên; chỉ đếm `sample_unit='bill'`; mỗi người một phiếu (lấy nhãn mới nhất của mỗi `user_id_hash`) | P2 |

## Ngăn Vận hành (ops_)

| Bảng | Đã có | Cần thêm | Mức |
|---|---|---|---|
| `ops_analyze_log` (`pipeline_requests`) | UQ `backend_request_id`; chỉ mục dò trùng và `created_at`; CHECK các tập đóng; FK `raw_sample_id` SET NULL | chỉ mục `raw_sample_id`; CHECK ba cột độ tin cậy trong [0,1]; CHECK `amount_cents IS NULL OR amount_cents >= 0`; CHECK `processing_ms >= 0`; **FK tự tham chiếu `duplicate_of_request_id` `ON DELETE SET NULL`** (hiện là UUID trần, khi dọn dòng cũ sau 90 ngày sẽ treo con trỏ); CHECK `jsonb_typeof(extra_fields) = 'object'`; script dọn dòng cũ hơn 90 ngày | P0 |
| `ops_usage_daily` (`bill_classify_stats`) | PK `(day, metric)` | CHECK `value >= 0`; CHECK `metric ~ '^[a-z0-9_]+$'`; quy ước `day` là ngày UTC (hiện dùng `current_date` theo múi giờ phiên DB) | P0 |

## Ngăn Cấu hình và hiểu câu

| Bảng | Đã có | Cần thêm | Mức |
|---|---|---|---|
| `config_documents` | UQ `(name, version)`; UQ từng phần một bản active; CHECK `status` | CHECK `version >= 1`; CHECK `name ~ '^[a-z_]+$'`; CHECK `jsonb_typeof(content) = 'object'`; CHECK `created_by` hex 64 | P0 |
| `nlu_examples` | UQ `text_key`; chỉ mục; CHECK `split`, `source` | CHECK `char_length(text) <= 200` (khớp `MAX_TEXT_CHARS` trong code). Tên ý định nằm ở `domain.yml` nên giữ kiểm tra ở tầng ứng dụng | P0 |
| `nlu_model_versions` (mới) | không | UQ `version`; UQ từng phần `WHERE status='active'`; CHECK `status`. Ứng viên gộp vào `ml_model_versions` | P2 |
| `nlu_candidates` (mới) | không | UQ `(user_id_hash, text_key)`; chỉ mục `(status, created_at)`; CHECK `status`; CHECK `char_length(text) <= 500`; CHECK confidence [0,1]; **thời hạn lưu chưa chốt** (đề xuất dọn bản `pending` sau N ngày) | P2 |

## Việc nên làm trước, theo mức ưu tiên

**P0, làm ngay trong một migration không phá gì:**
1. CHECK định dạng mã băm, CHECK khoảng [0,1] và không âm cho các bảng đánh giá, nhật ký và đếm.
2. UQ `(evaluation_run_id, labeled_sample_id)` cho `ml_evaluation_predictions`.
3. Chỉ mục cho các khóa ngoại còn thiếu: `ml_model_versions.training_job_id`, `ml_user_feedback.batch_id`, `ops_analyze_log.raw_sample_id`, `ml_evaluation_predictions.labeled_sample_id`.
4. FK tự tham chiếu `ops_analyze_log.duplicate_of_request_id` (SET NULL).
5. Script dọn `ops_analyze_log` quá 90 ngày.

**P1, cần kế hoạch dữ liệu:** FK `category_slug` (sau khi chốt 9 nhóm cũ), `format_id` NOT NULL, gộp `model_id`, `sample_unit`.

**P2, theo tính năng:** các bảng và view mới.

## Đã làm thêm ở migration 0027-0029 (2026-10-26)
- **0027:** CHECK mã băm SHA-256 hex 64 cho `user_merchant_memory`, `ml_training_samples`, `kb_audit_log.actor_hash`, `config_documents.created_by`, `nlu_candidates`, `nlu_model_versions.dataset_hash` (cho thêm `'unknown'`), `ops_analyze_log`, `ml_raw_samples` (hai cột), `ml_user_feedback` (hai cột); trigger chỉ-ghi-thêm cho `kb_audit_log`; FK `ops_analyze_log.pattern_id` SET NULL; F1-04 `NOT (is_sensitive AND maps_to IS NULL)`; F1-09 bỏ CHECK `transaction_type` và `extraction_method` của `ops_analyze_log` (tập đóng chỉ còn `pipeline_result`, `classifier_label`).
- **0028:** `ml_raw_samples.source` `bill_scan` thành `receipt`; bỏ `ml_training_samples.total_amount` và `label_kind`; bỏ `provider_key`, `package_name` khỏi `kb_provider_patterns`.
- **0029:** `bill_user_merchant_category` và view `bill_merchant_votes` gộp vào `user_merchant_memory` rồi xóa. `bill_unknown_scans` GIỮ làm hàng đợi duyệt cửa hàng lạ, là bảng duy nhất ngoài ERD có chủ đích.

## Giới hạn của tài liệu này
- Mình chưa chạy thử bất kỳ CHECK hay UQ nào trên DB thật, nên chưa biết dữ liệu hiện có có vi phạm không. Cần chạy truy vấn kiểm tra trước khi thêm ràng buộc.
- Các dòng của bảng mới và bảng đã gộp là suy ra từ thiết kế, chưa có code đối chiếu.
- Mã `F1-xx` trong bộ định dạng nhà cung cấp thuộc một đặc tả mình không có.
