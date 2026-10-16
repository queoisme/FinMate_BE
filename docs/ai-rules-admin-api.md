# API quản trị luật phân loại của AI

Dành cho người dựng màn hình admin trên web. Backend chỉ giữ cổng và ghi nhật ký; luật nằm ở DB của AI Service và **thay đổi có hiệu lực ngay** (không cần build lại, không cần khởi động lại).

- Gốc: `/api/v1/admin/ai-rules`. Cần JWT của tài khoản **Admin** (`Authorization: Bearer ...`). Không đăng nhập: 401. Không phải admin: 403.
- Phản hồi thành công: `{"success": true, "data": ...}`. Phản hồi 204 không có thân.
- Phản hồi lỗi: `{"success": false, "error": {"code": "...", "message": "..."}}`; `message` là tiếng Việt, hiện thẳng cho admin được.
- Trường trong `data` và trong thân request giữ tên của AI Service: **snake_case** (`category_slug`, `is_active`...).
- Danh sách trả `{"items": [...], "total": n, "limit": n, "offset": n}`; tham số `limit` (1-200, mặc định 50) và `offset` (>= 0).
- Mỗi thay đổi thành công ghi: một dòng nhật ký admin chung (`GET /api/v1/admin/audit-logs?eventType=Admin.AiRule.Changed`) và một dòng ở AI Service có nội dung **trước/sau** (xem `GET /history`). Mọi thân ghi nhận thêm `reason` (tùy chọn, tối đa 500 ký tự), hiện trong lịch sử.

## Việc admin làm được

| Việc | Gọi |
|---|---|
| Biết giá trị hợp lệ để dựng ô chọn | `GET /meta` |
| Xem / tìm / thêm / sửa / tắt thương hiệu | `GET /brands`, `PUT /brands/{brandKey}`, `PATCH /brands/{brandKey}/active` |
| Xem / tìm / thêm / tắt từ khóa | `GET /keywords`, `POST /keywords`, `PATCH /keywords/{id}/active` |
| Ghi đè nhóm của một tên món cho mọi người dùng | `GET /item-cache`, `PUT /item-cache`, `DELETE /item-cache?name=` |
| Sửa chỉ dẫn phân loại món cho Gemini | `GET /ai-instructions`, `POST /ai-instructions`, `POST /ai-instructions/{id}/activate` |
| **Thử trước khi lưu** | `POST /preview` |
| Xem lịch sử, hoàn tác | `GET /history`, `POST /history/{id}/revert` |

## Chi tiết

### `GET /meta`
`{category_groups, backend_slugs, keyword_kinds, max_keyword_chars, max_instruction_chars}`. `category_groups` (9 nhóm: `dining, transport, shopping, groceries, housing, utilities, healthcare, entertainment, other`) là nhóm của **thương hiệu và từ khóa**; `backend_slugs` (10 nhóm, không có `income`) là nhóm của **bộ nhớ tên món**. `keyword_kinds` = `["merchant", "product"]`.

### Thương hiệu (`bill_brands`: tên cửa hàng → nhóm)
- `GET /brands?q=&category_slug=&active=` → mỗi dòng `{brand_key, category_slug, display_name, whole_word, is_active, source, updated_at}`. `source` là `seed` (có sẵn) hoặc `admin` (do admin thêm/sửa).
- `PUT /brands/{brandKey}` thân `{category_slug, display_name?, whole_word?, reason?}`: thêm mới hoặc sửa. `brandKey` được chuẩn hóa (bỏ dấu, chữ thường). Sửa **không** đổi trạng thái bật/tắt. `whole_word: true` = chỉ khớp nguyên từ.
- `PATCH /brands/{brandKey}/active` thân `{is_active, reason?}`. Không có xóa cứng: tắt là cách gỡ.

### Từ khóa (`bill_keywords`: từ trong tên cửa hàng hoặc tên món → nhóm)
- `GET /keywords?kind=&category_slug=&q=&active=` → `{id, kind, category_slug, keyword, category_priority, is_active, source}`.
- `POST /keywords` thân `{kind, category_slug, keyword, reason?}` → **201**, thêm trường `warnings: [{code, message, categories}]`. Từ khóa được chuẩn hóa (bỏ dấu, chữ thường). Trùng với từ khóa đang bật: 409 `duplicate`; trùng với từ khóa đã tắt: bật lại.
  - `warnings` **không chặn lưu**, chỉ để admin cân nhắc: `same_keyword_other_category` (đã có ở nhóm khác), `short_keyword` (dưới 4 ký tự, dễ khớp nhầm như "lau"/"lẩu").
- `PATCH /keywords/{id}/active` thân `{is_active, reason?}`.
- Từ khóa loại `product` cũng ảnh hưởng **chat và giọng nói** (bộ giải danh mục dùng chung với hóa đơn). Loại `chat` không sửa được ở đây.

### Bộ nhớ tên món (`bill_item_cache`)
Tên món → nhóm mà Gemini đã trả lời và được lưu dùng chung; **câu đầu tiên thắng và sai là sai cho mọi người**, nên đây là chỗ sửa lỗi quan trọng.
- `GET /item-cache?q=` → `{item_key, category_slug, hits, updated_at}` (nhiều lượt dùng lên trước).
- `PUT /item-cache` thân `{name, category_slug, reason?}` (`category_slug` thuộc `backend_slugs`). Tên được chuẩn hóa (bỏ số tiền, bỏ dấu). Tên giống mã hàng ("SP002993"), quá ngắn/dài: 422 `invalid_item`.
- `DELETE /item-cache?name=` → 204; không có: 404.

### Chỉ dẫn cho Gemini (`bill_ai_instructions`)
Phần luật phân nhóm món gửi cho Gemini. Phần nhiệm vụ, an toàn và schema kết quả do code giữ, nên sửa ở đây không làm lộ dữ liệu hay đổi định dạng trả về.
- `GET /ai-instructions` → các phiên bản, mới nhất trước; đúng **một** phiên bản `is_active`.
- `POST /ai-instructions` thân `{instruction_text, reason?}` → **201**, phiên bản mới được bật ngay, bản cũ giữ lại. Tối đa 20.000 ký tự, không để trống.
- `POST /ai-instructions/{id}/activate` bật lại một phiên bản cũ (cách "hoàn tác" của mục này).

### `POST /preview` (không ghi gì, không để lại nhật ký)
Thân: `{merchant_name?, lines?[], item_name?, proposed_brands?[], proposed_keywords?[]}`.
- `proposed_brands[]`: `{brand_key, category_slug, display_name?, whole_word?, active?}`; `proposed_keywords[]`: `{kind, category_slug, keyword, active?}`. `active: false` mô phỏng việc **tắt** luật.
- Trả `{current, proposed, changed}`. `current`/`proposed` = `{bill, item}`: `bill = {category_slug, local_category, source, confidence}` (hoặc `null` nếu không gửi cửa hàng/dòng chữ), `item = {rule_slug, cache_slug}` (hoặc `null`). `proposed` là `null` khi không có đề xuất.
- `bill` chỉ phản ánh **luật** (thương hiệu → từ khóa → bình chọn theo món); bộ nhớ riêng của từng người dùng và mô hình học (nếu bật) không nằm trong đó. `item.cache_slug` cho biết bộ nhớ tên món có đang đè luật không.
- Đề xuất sai (nhóm hoặc loại lạ, từ khóa trống): 422, không đụng gì.

### Lịch sử và hoàn tác
- `GET /history?resource=&resource_key=` (`resource` = `brand | keyword | item_cache | instruction`) → `{id, resource, resource_key, action, before, after, actor_hash, reason, created_at}`, mới nhất trước. `actor_hash` là SHA-256 id admin (AI Service không bao giờ thấy id thật; đối chiếu admin qua nhật ký admin chung).
- `POST /history/{id}/revert` thân `{reason?}` (tùy chọn) đưa dòng luật về trạng thái **trước** thay đổi đó, và ghi thêm một mục `revert`. Thương hiệu/từ khóa vừa tạo thì **bị tắt**, không bị xóa. Mục của chỉ dẫn Gemini: 422 `not_revertable` (dùng `activate`).

## Mã lỗi thường gặp
| HTTP | `error.code` | Khi nào |
|---|---|---|
| 404 | `not_found` | không có thương hiệu / từ khóa / tên món / chỉ dẫn / mục lịch sử đó |
| 409 | `duplicate` | từ khóa đã có và đang bật |
| 422 | `invalid_category`, `invalid_kind`, `blank_key`, `blank_keyword`, `keyword_too_long`, `keyword_without_letters`, `display_too_long`, `invalid_item`, `blank_instruction`, `instruction_too_long`, `not_revertable`, `VALIDATION` | dữ liệu gửi lên không hợp lệ (thông điệp nói rõ) |
| 503 | mã "AI Service không khả dụng" | AI Service tắt hoặc lỗi |

## Gợi ý luồng trên giao diện
Tìm luật hiện có → **Thử** (`/preview`) với câu/cửa hàng bị sai → xem `changed` → **Lưu** (kèm `reason`) → đọc `warnings` → nếu sai thì mở **Lịch sử** và **Hoàn tác**.

## Giới hạn đã biết
- Chỉ sửa được **dữ liệu** luật. Logic (đọc số tiền, ngày, chọn tên cửa hàng và tổng tiền trên ảnh OCR) và từ điển chat (`dictionaries/*.json`) nằm trong code.
- Thương hiệu thuộc nhóm ngoài 9 nhóm (Con Cưng → `family`, Fahasa → `education`) được ánh xạ trong code, không sửa được ở đây.
- Chưa có bước tự chạy bộ đo hồi quy (`scripts/eval_flows.py`) trước khi áp dụng; dùng `/preview` và chạy bộ đo bằng tay sau những thay đổi lớn.
- Cảnh báo xung đột chỉ nhận ra "cùng từ khóa ở nhóm khác" và "quá ngắn", chưa nhận ra từ khóa này là một phần của từ khóa dài hơn ở nhóm khác.
