# API quản trị tài liệu cấu hình của AI/OCR

Dành cho người dựng màn hình admin trên web. Đây là chỗ admin **sửa từ vựng, từ điển, ngưỡng và chữ nhận dạng màn hình app mà không cần sửa code hay build lại**. Backend chỉ giữ cổng và ghi nhật ký; nội dung từng phiên bản nằm ở bảng `config_documents` của AI Service.

- Gốc: `/api/v1/admin/ai-config`. Cần JWT của tài khoản **Admin**. Không đăng nhập: 401. Không phải admin: 403.
- Phản hồi thành công: `{"success": true, "data": ...}`. Phản hồi lỗi: `{"success": false, "error": {"code": "...", "message": "..."}}`; `message` là tiếng Việt, hiện thẳng cho admin được.
- Trường trong `data` và thân request giữ tên của AI Service: **snake_case**.
- Mỗi lần **lưu nháp** hoặc **kích hoạt** thành công ghi một dòng nhật ký admin chung (`GET /api/v1/admin/audit-logs?eventType=Admin.AiConfig.Changed`): ai, tài liệu nào, `draft`/`activate`, phiên bản, ghi chú, có `force` không. Đọc và xem trước không để lại nhật ký.
- Mỗi lần lưu là một **dòng mới** (phiên bản mới); không bao giờ sửa dòng cũ. Mỗi tài liệu có đúng một bản `active`. **Hoàn tác = kích hoạt lại một phiên bản cũ.**

## Các tài liệu sửa được

| `name` | Là gì | Dịch vụ dùng | Xem trước | Hiệu lực sau khi kích hoạt |
|---|---|---|---|---|
| `intent_cues` | Từ vựng, mẫu nhận dạng, ngưỡng và câu trả lời của lớp hiểu câu chat | ai-service | Có (bộ câu có đáp án) | Ngay |
| `chat_dictionary` | Từ khóa phân loại danh mục cho câu chat/giọng nói (17 nhóm, nhãn hiển thị) | ai-service | Có | Ngay |
| `merchant_lexicon` | Từ điển tên cửa hàng (thông báo ngân hàng và bộ phân loại chung) | ai-service | Có | Ngay |
| `parsing_vocab` | Từ vựng đọc hóa đơn giấy của OCR | ocr-service | Không | Khoảng 30 giây |
| `thresholds` | Ngưỡng đọc hóa đơn của OCR (mỗi ngưỡng ghi số mẫu và nguồn đo) | ocr-service | Không | Khoảng 30 giây |
| `app_screens` | Chữ và hằng số nhận dạng màn hình app (MoMo, Grab, Xanh SM, vé xe, vé phim...) | ocr-service | Không | Khoảng 30 giây |

Chưa có bản nào trong DB thì dịch vụ dùng **bản mặc định trong image** (`version` = 0, `source` = `file`). Danh sách luôn cho biết `active_version`, `source`, số phiên bản đã lưu và `preview_available`.

## Gọi gì

| Việc | Gọi |
|---|---|
| Liệt kê tài liệu | `GET /documents` |
| Tải bản đang chạy để sửa (kèm `version`) | `GET /documents/{name}` |
| Xem bản mặc định (để so sánh hoặc đưa về nguyên trạng) | `GET /documents/{name}/default` |
| Lịch sử phiên bản (mới nhất trước) | `GET /documents/{name}/versions?limit=&offset=` |
| Xem nội dung một phiên bản | `GET /documents/{name}/versions/{version}` |
| Lưu bản nháp | `POST /documents/{name}/drafts` → **201** |
| Xem trước tác động | `POST /documents/{name}/versions/{version}/preview` (là POST nhưng không ghi gì) |
| Kích hoạt / hoàn tác | `POST /documents/{name}/versions/{version}/activate` |

## Luồng sửa

1. `GET /documents/chat_dictionary` → lấy `data.content` (toàn bộ tài liệu) và `data.version` (ví dụ 0).
2. Sửa nội dung, rồi `POST /documents/chat_dictionary/drafts` thân `{"content": {...toàn bộ...}, "note": "vì sao đổi (tối thiểu 3 ký tự)"}` → trả `{version, status: "draft", diff_vs_active, validation: "ok"}`. Bản nháp **chưa ảnh hưởng gì**.
3. `POST .../versions/{version}/preview` → `diff_vs_active` và `preview`. Với tài liệu có cổng đo: `{available, cases, correct_before, correct_after, delta, changed[], keyword_changes}`. `changed` là các câu đổi kết quả; `keyword_changes` là từ thêm/bớt theo nhóm. Tài liệu của ocr-service trả `preview.available = false`.
4. `POST .../versions/{version}/activate` thân `{"expected_active_version": 0, "force": false}`. `expected_active_version` **phải là số `version` đã tải ở bước 1** (0 = bản mặc định).
5. Hoàn tác: kích hoạt lại phiên bản cũ (với `expected_active_version` = bản đang chạy hiện tại). Muốn về đúng bản ban đầu: tải `GET .../default`, lưu thành nháp rồi kích hoạt.

## Lỗi hay gặp (giữ mã HTTP của AI Service)

| HTTP | `error.code` | Nghĩa |
|---|---|---|
| 404 | `not_found` | Không có tài liệu hoặc phiên bản đó |
| 409 | `stale_active` | Người khác vừa đổi bản đang chạy: tải lại rồi xem xét lại, không ghi đè |
| 409 | `already_active` | Phiên bản đó đang chạy rồi |
| 422 | `preview_regression` | Kích hoạt làm tụt độ chính xác trên bộ câu có đáp án; xem `/preview`, nếu vẫn muốn thì gửi `force: true` |
| 422 | `missing_keys`, `unknown_key`, `unknown_group` | Xóa mục/khóa mà code đang dùng, hoặc thêm mục code không đọc (thêm mục mới cần sửa code) |
| 422 | `type_changed`, `invalid_value`, `invalid_keywords` | Sai kiểu hoặc giá trị |
| 422 | `invalid_keyword`, `invalid_phrase` | Chữ phải **viết thường, bỏ dấu** (các dịch vụ so khớp trên chữ đã bỏ dấu nên chữ có dấu không bao giờ khớp) |
| 422 | `duplicate_keyword` | Một từ chỉ được thuộc một nhóm (kể cả giữa `keywords` và `weak_keywords`) |
| 422 | `invalid_regex`, `invalid_document`, `invalid_threshold` | Regex không biên dịch được, tham chiếu `<<tên>>` sai, ngưỡng ngoài khoảng cho phép |
| 422 | `invalid_category` | Danh mục phải thuộc 9 nhóm hóa đơn |
| 422 | `note_required` | Cần ghi lý do đổi |
| 503 | | AI Service không phản hồi |

## Lưu ý khi dựng giao diện

- Mỗi nhóm của `chat_dictionary` có `kind`, `label`, `keywords` và tùy chọn `weak_keywords`: từ khóa yếu (vd. "cho con", "cho me") vẫn là từ khóa của nhóm nhưng chỉ quyết danh mục khi câu không có từ khóa thường nào khớp. Chuyển một từ giữa hai danh sách không hiện là thêm/bớt trong `keyword_changes`.
- Gửi lên **toàn bộ tài liệu** sau khi sửa, không gửi phần thay đổi. Hiển thị `diff_vs_active` của bản nháp cho người duyệt thấy mình đổi gì.
- Hiện `message` của lỗi nguyên văn; đừng tự dịch mã lỗi.
- `thresholds` ảnh hưởng độ chính xác OCR trên mọi người dùng và **không có xem trước**: mỗi ngưỡng phải ghi `samples` (số mẫu đã đo) và `source`. Nên có bước xác nhận riêng và nhắc đo lại trên ảnh thật sau khi kích hoạt.
- Với tài liệu của ocr-service, sau khi kích hoạt hãy báo "áp dụng trong khoảng 30 giây" thay vì "đã áp dụng".
- Luật thương hiệu/từ khóa hóa đơn theo từng dòng vẫn ở `docs/ai-rules-admin-api.md`; đây là hai cơ chế riêng.
