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
