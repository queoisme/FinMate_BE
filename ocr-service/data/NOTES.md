# Ghi chú về các bảng dữ liệu mặc định

JSON không có chú thích, nên lý do đứng sau từng mục (từ nhập nhằng đã loại, nguồn học, chuỗi đã đóng cửa...) được giữ ở đây. Sửa bảng thì cập nhật cả ghi chú. Nguồn sự thật khi chạy thật là DB (`kb_brands`, `kb_keywords`); các file này chỉ để khởi tạo, chạy test và dự phòng khi không có DB.

## Thương hiệu (`data/brands.json`)

Mỗi mục có `note` (chú thích đứng cạnh) và `group` (chú thích đầu nhóm: nguồn nhập, tên đã loại và lý do). Chú thích cấp bảng (từ brands.py cũ):

- Tên chuẩn (có dấu) để hiển thị và lưu cho từng thương hiệu ở trên.
- Chuỗi phổ biến thêm sau: khóa -> (danh mục, tên hiển thị). Khóa phải đặc trưng (>= 5 chữ, đã bỏ dấu) vì khớp theo chuỗi con.
- Bảng nền mở rộng: chuỗi quán ăn / siêu thị / thời trang / giày dép / trang sức / mỹ phẩm / điện máy... phổ biến ở Việt Nam.
- Quy tắc: khóa viết thường, bỏ dấu, đặc trưng (tránh từ thông dụng như "mango", "ananas" trùng "bananas", "phu quy"...).
- ponytail: danh sách viết tay, không đầy đủ; chuỗi thiếu thì người dùng Lưu một lần là app nhớ. Thêm chuỗi mới vào data/brands.json.
- Tên ngắn hoặc là từ thông dụng: chỉ khớp khi đứng thành cả từ (không khớp giữa chữ khác như "Nikel", "Junoon", "Havana Vans").
- Rút ra từ trường "khop_an_toan" của đợt nghiên cứu và test nhận nhầm.

## Từ khóa tên cửa hàng (`data/merchant_keywords.json`)

- nhóm `groceries`, đứng cạnh `"groceries": ("sieu thi", "mart", "coop", "bach hoa xanh", "winmart", "tap hoa", "thuc pham", "cho dau moi", "`: đại lý nước uống: bán nước đóng chai/bình, không phải cước nước sinh hoạt

## Từ khóa sản phẩm (`data/product_keywords.json`)

- nhóm `dining` (chú thích đứng riêng): Bỏ các từ mơ hồ khi mất dấu: "duong" (đường ăn/đường phố), "lau" (lẩu/lầu), "chao" (cháo/chào), "muoi" (muối/mười).
- nhóm `groceries` (chú thích đứng riêng): món quen thuộc ở quán ăn nhanh, quán cơm, quán phở
- nhóm `groceries` (chú thích đứng riêng): học từ 12 bill ảnh 2026-10-14: "răng" (nha khoa) khớp nhầm "bắp rang", chặn luôn AI cho cả bill
- nhóm `healthcare` (chú thích đứng riêng): hàng tiêu dùng, thực phẩm tươi/đóng gói thường gặp ở tạp hóa, đại lý
- nhóm `entertainment` (chú thích đứng riêng): vé và dịch vụ vui chơi
- nhóm `other` (chú thích đứng riêng): dịch vụ làm đẹp/cá nhân (không có nhóm riêng nên là "other"): spa, salon, cắt tóc, nail, mi, triệt lông
- nhóm `shopping` (chú thích đứng riêng): phiếu mua/bán vàng bạc thường không có logo: nhận qua tên hàng
- nhóm `housing` (chú thích đứng riêng): hóa đơn mỹ phẩm không logo (siêu thị/nhà thuốc nhiều dòng khác nên danh mục của họ vẫn thắng)
- nhóm `shopping`, đứng cạnh `"bot giat", "nuoc rua chen", "giay ve sinh", "nuoc xa vai"`: đồ dùng gia đình tiêu hao là shopping, không phải thực phẩm
- nhóm `shopping`, đứng cạnh `"khan uot", "khan giay", "kem danh rang", "ban chai danh rang", "dau goi", "sua tam", "sua tam trang"`: đồ vệ sinh cá nhân dùng một lần
- nhóm `housing` (chú thích đứng riêng): quần áo, giày dép, túi xách, đồ điện tử nhỏ
- nhóm `housing` (chú thích đứng riêng): văn phòng phẩm ("bìa nút" không phải bia, "thước kẻ" không phải thuốc) và đồ vệ sinh nam
- nhóm `utilities`, đứng cạnh `"goi data", "cuoc di dong", "cap quang")`: "tien dienthoai" (xem _category_from_products) không bị "tien dien" (nhà ở) khớp nhầm

## Từ khóa chỉ tin trong tên cửa hàng (`data/name_only_keywords.json`)

- Danh sách chuỗi ("pho", "nha tro", "phong tro", "chung cu"): bỏ dấu làm "phở" trùng "phố" (Thành phố...), và địa chỉ giao hàng cũng có thể in "nhà trọ", nên các từ này chỉ được tin khi nằm trong tên cửa hàng.
- Dùng chung với ai-service. **Cả hai dịch vụ cùng đọc các tệp thương hiệu/từ khóa trong thư mục này** (ai-service nhận bản chép vào image lúc build qua `additional_contexts` của compose, biến `LEXICON_DIR`; xem `ai-service/app/bill/lexicon.py`): sửa một chỗ, không còn bản chép trong code.

## Bảng từ vựng của lớp đọc hóa đơn (`data/parsing_vocab.json`)

- Khóa dạng `parsing.extract._RECEIPT_VOCAB` (mô-đun + tên biến cũ), mỗi mục có `type` (tuple, tuple2, set, list, dict), `doc` (chú thích gốc trong code, giữ nguyên lý do) và `value`. Nạp bằng `classification.data_files.table(khóa)` và trả đúng KIỂU cũ.
- Gồm nhãn người bán/người mua, từ khóa tiêu đề, mã nhãn trường bị OCR viết liền, dấu hiệu địa chỉ, từ chung của hóa đơn, bảng sửa ký tự OCR hay nhầm... trước đây viết cứng trong `parsing/` và `reading/`.
- **Thận trọng:** nhiều mục (đặc biệt biến thể chữ OCR đọc hỏng như `hoaddnso`) xuất phát từ một vài hóa đơn cụ thể; chúng chỉ nên được thêm khi là một LOẠI lỗi OCR lặp lại, không phải để cho qua một ảnh. `tests/test_no_hardcoded_tables.py` chặn việc đưa bảng trở lại vào `.py`.

## Ngưỡng số của lớp đọc hóa đơn (`data/thresholds.json`)

- Mỗi mục có `value`, `doc`, `samples` (số mẫu đã dùng để chỉnh; `null` = chưa ghi nguồn đo) và `source`. Nạp bằng `classification.data_files.threshold(khóa)`.
- **Ngưỡng chỉnh theo vài mẫu chưa đáng tin cho ảnh lạ.** Hiện có 6 ngưỡng ghi rõ số mẫu (2, 4, 4, 28, 9, 9) và 30 ngưỡng chưa ghi nguồn đo: đó là việc cần bù khi có ảnh thật mới. Đổi giá trị chỉ khi kiểm trên ảnh CHƯA dùng để chỉnh.
- Chưa chuyển các hằng số nằm bên trong hàm (ví dụ ngưỡng lệch 50 lần ở kiểm tra tổng, trọng số cộng điểm trong `receipt_score`): chúng vẫn nằm trong code và cũng chỉnh theo vài ảnh.

## Bộ ảnh giữ riêng (`benchmark/holdout/` + `benchmark/expected_holdout.json`)

- Chạy `python benchmark/run_holdout.py`. Chưa có ảnh nào: chạy sẽ báo "chưa có bộ giữ riêng" (mã 2), đúng sự thật. Cần tối thiểu 30 ảnh chụp mới mà CHƯA AI dùng để chỉnh luật mới kết luận được; quy tắc ở đầu `run_holdout.py` (ảnh dùng để sửa luật thì chuyển sang bộ chỉnh).
- Các ảnh hiện có đều coi là đã dùng để chỉnh: `expected_databill.json`, `expected_items.json`, `expected.json`, và 12 bill ngày 2026-10-14 (luật học từ chúng được ghi ở `merchant/product` keywords).

## Từ điển câu chat/giọng nói

Đã chuyển sang ai-service: `ai-service/dictionaries/*.json` là bản mặc định, bản chạy thật là tài liệu `chat_dictionary` trong `config_documents` (ghi chú ở `ai-service/dictionaries/NOTES.md`). OCR không dùng từ điển chat nên bản chép `data/chat_keywords.json` và các dòng `kind = 'chat'` trong `kb_keywords` đã bị gỡ (migration 0016).

## Nhóm danh mục (`data/category_groups.json`)

Một nguồn cho 9 nhóm hóa đơn (`groups`) và cách quy đổi sang slug backend (`backend_slug`). OCR (`extract.CATEGORIES`), bộ phân loại bill của ai-service, ràng buộc CHECK của DB (`BILL_CATEGORY_GROUPS`), bảng `SLUG_BY_LOCAL_CATEGORY` và schema Gemini (suy từ `CATEGORY_SLUGS`) đều đọc từ đây hoặc từ enum, không ai chép riêng.

- Đây KHÔNG phải tài liệu admin sửa được: tập nhóm gắn với ràng buộc CHECK trong DB, đổi nhóm cần migration mới (migration cũ giữ nguyên) và đổi cả bảng từ khóa. Slug backend (`CATEGORY_SLUGS`) là hợp đồng với CategorySeeder của backend, giữ trong code.
- `groceries` quy về `food` vì backend không có nhóm siêu thị riêng; muốn tách thì đổi tại đây và thêm slug phía backend.

## Từ vựng hóa đơn dùng chung (`data/shared_words.json`)

Một nguồn cho từ vựng mà nhiều dịch vụ cùng cần. Trước đây mỗi nơi chép riêng và chỉ có chú thích "đồng bộ với..." canh nhau (OCR `items.py`/`totals.py`/`extract.py`, ai-service `bill/classifier.py` + `category_resolver.py`, backend `ReceiptSplitter.cs`).

- Mỗi mục là một VAI TRÒ (`tax`, `service`, `pre_tax`, `cash_given`, `change`, `payment_tender`, `generic_store`...). Dịch vụ nào cần vai trò nào thì đọc vai trò đó; nơi ghép regex dùng `data_files.shared_alternation(...)` (OCR) hoặc `lexicon.shared_alternation(...)` (ai-service).
- Backend đọc cùng tệp như TÀI NGUYÊN NHÚNG (`FinMate.Application.csproj`, lớp `SharedWords`). Docker: `docker-compose.yml` cho backend build context `lexicon` = `ocr-service/data`, Dockerfile chép `shared_words.json` vào `/ocr-service/data` để đường dẫn tương đối của csproj đúng cả trong và ngoài Docker.
- Thêm một từ: viết thường, bỏ dấu (trừ `tax_accented`), vào đúng vai trò, rồi chạy test của cả ba dịch vụ (`ocr-service/tests/test_shared_words.py`, `ai-service/tests/unit/test_shared_words.py`, `ReceiptSplitterTests` của backend). Các test này cũng canh việc không ai viết lại các từ đó trong mã.
- Hợp nhất từ vựng làm tập từ của vài nơi rộng ra một chút (ví dụ hàng "chuyển khoản" giờ cũng bị coi là hàng thanh toán ở `items.py`; từ "gtgt" ở `totals.py` khớp cả khi dính chữ như ở `items.py`). Đã chạy lại 564 test OCR cũ và 64 ảnh hóa đơn, kết quả không đổi.
- Chưa hợp nhất: `_NEGATIVE_MARKERS`/`_TOTAL_MARKERS` của pipeline thông báo trong ai-service (chứa cả dạng có dấu lẫn không dấu và khớp trên chữ chưa bỏ dấu), và các regex của `app_screens.py`; đó là việc riêng.

## Chữ nhận dạng màn hình app (`data/app_screens.json`)

Chữ và hằng số gắn với GIAO DIỆN từng app mà `parsing/app_screens.py` dùng để nhận ra và đọc ảnh chụp màn hình (chuyến xe, MoMo, Grab/Xanh SM/ShopeeFood, vé xem phim, nhãn SPX, vé xe...): nhãn như "Nhà cung cấp", "Tóm tắt đơn hàng", tên cửa hàng cố định ("Chuyến xe công nghệ", "Vexere", "CGV"), danh mục, số dòng tìm số tiền, khoảng giá. Trước đây viết cứng trong 13 hàm.

- Admin sửa như các tài liệu cấu hình khác: tài liệu `app_screens` trong `config_documents` (nháp, kiểm tra, kích hoạt, hoàn tác). OCR tự nạp trong vòng ~30 giây hoặc ngay qua `POST /admin/reload-config`. Không có bản trong DB thì dùng tệp này.
- Bản nháp được kiểm tra cấu trúc: không xóa mục/khóa, không thêm khóa code không đọc, cùng kiểu giá trị, cụm chữ viết thường bỏ dấu, regex biên dịch được và có nhóm bắt số tiền khi cần, danh mục thuộc 9 nhóm, số dòng là số nguyên 1-30. Xem trước tác động: không có (cần ảnh chụp màn hình thật); đo bằng `benchmark/` sau khi kích hoạt.
- CHỈ chữ và hằng số nằm ở đây. Thứ tự ưu tiên giữa các loại màn hình và thuật toán đọc bố cục (cắt đoạn giữa hai nhãn, chọn số tiền cuối...) vẫn là code: app đổi BỐ CỤC (không chỉ đổi chữ) thì cần sửa code. Bảng từ khóa dài (`cinema_words`, `courier_shop`, `wallet_category`, `housing_words`, `shop_badge`) nằm ở `parsing_vocab.json`.
- Kiểm chứng khi tách: so bản cũ và bản mới trên 14 màn hình mẫu của test cùng 1634 biến thể (xóa/nhân đôi/đảo dòng, in hoa, bỏ dấu, cắt đầu/đuôi): 1630 giống hệt, 4 biến thể mà bản cũ tự sập bằng StopIteration (nhận ra GrabFood nhờ "với Grab" nhưng thiếu dòng mã GF) nay trả kết quả.
