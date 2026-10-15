# Thiết kế: tách hóa đơn theo từng món thành nhiều giao dịch

Ngày: 2026-10-09 · Trạng thái: **đã triển khai trên nhánh `feat/receipt-item-split`**, chờ merge và chờ duyệt Task 17 (`ARCHITECTURE.md`, `TECH_STACK.md`); cờ `Receipt:SplitByItemsEnabled` mặc định tắt · Rule Gemini: [receipt-item-rules-draft.md](receipt-item-rules-draft.md)

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
              │  (items_ok, ≥ 2 món chính thường, không phụ kiện mồ côi)   tên món + parent + allow_ai
              ▼
         ai-service  POST /bill/classify-items
              │  phụ kiện thừa kế → luật DB → nhóm mặc định thương hiệu → Gemini (tên, chỉ khi allow_ai) → unknown
              ▼
             BE: gom nhóm, chia giảm giá, cộng tiền, kiểm tổng, lưu receipt_scan_items
              │
              ▼
            App: hiện các nhóm  ──Lưu──▶  BE: mỗi nhóm một POST /transactions (receiptScanId + receiptGroup)
```

Mọi bước lỗi đều **rơi về đường cũ không đổi**: phân loại cả bill như trước khi có tính năng này, không có `groups`. Quét không thất bại vì tính năng này.

**Cổng vào** (kiểm tra TRƯỚC khi đọc cờ đồng ý trong DB và trước khi gọi ai-service): `items_ok`, có ít nhất 2 hàng **chính** thường (không phải giảm giá/phụ thu, `parent = null`) và không có phụ kiện mồ côi (parent trỏ vào dòng không phải món thường). Bill không qua cổng thì không gửi tên món đi đâu cả.

## 4. ocr-service: bóc tách từng món

**Dữ liệu có sẵn.** `ocr._run()` nhận hộp tọa độ từ PaddleOCR nhưng bỏ đi. Thêm một hàm trả kèm hộp; `read_lines` giữ nguyên chữ ký.

**Module mới `items.py`:** `extract_items(boxes, total) → {items, items_ok}`.

0. **Chỉnh nghiêng (`ocr._deskew`):** đo độ dốc trang bằng trung vị độ dốc cạnh trên của các quad PaddleOCR rộng hơn 80px, rồi đưa mỗi hộp về trang ngang (giữ x, tâm dọc thành `cy - s·cx`, chiều cao lấy theo cạnh quad). Ảnh chụp lệch không còn làm giá ở mép phải trôi sang hàng của món dưới; kiểm tra tổng không bắt được lỗi đó vì giá vẫn cộng đủ. Trang phẳng giữ nguyên tọa độ. **Chốt an toàn:** chỉ dùng độ dốc khi có ít nhất 3 quad rộng và |độ dốc| ≤ 0,2; không thì độ dốc = 0 (hộp bao thẳng trục như cũ), vì độ dốc sai có thể hoán giá giữa các hàng mà tổng vẫn khớp.
1. **Gom hàng ngang:** sắp hộp theo tâm dọc (đã chỉnh nghiêng); hộp lệch dưới 0,6 lần chiều cao hộp trung vị so với hàng đang gom thì cùng hàng.
2. **Giá:** hộp ngoài cùng bên phải có dạng tiền (`19,000`, `22.000đ`, `-3,000đ`) và nằm ở 35% bên phải tờ giấy. Chữ còn lại bên trái ghép theo thứ tự trái sang phải là tên. Dùng lại `_amounts` của `extract.py` để hai nơi đọc tiền giống nhau.
3. **Tên xuống dòng:** hàng không có giá nằm ngay trên hàng có giá thì nối vào tên. Hàng chỉ có số nhỏ (cột số lượng) bị bỏ.
4. **Phụ kiện bắt đầu bằng `+`:** là một món có giá riêng; trường `parent` trỏ tới món chính ngay trên nó.
5. **Giảm giá:** hàng có số âm hoặc nhãn giảm, chiết khấu, voucher, khuyến mãi. `is_discount = true`.
6. **Vùng lấy món:** bắt đầu sau dòng tiêu đề cột (`SL`, `Tổng`, `Tên hàng`...) gần nhất phía trên hàng món đầu tiên (hàng có ô tiền đúng dạng có dấu nghìn và không phải dòng tổng; mã KH, điểm, điện thoại ở đầu phiếu không tính). Danh sách món kết thúc ở **dòng cộng tạm** (`Cộng tiền hàng`, `Tạm tính`, `Tiền hàng`, `Tổng...`, `Thành tiền`, hoặc một số in trần đúng bằng tổng các món đang cộng). Sau đó tới **dòng tổng cuối** (`thanh toán`, `phải trả`, `phải thu`, `total`, hoặc `Tổng cộng` sau cộng tạm) chỉ nhận: thuế/VAT/phí phục vụ/phí dịch vụ/`service` ("Service charge") là **món phụ thu** (`parent = null`, `is_discount = false`, giữ nhãn; nhãn có "chưa" hoặc "trước" — "chưa thuế", "trước VAT" — là cộng tạm, không tính). BE (`ReceiptSplitter.IsSurcharge`) nhận đủ các từ này (và thêm phí giao hàng, phí đóng gói, túi nilon); hai danh sách ghi chú trỏ nhau và có test liệt kê từ của ocr-service. Số âm hoặc nhãn giảm là **giảm giá cả bill**; các hàng khác (tiền khách đưa, tiền thừa) bỏ. Trong bảng món, ô không phải tiền ở mép phải ("8%", ghi chú tay) bị bỏ qua khi tìm giá.
7. **Kiểm tra:** tổng các món + phụ thu - giảm giá phải bằng **đúng từng đồng** tổng bill. Lệch, hoặc dưới 2 món thật (không tính phụ thu và giảm giá), thì `items_ok = false`.

Tên món đi qua `sanitize_lines` trước khi ra khỏi ocr-service (tên người, địa chỉ, số dài đã bị che).

**Hợp đồng `/analyze-bill`:** thêm `items: [{name, amount, parent, is_discount}]` và `items_ok`. Các trường cũ giữ nguyên.

**Giới hạn chấp nhận.** Giả định cột giá nằm bên phải. Bill nhiều cột giá (đơn giá, giảm, thành tiền) có thể rơi vào `items_ok = false`. Ảnh chụp màn hình app (Grab, Shopee) không đi đường này, giữ nguyên cách `app_screens`.

**Đo độ chính xác.** (a) Trên bộ 30 bill chuẩn, tỷ lệ bill mà tổng các món khớp tổng bill. (b) Gắn nhãn tay khoảng 10 bill để kiểm tra từng món ghép đúng giá chưa, vì tổng khớp vẫn có thể che hai lỗi bù trừ.

## 5. ai-service: phân loại tên món

**Endpoint mới** `POST /api/v1/bill/classify-items` (không đổi `/bill/classify`).

- **Nhận:** `{ items: [{name, parent}], merchant_name?, user_id_hash, allow_ai }` — không có số tiền. Tối đa 60 món, tên tối đa 200 ký tự (quá thì 422). `merchant_name` là tên OCR thô (giống `/bill/classify`), không phải tên hiển thị.
- **Trả:** `{ items: [{index, category_slug, source}], ai_used }`, `category_slug` là slug BE, `source` thuộc `rule | inherit | merchant | ai | unknown`.

**Thứ tự cho mỗi món:**
1. Có `parent` → thừa kế nhóm món chính (`inherit`).
2. Luật và từ khóa trong DB (`bill_keywords`, logic của `_category_from_products`) → `rule`. Món thuốc, khám bệnh nhận ra ở bước này không gửi ra ngoài.
3. Nhóm mặc định của thương hiệu, **chỉ** cho món chưa khớp luật (không đè kết quả bước 2) → `merchant`.
4. Gemini, chỉ cho món chính còn lại và chỉ khi `allow_ai = true` → `ai`. **Hóa đơn thuốc không gửi:** bỏ qua cả bước này khi phân loại cả bill (tên cửa hàng + mọi tên món) ra `healthcare` (nguồn khác `none`) hoặc có bất kỳ món nào đã ra `health` ở bước 2-3; món lạ còn lại thành `unknown`. Đây là phát hiện bằng luật/từ khóa, không phải lời hứa tuyệt đối: thuốc nằm trên bill không nhận ra là thuốc vẫn có thể bị gửi tên (khi người dùng đã đồng ý).
5. Còn lại → `unknown`.

**Gọi Gemini.** Một lần cho mỗi bill, chỉ 20 món chính chưa khớp đầu tiên (món sau đó và mọi phụ kiện ở lại `unknown`/thừa kế), mỗi tên tối đa 60 ký tự sau khi **bỏ các cụm giống số tiền** (`45,000`, `12.000đ`, `vnd 1.250.000`; tên rỗng sau khi bỏ thì không gửi), chỉ gửi tên. Khung prompt nằm trong code; phần mô tả nhóm, luật chốt và ví dụ lấy từ DB (bảng `bill_ai_instructions`, một dòng đang dùng, sửa được). Tên món đưa vào dưới dạng mảng JSON; prompt dặn không làm theo chữ trong tên. Kết quả ép theo schema (`index`, `category` thuộc 11 giá trị hợp lệ); thiếu phần tử, sai chỉ số hoặc `category` ngoài danh sách thì **bỏ cả kết quả AI** và các món đó thành `unknown`. Quá 5 giây cho mỗi pha HTTP (kết nối, đọc...) cộng trần 8 giây cho cả lần gọi (gồm lần thử lại), hoặc 429/503 sau một lần thử lại → coi như AI không dùng được (log chỉ ghi mã lý do như `http_500`, `bad_count`, không ghi tên món); khóa mạch 30 giây như đường Rasa. Bộ nhớ đệm trong bộ nhớ theo tên món đã chuẩn hóa, có hạn sống. Cấu hình bằng biến môi trường (`GEMINI_API_KEY`, `GEMINI_MODEL`); không có khóa thì bước 4 bị bỏ qua.

**Điều kiện dùng Gemini.** Dùng bản trả phí hoặc Vertex AI, không dùng bản miễn phí (cần đọc lại điều khoản hiện hành trước khi dùng thật). Người dùng phải đồng ý (cờ ở mục 6).

**DB của AI Service:** alembic **0008** thêm `bill_ai_instructions(id, instruction_text, is_active, updated_at)` kèm model ORM để `alembic check` sạch; nạp nội dung từ file rule đã duyệt.

**Sửa luật cũ cho khớp quyết định 3:** chuyển các từ khóa đồ dùng tiêu hao (`bot giat`, `nuoc rua chen`, `giay ve sinh`, `nuoc xa vai`) từ nhóm `groceries` sang `shopping`, trong code và trong `bill_keywords`.

## 6. BE: hợp đồng, dữ liệu, lưu giao dịch

### Hợp đồng (chỉ thêm trường)
- `OcrServiceResponse` thêm `Items` và `ItemsOk`.
- `ScanReceiptCommandHandler`: nếu `ItemsOk` và có từ 2 món, gọi `ClassifyItemsAsync`, rồi **tính tiền** (BE là nơi cộng, không tin tổng của AI):
  - Món cùng nhóm cộng lại. Giảm giá dính ngay dưới một món tính cho nhóm món đó; giảm giá chung dồn vào nhóm lớn nhất (không chia theo tỷ lệ, tránh số lẻ; đổi 2026-10-15).
  - Phần dư làm tròn và phụ phí gộp vào nhóm lớn nhất **tính theo tiền gốc** (trước giảm giá; bằng nhau thì nhóm xuất hiện trước).
  - Chỉ tách khi có **từ 2 nhóm có danh mục**; khi đó món `unknown` gom vào một nhóm `categorySlug = null` (cần xem lại). Dưới 2 nhóm có danh mục thì đường cũ.
  - Tổng các nhóm phải bằng tổng bill, lệch (hoặc nhóm 0đ/âm) thì rơi về đường cũ.
- `ScannedReceiptDto` thêm `groups: [{categorySlug, amountCents, items: [{name, amountCents}]}]`. Các trường cũ giữ nguyên: `amountCents` là tổng bill, `categorySlug` là nhóm **có danh mục** lớn nhất (không bao giờ null khi tách được), nên app cũ không vỡ.
- `POST /transactions` thêm `receiptGroup` (slug nhóm AI đề xuất) đi cùng `receiptScanId`. App tách bill gọi mỗi nhóm một lần, mỗi nhóm một `clientRequestId` **cố định** (uuid v5 của `scanId:nhóm`, uuid v4 khi không có `scanId`), nên mở lại màn rà soát và lưu lại không tạo trùng (gọi lại an toàn nhờ cơ chế đã có).
- Cờ đồng ý chia sẻ với AI: một cột mới trên hồ sơ người dùng, mặc định tắt, đổi qua `PATCH /users/me`. BE đọc cờ này rồi gửi `allow_ai`.

### Cấu hình bật tắt
`Receipt:SplitByItemsEnabled`, mặc định **tắt**. ocr-service luôn trả `items` (chỉ thêm), nên bật tắt không cần triển khai lại.

### DB (EF, chỉ thêm, không sửa bảng lõi)
- **`receipt_scan_items`:** `scan_id` (FK, xóa lần quét thì xóa món), `position`, `name`, `amount_cents`, `category_slug` (null được), `category_source`, `parent_position`. Không có chữ OCR.
- **`receipt_scan_links`** thay cột `receipt_scans.transaction_id` của Phase 23: `transaction_id` là khóa chính (một giao dịch tối đa một lần quét), `scan_id`, `group_category_slug`, và duy nhất `(scan_id, group_category_slug)` để mỗi nhóm của một lần quét chỉ nối một giao dịch. Xóa hẳn giao dịch thì chỉ gỡ liên kết (`ON DELETE CASCADE` trên bảng nối giữ nguyên ý nghĩa SET NULL cũ). Migration **chép** các liên kết cũ sang bảng mới (nhóm = `category_slug` của lần quét), sau đó bỏ cột cũ và chỉ mục `uq_receipt_scans_transaction_id`.
- `LinkToTransactionAsync` thêm tham số `groupCategorySlug`, ghi vào bảng nối; mã sai, của người khác, hoặc nhóm đã nối thì bỏ qua (giao dịch vẫn lưu), như cũ. Khóa nối: lần quét **đã tách** (có từ 2 nhóm trong `receipt_scan_items`, hoặc `receipt_scans.category_source = "items"`) dùng `receiptGroup` đã làm sạch (rỗng, > 100 ký tự hay có ký tự điều khiển thì dùng slug danh mục đã lưu); lần quét **không tách** luôn nối dưới một khóa cố định = `category_slug` của lần quét (`unknown` khi null), bỏ qua `receiptGroup` và danh mục giao dịch, nên một lần quét không tách nối tối đa một giao dịch như cột cũ.

### Học
Bill đã tách từ 2 nhóm trở lên **không dạy bộ nhớ cửa hàng** (cửa hàng đó không thuộc một nhóm). `ReceiptMemory.ShouldLearn` thêm điều kiện này.

## 7. App (`FinMate_FE`, Flutter)
Hiện các nhóm (tên nhóm, số tiền, danh sách món) trên màn rà soát; cho đổi nhóm và bỏ nhóm; bấm Lưu thì gửi một `POST /transactions` cho mỗi nhóm (có `receiptScanId`, `receiptGroup`, `clientRequestId`). Hợp đồng chỉ thêm trường nên app cũ vẫn hiện một nhóm như trước. Lưu nhóm đầu thành công, nhóm sau lỗi thì người dùng thử lại nhóm sau; không có tính nguyên tử giữa các lần gọi (endpoint lưu hàng loạt để sau).

## 8. Xử lý lỗi

| Tình huống | Kết quả |
|---|---|
| `items_ok = false`, dưới 2 món chính thường, hoặc có phụ kiện mồ côi | Đường cũ không đổi (không gọi ai-service) |
| Tổng các nhóm lệch tổng bill, nhóm 0đ/âm | Đường cũ |
| Dưới 2 nhóm có danh mục | Đường cũ |
| Gemini lỗi, quá hạn, hoặc trả sai schema | Món lạ thành `unknown`; còn từ 2 nhóm có danh mục thì vẫn tách, không thì đường cũ |
| Người dùng chưa đồng ý chia sẻ, hoặc hóa đơn thuốc | Không gọi Gemini; như dòng trên |
| ai-service lỗi hoặc không trả lời | Đường cũ, quét vẫn thành công |
| Lưu nhóm đầu được, nhóm sau lỗi | App gọi lại nhóm sau, an toàn nhờ `clientRequestId` |

## 9. Kiểm thử

- **ocr-service:** test `items.py` với hộp tọa độ lấy từ bill thật (Ngô Gia và vài bill khác: tên xuống dòng, phụ kiện, giảm giá); script đo tỷ lệ tổng khớp trên 30 bill chuẩn; test chữ ký `read_lines` không đổi.
- **ai-service:** thứ tự luật (inherit, rule, merchant, ai, unknown); Gemini giả trả sai schema thì bị bỏ; chuỗi cố tình chèn lệnh trong tên món; hóa đơn thuốc (cả nhà thuốc không có trong bảng thương hiệu) không gọi Gemini; cụm số tiền bị bỏ khỏi tên; `allow_ai = false` thì không gọi; khóa mạch; luật `bill_keywords` đã chuyển nhóm; `alembic check` sạch.
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

### Kết quả đo (2026-10-09)

Chạy `ocr-service/benchmark/run_items.py` (đường đọc thật: `read_boxes` → `analyze` → `main._bill_items`, không dùng bộ nhớ cửa hàng) trên 30 ảnh chuẩn. Chạy hai lần, kết quả từng bill giống hệt.

- **Bill nhiều dòng** = ảnh giấy có từ 2 dòng hàng có giá in trên giấy, **đếm bằng mắt** (`expected_items.json`, `_item_lines`); bỏ 2 ảnh chụp màn hình (mục 4: không đi đường này). Được 24/30 bill.
- **(a) `items_ok` trên bill nhiều dòng: 6/24 (25%)**. Ngưỡng ≥ 50%: **KHÔNG ĐẠT**.
- **(b) món nhãn tay đúng (tên chứa đoạn nhãn + đúng số tiền) trên bill có nhãn và `items_ok`: 16/25 (64%)**. Ngưỡng ≥ 90%: **KHÔNG ĐẠT**. Chỉ so số tiền: 25/25 (100%). Cả 9 món trượt đều đúng giá; trượt vì OCR đọc sai chữ trong tên ("Sira chua" thay "Sữa chua", "lanh Bao" thay "Bánh Bao", "Kemqie"). Mẫu nhỏ: chỉ 4 trong 12 bill gắn nhãn có `items_ok`.
- Gắn nhãn tay 12 bill (có giảm giá: WinMart, AEON, Bắc Hiền; tên xuống dòng: Đức Thành, Dino, Bảo Nam, pizza, Long Châu). Bộ 30 ảnh không có bill nào có dòng phụ kiện "+" có giá riêng, nên trường hợp này chưa được đo.

**Bill trượt (18/24) và lý do** (đọc từ các hàng OCR của từng ảnh):
- **Số không phải tiền nằm ở cột giá trước dòng tiêu đề (8 bill)**: điểm tích lũy "2,770 ĐIỂM" (03d6, 28b8), số hóa đơn/phiếu/mã KH (8904, vinarice, 79d9), "Nợ: 1311 / Có: 5112" (9d28), số điện thoại (d2b8), ngày "19/09/2026" (33da). `_amounts` đọc chúng là tiền, nên "hàng có giá đầu tiên" đứng trước tiêu đề và cả bill bị bỏ (`start = None`).
- **Không có hoặc không đọc được dòng tiêu đề (4 bill)**: Bắc Hiền b4ea không in tiêu đề cột; Kisnac 21fd ("So luting", "Thanh ten"), Kisnac tối b8c8, Minh Phát 9a6d (ảnh mờ, giá đọc thành "690.00").
- **Giá in thấp hơn tên, rơi vào hàng của món sau (2 bill)**: WinMart 84e0, be5c (tên / đơn giá+SL / thành tiền trên ba dòng lệch nhau; be5c còn có cột KM âm nằm bên phải).
- **Tiêu đề và món đầu dính một hàng do ảnh nghiêng (1 bill)**: Chả cá f7f2, hàng "Trà đá (ca) | SL | Thành tiền" khớp `_END_RE` ("thanh tien") nên dừng ngay; các tên còn lệch một hàng so với giá.
- **Chữ viết tay ở cột ghi chú (1 bill)**: Dino 9309, "Bush" là hộp ngoài cùng bên phải nên món 1 mất giá.
- **Dòng tổng cùng hàng với dòng khuyến mãi cuối (1 bill)**: AEON b25b, "KHUYẾN MÃI @67.700-8.700 | 307.000" thành một món 307.000 (tên bị `sanitize_lines` coi là email, lọc hết).
- **Tổng bill khác tổng các món theo thiết kế (4 bill, không bao giờ `items_ok` với luật hiện tại)**: VAT cộng sau (pizza c55e, 9d28, d2b8), chiết khấu cả bill sau "Tổng tiền hàng" (b4ea). Trần thực tế của (a) là 20/24.

**Lỗi thấy ở `items.py` (chưa sửa, theo yêu cầu bước đo):**
- Tên xuống dòng: luật 3 nối hàng không giá vào món **bên dưới**, nhưng bill in bảng thường để giá ở dòng đầu và phần tên còn lại ở dòng dưới, nên đuôi tên dính vào món sau ("h&p 420g Banh Oreo", "Cut 480g... Banh Bao T.Heo2"). Giá vẫn đúng; chỉ tên bẩn.
- `_END_RE` thiếu "cộng tiền hàng": ảnh 9603 (1 món) ra thêm món "Cong tien hang 125.000".
- Số mã/điểm/điện thoại/ngày bị coi là giá (xem trên): đây là nhóm lỗi lớn nhất, nhiều khả năng sửa được bằng cách chỉ nhận tiền ở hàng **sau** tiêu đề thay vì đòi tiêu đề đứng trước mọi hàng có giá.

#### Sau khi sửa heuristic (bước 4b, 2026-10-09)

**Cảnh báo: mọi số đo dưới đây là trong mẫu (in-sample).** Các luật (và bước chỉnh nghiêng) được chỉnh khi nhìn chính 24 bill dùng để đo, nên con số lạc quan hơn trên bill mới. Mỗi luật là luật chung, có test riêng bằng hộp tổng hợp, không có luật theo tên ảnh. Cần một bộ bill để riêng (held-out) trước khi tin ngưỡng.

Luật đã đổi trong `items.py`:
- **Tiêu đề và hàng món đầu tiên:** "hàng món đầu tiên" chỉ tính ô tiền đúng dạng (có dấu nghìn: `66.000`, `1,920,500đ`) và hàng không chứa chữ "Tổng"/cộng/thanh toán; lấy dòng tiêu đề **gần nhất** phía trên nó. Điểm tích lũy, mã KH, Nợ/Có, điện thoại, "Tổng tiền các đơn" ở đầu phiếu không còn chặn mất bill. Không có ô tiền đúng dạng nào thì giữ luật cũ.
- **Ô không phải tiền ở mép phải trong bảng món** (thuế suất "8%", "KKKNT", chữ viết tay ở cột ghi chú) bị bỏ qua, giá là ô tiền gần nhất bên trái còn trong cột giá. Chỉ áp trong bảng món, không áp ở vùng tổng.
- **Dòng cộng tạm** (`Cộng tiền hàng`, `Tạm tính`, `Tiền hàng`, `Tổng...`, `Tổng cộng`, `Thành tiền`, chịu OCR đọc sai `C0ng`, `T6ng`, `T4m`) kết thúc danh sách món nhưng không kết thúc vùng bill. Một số in trần đúng bằng tổng các món đang cộng (nhãn rơi xuống hàng dưới) cũng là dòng cộng tạm.
- **Sau dòng cộng tạm, tới dòng tổng cuối** (`thanh toán`, `phải trả`, `phải thu`, `total`, hoặc `Tổng cộng` đứng sau cộng tạm): hàng có nhãn thuế/VAT/phí phục vụ/phí dịch vụ (trừ "chưa thuế", "trước thuế") là một món phụ thu `parent=None, is_discount=False`, giữ nguyên nhãn; hàng số âm hoặc nhãn giảm/chiết khấu/voucher/khuyến mãi là giảm giá cả bill; các hàng khác (tiền khách đưa, tiền thừa, cộng lặp lại) bị bỏ. Phụ thu và giảm giá không tính vào "ít nhất 2 món". Kiểm tra vẫn **khớp đúng từng đồng**.

Đổi trong `ocr.py` (vòng sửa 1 sau review): `_deskew` chỉnh nghiêng tọa độ hộp từ quad PaddleOCR trước khi gom hàng (mục 4 bước 0). `OcrBox` giờ mang tọa độ đã chỉnh nghiêng; chữ, thứ tự và độ tin cậy của `read_lines` không đổi.

Cách đo (b) đổi: giá phải đúng từng đồng, tên so **gần đúng** (`difflib.SequenceMatcher` ≥ 0,6 giữa đoạn nhãn đã bỏ dấu và một khúc cùng độ dài trong tên), vì OCR đọc sai chữ. In kèm cách đo chặt cũ và cách chỉ so tiền. Thêm nhãn tay (đọc từ ảnh) cho 5 bill mới ra `items_ok` (28b8, 8904, 9309, 9d28, d2b8) để (b) đo được cả chúng; tổng 17 bill có nhãn.

Ba lần đo, cùng 30 ảnh, cùng cách đo mới:

| chỉ số | trước 4b | sau luật heuristic, chưa chỉnh nghiêng | sau chỉnh nghiêng (`ocr._deskew`) | ngưỡng |
|---|---|---|---|---|
| (a) `items_ok` trên bill nhiều dòng | 6/24 (25%) | 12/24 (50%), **trung thực 11/24 (46%)** vì 8904 ghép sai | **14/24 (58%)** | ≥ 50% |
| (b) món nhãn tay đúng, tên gần đúng, 17 bill có nhãn | 25/25 (100%) | 49/54 (91%) | **56/58 (97%)** | ≥ 90% |
| (b) chỉ trên 12 bill gắn nhãn ở bước 4 | 25/25 | 33/34 (97%) | 37/38 (97%) | |
| tên chứa nguyên đoạn nhãn (cách đo bước 4) | 16/25 (64%) | 33/54 (61%) | 38/58 (66%) | |
| chỉ so số tiền | 25/25 | 54/54 (100%) | 58/58 (100%) | |

Số "trước 4b" của (b) tính trên hộp OCR đã lưu bằng `items.py` cũ; hai cột sau chạy thật bằng `run_items.py` (PaddleOCR).

**Kết luận ngưỡng chỉ áp cho lần đo sau chỉnh nghiêng:** (a) 14/24 và (b) 56/58 đạt ngưỡng, **trong mẫu**. Lần đo trước chỉnh nghiêng **không đạt**: 12/24 có tính 8904, một bill `items_ok = True` mà 3/4 tên gắn sai giá (ảnh nghiêng 1,56 hàng, giá trôi sang hàng món dưới, tổng vẫn khớp). Kiểm tra tổng không phát hiện được kiểu lỗi này (có test `test_names_one_row_above_prices_still_balance_sum_check_cannot_catch_it` ghi nhận), nên chặn ở bước đọc: `ocr._deskew` đưa hộp về trang ngang trước khi gom hàng. Sau chỉnh nghiêng 8904 ghép đúng 4/4; WinMart 84e0 (đã đối chiếu ảnh: 3/3 đúng) và Chả cá f7f2 (4/4) ra thêm `items_ok`; không bill nào đang đúng bị hỏng.

Rủi ro còn lại: một độ dốc cho cả trang, giấy cong hoặc nhăn mạnh vẫn có thể lệch hàng mà tổng vẫn khớp. Chưa thêm chốt chặn "độ dốc các quad lệch nhau nhiều thì ok=false" (chưa đo).

**Bill còn trượt (10/24) và lý do:**
- Không có tiêu đề cột: Bắc Hiền b4ea, Coopmart 79d9.
- Tiêu đề / chữ đọc hỏng hoặc ảnh mờ: Kisnac 21fd, b8c8, Minh Phát 9a6d.
- Cột thành tiền nằm giữa bảng rộng, bên phải còn nhiều cột trống nên ngoài 35% bên phải: 33da.
- WinMart be5c: thiếu món "cà chua" 12.000 (giá lệch hàng / cột KM âm).
- Pizza c55e: 4 món đúng, nhưng vùng tổng in số cao hơn nhãn của nó nên dòng "Thuế GTGT 34.800" không được nhận là phụ thu (bố cục, không phải nghiêng).
- AEON b25b: dòng tổng dính dòng "KHUYẾN MÃI@..." thành một món 307.000 (tên bị `sanitize_lines` coi là email); ghi riêng, không sửa ở bước này.
- 9d28: chữ "Cộng" đứng một mình (không phải "Cộng tiền hàng") nên 353.364 thành một món, và một tên bị `sanitize_lines` lọc hết; `items_ok = false`.

Chưa sửa (ngoài phạm vi 4b): tên xuống dòng dính vào món sau (luật 3), lọc email ăn chữ "KHUYENMAI@".
