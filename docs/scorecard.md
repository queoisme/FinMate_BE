# Bảng điểm khả thi FinMate

Đo lúc 2026-10-18 22:22 bằng `python scripts/scorecard.py`. Ngưỡng là **đề xuất**, chủ dự án quyết định; "—" là chỉ số để tham khảo, chưa có ngưỡng.

| Nhóm | Kết quả |
|---|---|
| Chatbot | CHƯA ĐẠT (17/19 chỉ tiêu) |
| Speech | ĐẠT (5/5 chỉ tiêu) |
| OCR | ĐẠT (6/6 chỉ tiêu) |

ĐẠT trên bộ mẫu nhỏ là điều kiện cần, chưa đủ để kết luận khả thi: xem "Độ tin cậy" của từng khối.

## Chatbot: Chat và món: luật + từ điển (không Rasa, không AI) — ĐẠT

| Chỉ tiêu | Đo được | Ngưỡng | |
|---|---|---|---|
| Chat: đúng (66 câu) | 98.5% | ≥ 95.0% | ĐẠT |
| Chat: sai mà vẫn tự tin | 1.5% | ≤ 2.0% | ĐẠT |
| Món trên bill: sai mà vẫn tự tin (127 món, chưa có AI) | 4.7% | ≤ 5.0% | ĐẠT |
| Món trên bill: đúng (chỉ luật, phần còn lại cần AI) | 84.3% |  | — |
| Chat và bill ra cùng danh mục cho cùng một cụm | 98.5% | ≥ 90.0% | ĐẠT |

**Độ tin cậy:** Bộ câu do người sửa luật viết nhãn, nhỏ (~66 câu chat, ~127 món). Cho thấy hướng đi, không phải độ chính xác với người dùng thật.

Ca sai tiêu biểu:

- 'mua đồ 1,5tr': muốn [('expense', 1500000, None)], ra [('expense', 1500000, 'shopping')]
- 'Sting dau 330ml @ None': muốn food, ra unknown
- 'Oreo vani @ None': muốn food, ra unknown
- 'Sua chua Vinamilk @ None': muốn food, ra unknown
- 'Pepsi lon @ None': muốn food, ra unknown
- 'Tra bi dao Ngo Gia @ None': muốn food, ra unknown
- 'Keo cao su @ None': muốn food, ra unknown
- 'Onigiri ca hoi @ None': muốn food, ra unknown

## Chatbot: Đọc số tiền tiếng Việt (chữ, 'k', 'củ', 'lít') — ĐẠT

| Chỉ tiêu | Đo được | Ngưỡng | |
|---|---|---|---|
| Đọc số tiền trong câu chat/voice, bộ chính (105 câu) | 96.2% | ≥ 95.0% | ĐẠT |
| Đọc số tiền trong câu chat/voice, bộ giữ riêng (71 câu) | 98.6% | ≥ 95.0% | ĐẠT |

**Độ tin cậy:** Bộ giữ riêng ít bị tinh chỉnh theo hơn bộ chính, nhưng cũng do người viết luật gán nhãn.

## Chatbot: Chat qua endpoint thật (Rasa đang chạy) — CHƯA ĐẠT

| Chỉ tiêu | Đo được | Ngưỡng | |
|---|---|---|---|
| Rasa + luật: đúng (66 câu) | 97.0% | ≥ 95.0% | ĐẠT |
| Rasa + luật: sai mà vẫn tự tin | 3.0% | ≤ 2.0% | CHƯA ĐẠT |
| Rasa + luật: Rasa có trả lời | 100.0% | ≥ 95.0% | ĐẠT |
| Rasa + luật: thời gian p95 | 35 ms | ≤ 500 ms | ĐẠT |

**Độ tin cậy:** Cùng bộ câu với khối luật, nhưng đi đúng đường backend gọi. Khác biệt với khối luật chính là phần Rasa gây ra.

Ca sai tiêu biểu:

- 'mua đồ 1,5tr': muốn [('expense', 1500000, None)], ra [('expense', 1500000, 'shopping')]
- 'cảm ơn': muốn thanks, ra confirm

## Chatbot: Rasa NLU (ý định + số tiền) — CHƯA ĐẠT

| Chỉ tiêu | Đo được | Ngưỡng | |
|---|---|---|---|
| Ý định, câu viết tay (74 câu) | 95.9% | ≥ 90.0% | ĐẠT |
| Đoán sai ý định mà độ chắc ≥ 0,8, câu viết tay | 4.1% | ≤ 5.0% | ĐẠT |
| Cắt đúng số tiền, câu viết tay | 100.0% | ≥ 90.0% | ĐẠT |
| Ý định, ngoài chủ đề (MASSIVE) (129 câu) | 93.0% | ≥ 90.0% | ĐẠT |
| Đoán sai ý định mà độ chắc ≥ 0,8, ngoài chủ đề (MASSIVE) | 3.1% | ≤ 5.0% | ĐẠT |
| Ý định, tổng hợp, nhiều biến thể (320 câu) | 80.9% | ≥ 80.0% | ĐẠT |
| Đoán sai ý định mà độ chắc ≥ 0,8, tổng hợp, nhiều biến thể | 14.7% | ≤ 5.0% | CHƯA ĐẠT |
| Cắt đúng số tiền, tổng hợp, nhiều biến thể | 92.2% | ≥ 90.0% | ĐẠT |
| Thời gian p95 một câu | 28 ms | ≤ 200 ms | ĐẠT |

**Độ tin cậy:** Bộ 'câu viết tay' tách khỏi bộ huấn luyện. Bộ 'tổng hợp' do máy sinh từ mẫu câu: dễ hơn thật ở chỗ khuôn mẫu, khó hơn ở chỗ không dấu.

Ca sai tiêu biểu:

- [câu viết tay] 'nạp game 200k': muốn add_expense, ra transfer_money (1.00)
- [câu viết tay] 'hủy giúp mình': muốn cancel, ra out_of_scope (0.99)
- [câu viết tay] 'lương 20 triệu có đủ sống không': muốn out_of_scope, ra add_income (1.00)
- [ngoài chủ đề (MASSIVE)] 'giảm hai mươi phần trăm': muốn out_of_scope, ra add_expense (1.00)
- [ngoài chủ đề (MASSIVE)] 'im lặng': muốn out_of_scope, ra deny (0.45)
- [ngoài chủ đề (MASSIVE)] 'thay đổi tất cả các đèn trong nhà thành màu xanh dương': muốn out_of_scope, ra deny (0.56)
- [ngoài chủ đề (MASSIVE)] 'thêm bữa trưa với dương': muốn out_of_scope, ra add_income (1.00)
- [tổng hợp, nhiều biến thể] 'hom thu ba, nap the game 35 canh': muốn add_expense, ra add_income (0.96)
- [tổng hợp, nhiều biến thể] 'hôm thứ ba, mua cá 1tr8': muốn add_expense, ra out_of_scope (1.00)
- [tổng hợp, nhiều biến thể] 'ghé Gojek trả tiền gas 3tr5': muốn add_expense, ra add_income (1.00)
- [tổng hợp, nhiều biến thể] 'giat ui khoang 95k': muốn add_expense, ra transfer_money (0.99)

## Speech: Giọng nói, phần chữ → số tiền/loại/danh mục — ĐẠT

| Chỉ tiêu | Đo được | Ngưỡng | |
|---|---|---|---|
| Chữ sau STT → số tiền đúng (53 câu) | 98.1% | ≥ 95.0% | ĐẠT |
| Chữ sau STT → thu/chi đúng | 96.2% | ≥ 95.0% | ĐẠT |
| Chữ sau STT → danh mục đúng | 86.7% | ≥ 85.0% | ĐẠT |

**Độ tin cậy:** Chỉ đo phần SAU khi đã có chữ. Câu đưa vào là chữ viết sẵn, chưa phải kết quả nhận dạng thật của người nói giọng vùng miền.

Ca sai tiêu biểu:

- 'mua áo một trăm năm mươi ngàn': tiền 150000→150000, loại Debit→Debit, nhóm shopping→other
- 'mua laptop hai mươi ba triệu năm trăm': tiền 23500000→23500000, loại Debit→Debit, nhóm shopping→other
- 'mua áo 150k': tiền 150000→150000, loại Debit→Debit, nhóm shopping→other
- 'ăn phở 2 mươi lăm nghìn': tiền 25000→5000, loại Debit→Debit, nhóm food→food
- 'được mẹ cho năm trăm nghìn': tiền 500000→500000, loại Credit→Debit, nhóm None→None
- 'bạn chuyển khoản trả nợ ba trăm nghìn': tiền 300000→300000, loại Credit→Debit, nhóm None→None

## Speech: Giọng nói → chữ (GIPFormer) — ĐẠT

| Chỉ tiêu | Đo được | Ngưỡng | |
|---|---|---|---|
| Tỷ lệ lỗi từ (WER), phần đầu câu (13 file) | 1.7% | ≤ 10.0% | ĐẠT |
| Câu nhận dạng đúng hoàn toàn | 84.6% |  | — |
| Tốc độ: thời gian xử lý / thời lượng âm thanh | 0.04 | ≤ 0.50 | ĐẠT |

**Độ tin cậy:** Chỉ vài chục file, một giọng/nguồn, tham chiếu suy ra từ tên file (bị cắt) nên chỉ chấm phần đầu câu. Thêm `Data_Voice/references.tsv` (tên_file<TAB>lời nói đầy đủ) để đo cả câu. Chưa có giọng thật trong môi trường ồn.

Ca sai tiêu biểu:

- WER 17%: muốn 'chuyen hai trieu sang tiet kiem' / ra 'chuyen hai trieu xang tiet kiem'
- WER 9%: muốn 'toi co di mua mot bo quan ao het hai trieu' / ra 'toi co di mua mot bo quan ao het hai chieu toi cho me hai cu me cho toi hai toi'

## OCR: Hóa đơn: PaddleOCR + VietOCR + bóc tách — ĐẠT

| Chỉ tiêu | Đo được | Ngưỡng | |
|---|---|---|---|
| Tổng tiền đúng (58 ảnh) | 100.0% | ≥ 95.0% | ĐẠT |
| LỖI ÂM THẦM: sai mà app không nhắc kiểm tra | 0.0% | ≤ 2.0% | ĐẠT |
| Đúng cả tổng, tên cửa hàng, danh mục | 91.4% | ≥ 85.0% | ĐẠT |
| Tên cửa hàng đúng | 93.1% | ≥ 90.0% | ĐẠT |
| Danh mục đúng | 96.6% | ≥ 90.0% | ĐẠT |
| Bill bị nhắc kiểm tra (phiền, không hại) | 27.6% |  | — |
| Thời gian trung bình một ảnh | 2.4 giây | ≤ 5.0 giây | ĐẠT |

**Độ tin cậy:** Bộ ảnh này cũng là bộ đã dùng để sửa luật bóc tách, nên số đo lạc quan hơn ảnh lạ. Muốn biết độ chính xác thật cần ảnh chụp mới chưa từng dùng để chỉnh.

