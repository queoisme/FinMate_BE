# Đồng bộ danh mục từ backend sang AI Service

AI Service giữ một bản sao danh mục hệ thống của backend trong bảng `kb_categories`. Backend là nguồn sự thật; AI Service chỉ nhận. Khi backend thêm, đổi tên hoặc tắt một danh mục hệ thống, nó gọi endpoint dưới đây để AI Service dùng được danh mục đó ngay (phản hồi sửa danh mục, bộ nhớ cửa hàng, bộ nhớ tên món, nhật ký phân tích), **không cần migration và không cần deploy lại**.

**Trạng thái:** cả hai phía đã xong. Backend gọi endpoint này (`AiCategorySyncService`, `backend/FinMate.Application/Admin/Commands/`) ngay sau khi admin tạo/sửa/tắt danh mục hệ thống (thất bại chỉ ghi log, không làm hỏng thao tác của admin), một lần lúc khởi động và lại mỗi giờ (`AiCategorySyncJob`, Hangfire, tự thử lại khi AI Service chưa lên). Bỏ qua danh mục `loan` và slug không đúng `^[a-z_]{1,32}$` (ghi cảnh báo). Tắt hẳn bằng `AI_CATEGORY_SYNC_ENABLED=false`. Chưa kiểm chứng với AI Service thật (chỉ có unit test phía backend; test tích hợp cần Docker).

## Endpoint

`PUT /api/v1/admin/categories/sync`

Tiêu đề:
- khóa nội bộ như mọi lời gọi backend → AI Service;
- `X-Actor-Hash`: SHA-256 (64 ký tự hex) id admin thực hiện; lời gọi lúc khởi động dùng một mã băm cố định của tiến trình hệ thống. Được ghi vào `kb_audit_log` (`resource = 'category'`).

Thân:

```json
{
  "categories": [
    {"slug": "pets", "label": "Thú cưng", "kind": "expense", "is_active": true},
    {"slug": "bonus", "label": "Thưởng", "kind": "income", "is_active": true}
  ],
  "taxonomy_version": 2,
  "reason": "admin thêm danh mục Thú cưng"
}
```

- `slug`: chỉ `a-z` và dấu gạch dưới, tối đa 32 ký tự; không đổi sau khi tạo.
- `kind`: `expense` (khoản chi) hoặc `income` (thu nhập). Bảng chỉ nhận khoản chi (bộ nhớ tên món, bộ nhớ cửa hàng, phiếu theo món, nhãn cuối của bill) từ chối `income`.
- `is_active`: tắt một danh mục bằng `false`; KHÔNG BAO GIỜ xóa. Danh mục bị tắt không còn được nhận cho dữ liệu mới, dữ liệu cũ giữ nguyên.
- `taxonomy_version`, `reason`: tùy chọn.

Phản hồi 200: `{"created": 1, "updated": 0, "unchanged": 1}`.

Quy tắc:
- Danh sách chỉ CỘNG THÊM: danh mục không có trong danh sách giữ nguyên (không bị tắt).
- Gửi lại y nguyên là an toàn (idempotent): chỉ làm mới `synced_at`.
- **409** (`category_conflict`): slug trùng một nhóm cũ của bộ luật bill (`dining`, `groceries`, `utilities`, `healthcare`), hoặc muốn đổi `kind` của danh mục đã có (các bảng đã ghi dữ liệu theo loại cũ).
- **422**: slug sai định dạng, trùng trong danh sách, `kind` không hợp lệ, danh sách rỗng hoặc dài hơn 200, thiếu `X-Actor-Hash`.

`GET /api/v1/admin/categories` trả danh mục AI Service đang biết (gồm cả dòng nhóm cũ `kind = legacy`) để backend đối chiếu.

## Hiệu lực

- Bản AI Service nhận lời gọi nạp lại ngay. Các bản khác nạp lại tự động mỗi 60 giây (`category_registry.REFRESH_SECONDS`).
- **Mô hình và Gemini KHÔNG tự đoán danh mục mới.** Bộ phân loại chỉ biết các lớp nó đã học, và prompt chỉ liệt kê danh mục trong code (`CATEGORY_SLUGS`). Danh mục mới dùng được cho thứ người dùng CHỌN; muốn máy tự đoán nó thì phải huấn luyện lại và thêm vào `CATEGORY_SLUGS`/`LABELS`.
- Danh mục tự tạo của từng người dùng (slug tùy ý) vẫn KHÔNG đồng bộ sang đây: phản hồi mang slug đó vẫn được bỏ về rỗng như trước.
