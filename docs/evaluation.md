# Đánh giá luồng chat và phân loại món

Bộ kiểm thử cố định: `ai-service/tests/fixtures/eval/{chat_cases,item_cases}.jsonl` (63 câu chat, 72 món). Chạy: `python scripts/eval_flows.py`.
Chỉ đo lớp luật/từ điển (không Rasa, không DB, Gemini luôn trả "unknown"). Nhãn kỳ vọng viết trước khi sửa luật; không được sửa nhãn để qua.

## Baseline 2026-10-12 (trước khi sửa)

```
== chat: 50/63 đúng, accuracy 0.794, bỏ qua 0.048, sai mà tự tin 11
   nhầm needs_amount -> out_of_scope: 3
   nhầm [('expense', 3000000, 'education')] -> [('expense', 3000000, 'other')]: 1
   nhầm [('expense', 10000, 'food')] -> [('expense', 10000, None)]: 1
   nhầm [('expense', 20000, 'food'), ('expense', 10000, 'food'), ('expense', 50000, 'transport')] -> [('expense', 50000, 'transport')]: 1
   nhầm [('expense', 20000, 'food'), ('expense', 10000, 'food')] -> [('expense', 10000, 'food')]: 1
   nhầm [('expense', 45000, 'food'), ('expense', 30000, 'food')] -> [('expense', 30000, 'food')]: 1
   nhầm [('income', 12000000, 'income'), ('expense', 20000, 'food')] -> [('income', 20000, 'income')]: 1
   nhầm [('expense', 50000, 'transport'), ('expense', 40000, 'food')] -> [('expense', 40000, 'food')]: 1
   - 'đóng học phí 3 triệu': muốn [('expense', 3000000, 'education')], ra [('expense', 3000000, 'other')]
   - 'uống nước 10k': muốn [('expense', 10000, 'food')], ra [('expense', 10000, None)]
   - 'ăn cơm 20k, uống nước 10k, đổ xăng 50k': muốn [('expense', 20000, 'food'), ('expense', 10000, 'food'), ('expense', 50000, 'transport')], ra [('expense', 50000, 'transport')]
   - 'ăn cơm 20k, uống nước 10k': muốn [('expense', 20000, 'food'), ('expense', 10000, 'food')], ra [('expense', 10000, 'food')]
   - 'ăn phở 45k và cà phê 30k': muốn [('expense', 45000, 'food'), ('expense', 30000, 'food')], ra [('expense', 30000, 'food')]
   - 'nhận lương 12 triệu, ăn cơm 20k': muốn [('income', 12000000, 'income'), ('expense', 20000, 'food')], ra [('income', 20000, 'income')]
   - 'đi grab 50k rồi ăn trưa 40k': muốn [('expense', 50000, 'transport'), ('expense', 40000, 'food')], ra [('expense', 40000, 'food')]
   - 'mua 2 lít sữa 50k': muốn [('expense', 50000, 'food')], ra [('expense', 50000, None)]
   - 'mua đồ 1,5tr': muốn [('expense', 1500000, None)], ra [('expense', 1500000, 'shopping')]
   - 'mua sữa bột 400k': muốn [('expense', 400000, 'family')], ra [('expense', 400000, 'food')]
   - 'ăn cơm': muốn needs_amount, ra out_of_scope
   - 'mua nước': muốn needs_amount, ra out_of_scope
   - 'đi ăn': muốn needs_amount, ra out_of_scope
== items: 51/72 đúng, accuracy 0.708, bỏ qua 0.333, sai mà tự tin 2
   nhầm food -> unknown: 9
   nhầm shopping -> unknown: 2
   nhầm health -> unknown: 2
   nhầm family -> food: 2
   nhầm family -> unknown: 2
   nhầm education -> unknown: 2
   nhầm other -> unknown: 2
   - 'Sting dau 330ml @ None': muốn food, ra unknown
   - 'Oreo vani @ None': muốn food, ra unknown
   - 'Sua chua Vinamilk @ None': muốn food, ra unknown
   - 'Pepsi lon @ None': muốn food, ra unknown
   - 'Tra bi dao Ngo Gia @ None': muốn food, ra unknown
   - 'Thit ba chi 500g @ None': muốn food, ra unknown
   - 'Keo cao su @ None': muốn food, ra unknown
   - 'Onigiri ca hoi @ None': muốn food, ra unknown
   - 'Banh bao @ None': muốn food, ra unknown
   - 'Pin AA Panasonic @ None': muốn shopping, ra unknown
   - 'But bi Thien Long @ None': muốn shopping, ra unknown
   - 'Nuoc muoi sinh ly @ None': muốn health, ra unknown
   - 'Berberin @ None': muốn health, ra unknown
   - 'Ta quan Bobby L @ None': muốn family, ra food
   - 'Sua bot Enfa A+ @ None': muốn family, ra food
   - 'Do choi tre em @ None': muốn family, ra unknown
   - 'Binh sua em be @ None': muốn family, ra unknown
   - 'Sach luyen thi IELTS @ None': muốn education, ra unknown
   - 'Hoc phi @ None': muốn education, ra unknown
   - 'Phi giao hang @ None': muốn other, ra unknown
   - 'Giat ui @ None': muốn other, ra unknown
```

Nhận xét: (1) cả 5 câu nhiều khoản chỉ ra một khoản, đúng như phân tích; (2) "ăn cơm", "mua nước", "đi ăn" ra `out_of_scope` khi không có Rasa
(trả `needs_amount` hiện chỉ có khi Rasa nhận ra ý định); (3) `family` (tã, sữa bột) chưa có trong luật món nên ra `food` hoặc unknown;
(4) 9 món ăn uống phổ biến (Sting, Pepsi, Oreo...) chưa có trong luật món.

## Sau khi sửa 2026-10-12

```
== chat: 62/63 đúng, accuracy 0.984, bỏ qua 0.016, sai mà tự tin 1
   nhầm [('expense', 1500000, None)] -> [('expense', 1500000, 'shopping')]: 1
   - 'mua đồ 1,5tr': muốn [('expense', 1500000, None)], ra [('expense', 1500000, 'shopping')]
== items: 52/72 đúng, accuracy 0.722, bỏ qua 0.319, sai mà tự tin 2
   nhầm food -> unknown: 8
   nhầm shopping -> unknown: 2
   nhầm health -> unknown: 2
   nhầm family -> food: 2
   nhầm family -> unknown: 2
   nhầm education -> unknown: 2
   nhầm other -> unknown: 2
   - 'Sting dau 330ml @ None': muốn food, ra unknown
   - 'Oreo vani @ None': muốn food, ra unknown
   - 'Sua chua Vinamilk @ None': muốn food, ra unknown
   - 'Pepsi lon @ None': muốn food, ra unknown
   - 'Tra bi dao Ngo Gia @ None': muốn food, ra unknown
   - 'Keo cao su @ None': muốn food, ra unknown
   - 'Onigiri ca hoi @ None': muốn food, ra unknown
   - 'Banh bao @ None': muốn food, ra unknown
   - 'Pin AA Panasonic @ None': muốn shopping, ra unknown
   - 'But bi Thien Long @ None': muốn shopping, ra unknown
   - 'Nuoc muoi sinh ly @ None': muốn health, ra unknown
   - 'Berberin @ None': muốn health, ra unknown
   - 'Ta quan Bobby L @ None': muốn family, ra food
   - 'Sua bot Enfa A+ @ None': muốn family, ra food
   - 'Do choi tre em @ None': muốn family, ra unknown
   - 'Binh sua em be @ None': muốn family, ra unknown
   - 'Sach luyen thi IELTS @ None': muốn education, ra unknown
   - 'Hoc phi @ None': muốn education, ra unknown
   - 'Phi giao hang @ None': muốn other, ra unknown
   - 'Giat ui @ None': muốn other, ra unknown
```

So với baseline (cùng bộ câu, cùng nhãn kỳ vọng):

- **Chat: 50/63 → 62/63.** Cả 5 câu nhiều khoản đã tách đúng; "ăn cơm", "mua nước", "đi ăn" ra `needs_amount`; `học phí` → `education`, `sữa bột` → `family`, `uống nước` → `food`, `mua 2 lít sữa` → `food`.
- Câu còn sai: **"mua đồ 1,5tr"** (kỳ vọng không nhóm, ra `shopping`). Không sửa nhãn kỳ vọng lẫn từ vựng cho khớp: "mua đồ" → shopping là chủ ý của từ vựng chat.
- **Món: 51/72 → 52/72.** Rất ít thay đổi vì bộ đo chạy với Gemini giả luôn "unknown": tác dụng của Gemini-trước-nhóm-mặc-định và của `bill_item_cache` không đo được ở đây
  (đã kiểm riêng bằng test đơn vị và chạy thật). Phần còn lại sai là món chưa có trong luật (Sting, Pepsi, Oreo, tã/sữa bột em bé, học phí, phí giao hàng...) hoặc
  cần Gemini; ví dụ "Ta quan"/"Sua bot" ra `food` vì luật món chưa có nhóm `family`.
- Đã thử khớp gần đúng ở món: một ca ("nước muối sinh lý" → "nước suối") làm tăng số ca sai-mà-tự-tin nên bị loại bằng luật cặp chữ OCR hay nhầm.

Giới hạn của số đo: bộ kiểm thử do người sửa luật viết, nhỏ (63 + 72), chưa phải tập giữ riêng từ bill thật của người dùng. Dùng để thấy hướng, không phải độ chính xác tuyệt đối.

## Baseline unify 2026-10-12 (trước khi gộp bộ phân loại)

Bộ `consistency_cases.jsonl`: 66 cụm, mỗi cụm đi qua đường chat và đường món trên bill (Gemini giả, không cửa hàng). Kỳ vọng theo bản luật đã duyệt, viết trước khi sửa.

```
== consistency: 31/66 cùng danh mục ở chat và bill (agree_rate 0.47), cùng và đúng kỳ vọng 30
   - 'nước ngọt Pepsi': chat=unknown bill=food kỳ vọng=food
   - 'sữa tươi TH': chat=unknown bill=food kỳ vọng=food
   - 'thịt ba chỉ': chat=unknown bill=food kỳ vọng=food
   - 'gạo ST25': chat=unknown bill=food kỳ vọng=food
   - 'mì gói Hảo Hảo': chat=unknown bill=food kỳ vọng=food
   - 'bia Tiger': chat=unknown bill=food kỳ vọng=food
   - 'bột giặt Omo': chat=unknown bill=shopping kỳ vọng=shopping
   - 'nước rửa chén Sunlight': chat=unknown bill=shopping kỳ vọng=shopping
   - 'giấy vệ sinh': chat=unknown bill=shopping kỳ vọng=shopping
   - 'khăn ướt': chat=food bill=shopping kỳ vọng=shopping
   - 'dầu gội Clear': chat=unknown bill=shopping kỳ vọng=shopping
   - 'kem đánh răng': chat=food bill=shopping kỳ vọng=shopping
   - 'sữa tắm Dove': chat=unknown bill=shopping kỳ vọng=shopping
   - 'sữa rửa mặt Cerave': chat=unknown bill=shopping kỳ vọng=shopping
   - 'kem chống nắng': chat=food bill=shopping kỳ vọng=shopping
   - 'áo thun': chat=unknown bill=shopping kỳ vọng=shopping
   - 'giày thể thao': chat=unknown bill=shopping kỳ vọng=shopping
   - 'ốp lưng điện thoại': chat=unknown bill=shopping kỳ vọng=shopping
   - 'vitamin C': chat=unknown bill=health kỳ vọng=health
   - 'paracetamol': chat=unknown bill=health kỳ vọng=health
   - 'khám bệnh': chat=health bill=unknown kỳ vọng=health
   - 'nha khoa': chat=health bill=unknown kỳ vọng=health
   - 'xét nghiệm máu': chat=health bill=unknown kỳ vọng=health
   - 'tã quần Bobby': chat=family bill=food kỳ vọng=family
   - 'sữa bột Enfa': chat=family bill=food kỳ vọng=family
   - 'bình sữa cho bé': chat=food bill=unknown kỳ vọng=family
   - 'đồ chơi trẻ em': chat=family bill=unknown kỳ vọng=family
   - 'học phí': chat=education bill=unknown kỳ vọng=education
   - 'khóa học tiếng Anh': chat=education bill=unknown kỳ vọng=education
   - 'sách giáo khoa': chat=education bill=unknown kỳ vọng=education
   - 'luyện thi IELTS': chat=education bill=unknown kỳ vọng=education
   - 'internet': chat=bills bill=unknown kỳ vọng=bills
   - 'đi grab': chat=transport bill=unknown kỳ vọng=transport
   - 'giặt ủi': chat=other bill=unknown kỳ vọng=other
   - 'hàng khác': chat=other bill=unknown kỳ vọng=unknown
```

## Sau khi gộp bộ phân loại (2026-10-12)

```
== chat: 62/63 đúng, accuracy 0.984, bỏ qua 0.016, sai mà tự tin 1
   nhầm [('expense', 1500000, None)] -> [('expense', 1500000, 'shopping')]: 1
   - 'mua đồ 1,5tr': muốn [('expense', 1500000, None)], ra [('expense', 1500000, 'shopping')]
== items: 60/72 đúng, accuracy 0.833, bỏ qua 0.236, sai mà tự tin 0
   nhầm food -> unknown: 7
   nhầm shopping -> unknown: 2
   nhầm health -> unknown: 2
   nhầm other -> unknown: 1
   - 'Sting dau 330ml @ None': muốn food, ra unknown
   - 'Oreo vani @ None': muốn food, ra unknown
   - 'Sua chua Vinamilk @ None': muốn food, ra unknown
   - 'Pepsi lon @ None': muốn food, ra unknown
   - 'Tra bi dao Ngo Gia @ None': muốn food, ra unknown
   - 'Keo cao su @ None': muốn food, ra unknown
   - 'Onigiri ca hoi @ None': muốn food, ra unknown
   - 'Pin AA Panasonic @ None': muốn shopping, ra unknown
   - 'But bi Thien Long @ None': muốn shopping, ra unknown
   - 'Nuoc muoi sinh ly @ None': muốn health, ra unknown
   - 'Berberin @ None': muốn health, ra unknown
   - 'Phi giao hang @ None': muốn other, ra unknown
== consistency: 65/66 cùng danh mục ở chat và bill (agree_rate 0.985), cùng và đúng kỳ vọng 64
   - 'hàng khác': chat=other bill=unknown kỳ vọng=unknown
```

So với baseline cùng ngày (cùng bộ câu, cùng nhãn kỳ vọng):

- **Cùng danh mục ở chat và bill: 31/66 lên 65/66.** Cùng danh mục và đúng kỳ vọng: 30 lên 64.
- **Món trên bill: 52/72 lên 60/72, số ca "sai mà tự tin" từ 2 xuống 0.**
- **Chat: giữ 62/63** (ca còn sai vẫn là "mua đồ 1,5tr", cố ý).
- Còn một ca lệch: **"hàng khác"**. Chat hiểu "khác" là tên danh mục Khác mà người dùng gõ; món trên bill gọi "hàng khác" thì chưa rõ. Giữ nguyên có chủ ý.
- Tác dụng phụ đã gặp và xử lý: khớp gần đúng trên câu chat làm "hôm nay trời đẹp quá" thành mua sắm ("đẹp quá" ~ "dép quai"), nên chat tắt khớp gần đúng (chỉ dành cho lỗi OCR).

## Rasa NLU: v3 (267 câu) so với v4 (thêm 1.100 câu tổng hợp), 2026-10-12

Đo bằng `rasa test nlu` (`ai-service/rasa/tools/summarize_report.py`). Bộ cũ = 74 câu viết tay; bộ mới `nlu_test_v2.yml` = 320 câu TỔNG HỢP, mẫu câu/món/cửa hàng/ngày khác hẳn bộ huấn luyện (script kiểm tra chống rò rỉ). Không có câu nào là chat thật của người dùng.

| | v3 bộ cũ | v4 bộ cũ | v3 bộ mới | v4 bộ mới |
|---|---|---|---|---|
| Ý định đúng | 93,2% | 93,2% | 79,4% | **81,9%** |
| F1 trung bình ý định | 0,924 | 0,935 | 0,740 | **0,787** |
| `amount` F1 | 0,96 | **0,98** | 0,80 | **0,97** |
| `merchant` F1 | 1,00 (n=3) | 0,86 (n=3) | 0,49 | **0,32** (recall 0,20) |
| `date` F1 | chưa có | chưa có | 0,00 | **0,58** |
| Ý định yếu nhất | out_of_scope 0,75 | out_of_scope 0,78 | add_income 0,55 | add_income 0,69 |

Nhận xét: số tiền chính xác hơn rõ (0,80 → 0,97), ngày bắt đầu được nhận, ý định cải thiện vừa phải. `merchant` tệ hơn vì bộ sinh chỉ có 2 mẫu câu chứa cửa hàng (ít ví dụ) — cần thêm mẫu hoặc nhờ luật `_merchant_in` trong parser. Ý định vẫn chỉ ~82% trên câu lạ nên dữ liệu thật vẫn cần.
