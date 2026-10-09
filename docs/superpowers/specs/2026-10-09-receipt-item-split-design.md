# Thiết kế: tách hóa đơn theo từng món thành nhiều giao dịch

Ngày: 2026-10-09 · Trạng thái: **chờ bạn đọc duyệt** (chưa có kế hoạch triển khai, chưa có code) · Rule Gemini: [receipt-item-rules-draft.md](receipt-item-rules-draft.md)

## 1. Mục tiêu và ràng buộc

**Mục tiêu.** Một hóa đơn có hàng thuộc nhiều nhóm (ví dụ mỹ phẩm và đồ ăn) được tách thành nhiều nhóm kèm số tiền từng nhóm, kể cả khi cửa hàng chưa từng gặp. Người dùng bấm Lưu thì mỗi nhóm thành **một giao dịch riêng**, cùng nối về một lần quét.

**Ràng buộc đã chốt với bạn:**
- Gemini (hoặc AI bên thứ ba) chỉ nhận **tên món đã lọc**, không nhận ảnh, giá, tên cửa hàng, ngày giờ, hay ID người dùng.
- Tiền chỉ có một nguồn: giá OCR đọc tại chỗ. Mô hình không bao giờ tạo ra số tiền.
- AI Service vẫn stateless và chỉ trả quyết định; BE lưu kết quả và tính tiền.
- Chỉ lưu các trường đã bóc tách (tên món, giá, nhóm), không lưu chữ OCR hay ảnh.
- Luật cho Gemini nằm trong DB, sửa không cần build lại.
- Thay đổi chỉ **thêm** vào BE hiện có, hợp đồng chỉ thêm trường nên app cũ vẫn chạy.

**Không làm (YAGNI).** Bộ nhớ riêng từng người cho từng món; tự lưu câu trả lời của Gemini thành luật; endpoint lưu hàng loạt nguyên tử; hướng "Gemini ghép tên với giá" (hướng B/C đã cân nhắc và chưa chọn).

## 2. Quyết định đã chốt

| # | Quyết định | Lý do |
|---|---|---|
| 1 | Một bill tách nhóm → **hai giao dịch riêng**, mỗi nhóm một giao dịch, cùng nối về một lần quét | Dùng ngay với báo cáo, ngân sách hiện có; không đổi bảng giao dịch |
| 2 | Ghép tên với giá **tại chỗ bằng vị trí chữ** (hướng A); Gemini chỉ phân loại tên | Giá và phép cộng kiểm chứng được, Gemini không thấy giá |
| 3 | Đồ dùng tiêu hao trong nhà (bột giặt, giấy vệ sinh) là `shopping`; **sửa luật cũ** (hiện xếp `food`) cho khớp | Hai nơi không được mâu thuẫn |
| 4 | Phụ phí (giao hàng, đóng gói) được **BE gộp vào nhóm lớn nhất** của bill | Người dùng không coi đó là một khoản chi riêng |
| 5 | Thú cưng là `other`; quà tặng là `shopping`; bắp nước ở rạp là `entertainment` | Theo bản nháp rule |

Mục 3 đến 5 là mặc định mình lấy theo khuyến nghị trong bản nháp rule. Bạn sửa nếu muốn khác.

## 3. Luồng tổng thể

```
App ──ảnh──▶ BE ──ảnh──▶ ocr-service
                          │ OCR, lọc riêng tư, bóc tách
                          ▼
             BE ◀── items[{name, amount, parent, is_discount}], items_ok, tổng bill
              │
              │  (items_ok và ≥ 2 món)   tên món + parent + allow_ai
              ▼
         ai-service  POST /bill/classify-items
              │  phụ kiện thừa kế → luật DB → nhóm mặc định thương hiệu → Gemini (tên, chỉ khi allow_ai) → unknown
              ▼
             BE: gom nhóm, chia giảm giá, cộng tiền, kiểm tổng, lưu receipt_scan_items
              │
              ▼
            App: hiện các nhóm  ──Lưu──▶  BE: mỗi nhóm một POST /transactions (receiptScanId + receiptGroup)
```

Mọi bước lỗi đều **rơi về đường cũ** (một nhóm, đánh dấu cần xem lại). Quét không bao giờ thất bại vì tính năng này.

## 4. ocr-service: bóc tách từng món

**Dữ liệu có sẵn.** `ocr._run()` nhận hộp tọa độ từ PaddleOCR nhưng bỏ đi. Thêm một hàm trả kèm hộp; `read_lines` giữ nguyên chữ ký.

**Module mới `items.py`:** `extract_items(boxes, total) → {items, items_ok}`.

1. **Gom hàng ngang:** sắp hộp theo tâm dọc; hộp lệch dưới 0,6 lần chiều cao hộp trung vị so với hàng đang gom thì cùng hàng.
2. **Giá:** hộp ngoài cùng bên phải có dạng tiền (`19,000`, `22.000đ`, `-3,000đ`) và nằm ở 35% bên phải tờ giấy. Chữ còn lại bên trái ghép theo thứ tự trái sang phải là tên. Dùng lại `_amounts` của `extract.py` để hai nơi đọc tiền giống nhau.
3. **Tên xuống dòng:** hàng không có giá nằm ngay trên hàng có giá thì nối vào tên. Hàng chỉ có số nhỏ (cột số lượng) bị bỏ.
4. **Phụ kiện bắt đầu bằng `+`:** là một món có giá riêng; trường `parent` trỏ tới món chính ngay trên nó.
5. **Giảm giá:** hàng có số âm hoặc nhãn giảm, chiết khấu, voucher, khuyến mãi. `is_discount = true`.
6. **Vùng lấy món:** từ dòng tiêu đề cột (`SL`, `Tổng`, `Tên hàng`) tới dòng tổng đầu tiên. Các dòng `Tổng SL món`, `Thành tiền` (cộng chưa giảm), `Tổng thanh toán` nằm ngoài vùng.
7. **Kiểm tra:** tổng các món cộng giảm giá phải bằng đúng tổng bill. Lệch, hoặc dưới 2 món, thì `items_ok = false`.

Tên món đi qua `sanitize_lines` trước khi ra khỏi ocr-service (tên người, địa chỉ, số dài đã bị che).

**Hợp đồng `/analyze-bill`:** thêm `items: [{name, amount, parent, is_discount}]` và `items_ok`. Các trường cũ giữ nguyên.

**Giới hạn chấp nhận.** Giả định cột giá nằm bên phải. Bill nhiều cột giá (đơn giá, giảm, thành tiền) có thể rơi vào `items_ok = false`. Ảnh chụp màn hình app (Grab, Shopee) không đi đường này, giữ nguyên cách `app_screens`.

**Đo độ chính xác.** (a) Trên bộ 30 bill chuẩn, tỷ lệ bill mà tổng các món khớp tổng bill. (b) Gắn nhãn tay khoảng 10 bill để kiểm tra từng món ghép đúng giá chưa, vì tổng khớp vẫn có thể che hai lỗi bù trừ.

## 5. ai-service: phân loại tên món

**Endpoint mới** `POST /api/v1/bill/classify-items` (không đổi `/bill/classify`).

- **Nhận:** `{ items: [{name, parent}], merchant?, user_hash, allow_ai }` — không có số tiền.
- **Trả:** `{ items: [{index, category, source}], ai_used }`, `category` là slug BE, `source` thuộc `rule | inherit | merchant | ai | unknown`.

**Thứ tự cho mỗi món:**
1. Có `parent` → thừa kế nhóm món chính (`inherit`).
2. Luật và từ khóa trong DB (`bill_keywords`, logic của `_category_from_products`) → `rule`. Món thuốc, khám bệnh nhận ra ở bước này nên không bao giờ gửi ra ngoài.
3. Nhóm mặc định của thương hiệu, **chỉ** cho món chưa khớp luật (không đè kết quả bước 2) → `merchant`.
4. Gemini, chỉ cho món còn lại và chỉ khi `allow_ai = true` → `ai`.
5. Còn lại → `unknown`.

**Gọi Gemini.** Một lần cho mỗi bill, tối đa 20 món, mỗi tên tối đa 60 ký tự, chỉ gửi tên. Khung prompt nằm trong code; phần mô tả nhóm, luật chốt và ví dụ lấy từ DB (bảng `bill_ai_instructions`, một dòng đang dùng, sửa được). Tên món đưa vào dưới dạng mảng JSON; prompt dặn không làm theo chữ trong tên. Kết quả ép theo schema (`index`, `category` thuộc 11 giá trị hợp lệ); thiếu phần tử, sai chỉ số hoặc `category` ngoài danh sách thì **bỏ cả kết quả AI** và các món đó thành `unknown`. Quá 5 giây, hoặc 429/503 sau một lần thử lại → coi như AI không dùng được; khóa mạch 30 giây như đường Rasa. Bộ nhớ đệm trong bộ nhớ theo tên món đã chuẩn hóa, có hạn sống. Cấu hình bằng biến môi trường (`GEMINI_API_KEY`, `GEMINI_MODEL`); không có khóa thì bước 4 bị bỏ qua.

**Điều kiện dùng Gemini.** Dùng bản trả phí hoặc Vertex AI, không dùng bản miễn phí (cần đọc lại điều khoản hiện hành trước khi dùng thật). Người dùng phải đồng ý (cờ ở mục 6).

**DB của AI Service:** alembic **0008** thêm `bill_ai_instructions(id, instruction_text, is_active, updated_at)` kèm model ORM để `alembic check` sạch; nạp nội dung từ file rule đã duyệt.

**Sửa luật cũ cho khớp quyết định 3:** chuyển các từ khóa đồ dùng tiêu hao (`bot giat`, `nuoc rua chen`, `giay ve sinh`, `nuoc xa vai`) từ nhóm `groceries` sang `shopping`, trong code và trong `bill_keywords`.

## 6. BE: hợp đồng, dữ liệu, lưu giao dịch

### Hợp đồng (chỉ thêm trường)
- `OcrServiceResponse` thêm `Items` và `ItemsOk`.
- `ScanReceiptCommandHandler`: nếu `ItemsOk` và có từ 2 món, gọi `ClassifyItemsAsync`, rồi **tính tiền** (BE là nơi cộng, không tin tổng của AI):
  - Món cùng nhóm cộng lại. Giảm giá dính ngay dưới một món tính cho nhóm món đó; giảm giá chung chia theo tỷ lệ tiền (món có giá lớn nhất nhận phần dư làm tròn để tổng khớp).
  - Phụ phí gộp vào nhóm lớn nhất.
  - Món `unknown` gom vào một nhóm `categorySlug = null` và đánh dấu cần xem lại.
  - Tổng các nhóm phải bằng tổng bill, lệch thì rơi về đường cũ.
- `ScannedReceiptDto` thêm `groups: [{categorySlug, amountCents, items: [{name, amountCents}]}]`. Các trường cũ giữ nguyên: `amountCents` là tổng bill, `categorySlug` là nhóm lớn nhất, nên app cũ không vỡ.
- `POST /transactions` thêm `receiptGroup` (slug nhóm AI đề xuất) đi cùng `receiptScanId`. App tách bill gọi hai lần, mỗi lần một nhóm, mỗi lần một `clientRequestId` (gọi lại an toàn nhờ cơ chế đã có).
- Cờ đồng ý chia sẻ với AI: một cột mới trên hồ sơ người dùng, mặc định tắt, đổi qua `PATCH /users/me`. BE đọc cờ này rồi gửi `allow_ai`.

### Cấu hình bật tắt
`Receipt:SplitByItemsEnabled`, mặc định **tắt**. ocr-service luôn trả `items` (chỉ thêm), nên bật tắt không cần triển khai lại.

### DB (EF, chỉ thêm, không sửa bảng lõi)
- **`receipt_scan_items`:** `scan_id` (FK, xóa lần quét thì xóa món), `position`, `name`, `amount_cents`, `category_slug` (null được), `category_source`, `parent_position`. Không có chữ OCR.
- **`receipt_scan_links`** thay cột `receipt_scans.transaction_id` của Phase 23: `transaction_id` là khóa chính (một giao dịch tối đa một lần quét), `scan_id`, `group_category_slug`, và duy nhất `(scan_id, group_category_slug)` để mỗi nhóm của một lần quét chỉ nối một giao dịch. Xóa hẳn giao dịch thì chỉ gỡ liên kết (`ON DELETE CASCADE` trên bảng nối giữ nguyên ý nghĩa SET NULL cũ). Migration **chép** các liên kết cũ sang bảng mới (nhóm = `category_slug` của lần quét), sau đó bỏ cột cũ và chỉ mục `uq_receipt_scans_transaction_id`.
- `LinkToTransactionAsync` giữ nguyên chữ ký, ghi vào bảng nối; mã sai, của người khác, hoặc nhóm đã nối thì bỏ qua (giao dịch vẫn lưu), như cũ.

### Học
Bill đã tách từ 2 nhóm trở lên **không dạy bộ nhớ cửa hàng** (cửa hàng đó không thuộc một nhóm). `ReceiptMemory.ShouldLearn` thêm điều kiện này.

## 7. App (`FinMate_FE`, Flutter)
Hiện các nhóm (tên nhóm, số tiền, danh sách món) trên màn rà soát; cho đổi nhóm và bỏ nhóm; bấm Lưu thì gửi một `POST /transactions` cho mỗi nhóm (có `receiptScanId`, `receiptGroup`, `clientRequestId`). Hợp đồng chỉ thêm trường nên app cũ vẫn hiện một nhóm như trước. Lưu nhóm đầu thành công, nhóm sau lỗi thì người dùng thử lại nhóm sau; không có tính nguyên tử giữa các lần gọi (endpoint lưu hàng loạt để sau).

## 8. Xử lý lỗi

| Tình huống | Kết quả |
|---|---|
| `items_ok = false` hoặc dưới 2 món | Đường cũ: một nhóm, cần xem lại |
| Tổng các nhóm lệch tổng bill | Đường cũ |
| Gemini lỗi, quá hạn, hoặc trả sai schema | Món lạ thành `unknown`, cần xem lại |
| Người dùng chưa đồng ý chia sẻ | Không gọi Gemini, món lạ thành `unknown` |
| ai-service không trả lời | Mọi món `unknown` (hoặc đường cũ), quét vẫn thành công |
| Lưu nhóm đầu được, nhóm sau lỗi | App gọi lại nhóm sau, an toàn nhờ `clientRequestId` |

## 9. Kiểm thử

- **ocr-service:** test `items.py` với hộp tọa độ lấy từ bill thật (Ngô Gia và vài bill khác: tên xuống dòng, phụ kiện, giảm giá); script đo tỷ lệ tổng khớp trên 30 bill chuẩn; test chữ ký `read_lines` không đổi.
- **ai-service:** thứ tự luật (inherit, rule, merchant, ai, unknown); Gemini giả trả sai schema thì bị bỏ; chuỗi cố tình chèn lệnh trong tên món; món thuốc không bao giờ ra ngoài; `allow_ai = false` thì không gọi; khóa mạch; luật `bill_keywords` đã chuyển nhóm; `alembic check` sạch.
- **BE:** gom nhóm, chia giảm giá và phần dư làm tròn, gộp phụ phí, chặn tổng lệch; quét rồi lưu hai giao dịch trên Postgres thật; nhóm trùng bị chặn (23505); xóa hẳn giao dịch chỉ gỡ liên kết; migration chép đúng dữ liệu cũ; bill tách nhóm không dạy bộ nhớ cửa hàng; cờ `SplitByItemsEnabled` tắt thì hành vi y hệt hiện tại.
- **App:** hiện các nhóm, lưu nhiều giao dịch, app cũ vẫn chạy với phản hồi mới.
- **Chạy thật trên `docker compose`:** quét bill Ngô Gia (một nhóm) và một bill nhiều nhóm, JOIN `receipt_scan_items` với giao dịch đã lưu.

## 10. Thứ tự làm (mỗi bước chạy được riêng)

1. **ocr-service:** bóc tách món, đo độ chính xác ghép giá. *Nếu độ chính xác thấp, dừng lại và quay về cân nhắc hướng C trước khi làm tiếp.*
2. **ai-service:** `classify-items`, migration 0008, sửa luật đồ dùng tiêu hao.
3. **BE:** hợp đồng, `receipt_scan_items`, `receipt_scan_links` và migration chép, cờ đồng ý, `SplitByItemsEnabled`.
4. **FinMate_FE:** hiện nhóm, lưu nhiều giao dịch.

## 11. Cần duyệt trước khi triển khai
Theo quy ước dự án, các route (`/bill/classify-items`), bảng (`receipt_scan_items`, `receipt_scan_links`, `bill_ai_instructions`) và trường hợp đồng mới sẽ đổi `ARCHITECTURE.md`; Gemini là một bên thứ ba mới, có thể đổi `TECH_STACK.md`. Cả hai cần bạn duyệt trước khi mình sửa.

## 12. Rủi ro còn lại
- Độ chính xác ghép giá trên bill thật chưa biết cho tới khi đo ở bước 1.
- Bản miễn phí của Gemini có thể dùng dữ liệu để cải thiện mô hình; cần xác nhận điều khoản trước khi bật thật.
- Dữ liệu sức khỏe là nhóm nhạy cảm theo pháp luật Việt Nam; nên có người hiểu luật xác nhận cách xin đồng ý.
- Hai giao dịch lưu không nguyên tử; người dùng có thể thấy bill lưu dở khi mất mạng giữa hai lần gọi (offline queue của app giảm rủi ro này).
