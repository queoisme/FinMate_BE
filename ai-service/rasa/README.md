# Rasa NLU (chat)

Hiểu ý định và cắt số tiền của câu chat ("đi ăn 200k", "nhận lương 14 củ 5", "tháng này tôi tiêu bao nhiêu"). Chạy thành
service `rasa` trong `docker-compose.yml`; `ai-service` gọi qua `RASA_URL` trong `POST /api/v1/chat/parse`.
Rasa tắt thì `ai-service` vẫn trả lời bằng luật (cầu dao 30 giây trong `app/services/nlu.py`).

- `config.yml`, `domain.yml`, `data/nlu.yml`: DIET 100 epoch, 10 ý định (add_expense, add_income, transfer_money, ask_spending,
  greet, goodbye, confirm, deny, cancel, out_of_scope), thực thể `amount` và `merchant`. `rules.yml`/`stories.yml` chưa dùng.
- `tests/nlu_test.yml`: 74 câu giữ riêng để đo. Model đang chạy `finmate-nlu-v6` (compose): 97,3% ý định trên bộ này (v3 từng 93,2%).
- `models/`: model đã huấn luyện, không nằm trong git.

Đo lại sau khi sửa dữ liệu:

```
docker compose run --rm rasa test nlu --nlu tests/nlu_test.yml -m models/finmate-nlu-v3.tar.gz --out results
```

Rasa hay quá tự tin với từ lạ (từng đọc "cắt kính cận 800k" là thu nhập, độ chắc 1,0), nên `app/services/transaction_parser.py` không tin Rasa
khi câu có từ khóa chi tiêu rõ mà không có dấu hiệu thu hay chuyển tiền.

## Dữ liệu huấn luyện (cập nhật 2026-10-12)

- `data/nlu.yml`: 267 câu viết tay.
- `data/nlu_generated.yml` + `tests/nlu_test_v2.yml`: dữ liệu TỔNG HỢP do `tools/generate_nlu.py` sinh (train và test dùng mẫu câu/món/cửa hàng tách biệt).
- `data/nlu_massive_oos.yml` + `tests/nlu_test_massive_oos.yml`: câu ngoài chủ đề lấy từ **MASSIVE 1.1 (Amazon Science, CC BY 4.0, https://github.com/alexa/massive)**, đã điều chỉnh bằng `tools/import_massive.py`. Cần ghi nguồn này khi phân phối.
- Đo: `tools/summarize_report.py results/<thư mục>`; kết quả v3/v4/v5 ở `docs/evaluation.md`.

## Giảm "sai mà tự tin" (2026-10-18)

Vấn đề: Rasa hay đoán sai ý định mà vẫn báo độ chắc ~1,0 khi gặp món lạ ("nạp thẻ game" -> chuyển tiền, "trả tiền gas" -> thu nhập, "thuê phòng trọ" -> ngoài chủ đề).
Nguyên nhân là dữ liệu: bộ học chỉ có 32 món và vài động từ, nên model nhớ "món -> ý định" thay vì nhìn động từ.

- Thử đổi siêu tham số (ít epoch hơn, dropout/regularization mạnh hơn): lỗi tự tin giảm không đáng kể mà câu viết tay tụt 3-15 điểm -> bỏ. `model_confidence: linear_norm`
  không dùng được ở Rasa 3.6 (DIET chỉ có `softmax`).
- Mở rộng `tools/generate_nlu.py` (ITEMS_TRAIN 32 -> ~100 món, thêm họ "trả tiền / nạp / thuê / đóng", thêm 6 mẫu câu, thu nhập kiểu "mẹ gửi", chuyển tiền) và vài câu viết tay vào `data/nlu.yml`
  (hủy, ngoài chủ đề kiểu "tăng 10 phần trăm", nạp thẻ). Không chép câu nào của bộ test vào bộ học. Bộ test v2 dùng dòng số ngẫu nhiên riêng (`SEED + 1`) nên sửa bộ học không làm đổi bộ đo.
- Bộ **`tests/nlu_test_fresh.yml`** (67 câu viết tay, món/cách nói chưa từng thấy): bộ độc lập nhất, KHÔNG được đưa vào `data/`. Bộ v2 là dữ liệu máy sinh cùng "họ" với bộ học nên lạc quan hơn.
- Đã thử thêm một đợt dữ liệu nữa (thêm họ "thuê/đặt/đi chơi" và 11 mẫu câu): không tốt hơn trong sai số giữa các lần huấn luyện, nên bỏ. Mỗi lần huấn luyện cho kết quả lệch vài điểm; so sánh phải trên nhiều bộ.

Kết quả (v5 -> v6), Rasa thô / sau luật của `transaction_parser` (cùng các câu, qua `/chat/parse`):

| Bộ | Ý định thô | Sai mà chắc ≥ 0,8 | Đúng sau luật | Sai và không nhắc xem lại |
|---|---|---|---|---|
| viết tay (74) | 95,9 -> 97,3% | 3 -> 1 | 97,3 -> 98,6% | 1 -> 1 |
| mới, chưa từng thấy (67) | 82,1 -> 86,6% | 11 -> 8 | 89,6 -> 91,0% | 3 -> 2 |
| ngoài chủ đề (129) | 93,0 -> 97,7% | 4 -> 3 | 95,3 -> 97,7% | 3 -> 3 |
| tổng hợp v2 (315) | 80,6 -> 93,0% | 46 -> 16 | 93,7 -> 96,2% | 8 -> 5 |

Không ca nào bị tự lưu sai (`auto_save_ok`) ở cả hai model. Chỉ tiêu cần theo dõi là cột cuối (cộng gộp 1,9% trên 585 câu), do luật đã chặn phần lớn lỗi thô của Rasa.
Chưa hết: ở bộ mới Rasa thô vẫn sai mà tự tin 12% (khung câu lạ như "tốn 40k cho cơm chiên"). Hai lỗi còn lại sau luật không phải của Rasa và đã sửa trong `transaction_parser`/`amount_slang`: "rút 1 củ từ ví" mất số tiền ("từ" bị coi là củ từ khoai môn) và "bạn bao nhiêu tuổi" bị hiểu là câu hỏi chi tiêu. Sau sửa: sai mà không nhắc xem lại 0,9% (585 câu), đúng sau luật 100% / 92,5% / 98,4% / 97,1% (viết tay / mới / ngoài chủ đề / tổng hợp).

Huấn luyện lại model mới rồi đo: `python scripts/scorecard.py --only rasa` (từ gốc repo). Model nằm ngoài git: sau khi huấn luyện đặt tên `finmate-nlu-vN` và sửa `docker-compose.yml`.
