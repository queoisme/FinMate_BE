# Rasa NLU (chat)

Hiểu ý định và cắt số tiền của câu chat ("đi ăn 200k", "nhận lương 14 củ 5", "tháng này tôi tiêu bao nhiêu"). Chạy thành
service `rasa` trong `docker-compose.yml`; `ai-service` gọi qua `RASA_URL` trong `POST /api/v1/chat/parse`.
Rasa tắt thì `ai-service` vẫn trả lời bằng luật (cầu dao 30 giây trong `app/services/nlu.py`).

- `config.yml`, `domain.yml`, `data/nlu.yml`: DIET 100 epoch, 10 ý định (add_expense, add_income, transfer_money, ask_spending,
  greet, goodbye, confirm, deny, cancel, out_of_scope), thực thể `amount` và `merchant`. `rules.yml`/`stories.yml` chưa dùng.
- `tests/nlu_test.yml`: 74 câu giữ riêng để đo. Model đang chạy `finmate-nlu-v8` (compose): 97,3% ý định trên bộ này (v3 từng 93,2%).
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

## Khung câu lạ (2026-10-19): v6 -> v8

Sau đợt trước Rasa thô vẫn sai mà chắc tới 12-15% ở câu có khung/món chưa từng thấy ("tốn 40k cho cơm chiên", "thẻ gym tháng 500k", "nạp 400k vào ví grab").

- Bộ test mới **`tests/nlu_test_fresh2.yml`** (72 câu viết tay, nhiều khung câu: tiền đứng đầu, từ đệm, đảo trật tự, không dấu, câu ngoài chủ đề có chữ tiền). Viết TRƯỚC khi đổi dữ liệu.
  Cùng với `nlu_test_fresh.yml`, KHÔNG được đưa vào `data/`. `generate_nlu.py` giờ dừng nếu bộ học chứa câu của bất kỳ bộ test viết tay nào
  (bắt được một rò rỉ có từ trước: "nên mua nhà hay thuê nhà" nằm cả trong bộ học lẫn `nlu_test.yml`; đã bỏ khỏi bộ học).
- Dữ liệu sinh đợt 3: ~25 khung chi tiêu mới (tiền đứng đầu, "hết X cho Y", "Y tháng này X", "Y X nhé/luôn", cụm chưa có số tiền như "đi ăn"), ~100 danh từ/khoản định kỳ,
  thu nhập kiểu "dì út gửi", nạp vào ví khác MoMo ("nạp X vào ví lazada"), "rút tiền X", ngoài chủ đề có chữ tiền ("đổi X sang yên"). Bộ học 1.141 -> 1.649 câu.
- `config.yml` cố định `random_seed: 1`: huấn luyện lại ra đúng model. Giữa các seed kết quả lệch 1-4 điểm, nên đã so hai seed mỗi biến thể trước khi chọn.
- Một biến thể thiếu "rút tiền X" và cụm ngắn làm hỏng hai câu của bộ chat chính ("rút tiền 500k" thành thu nhập, "đi ăn" thành xác nhận): đã thêm hai họ đó vào bộ học.

| Bộ | Rasa thô đúng (v6 -> v8) | Sai mà chắc >= 0,8 | Đúng sau luật |
|---|---|---|---|
| viết tay (74) | 97,3 -> 97,3% | 1 -> 2 | 100 -> 98,6% |
| món chưa từng thấy (67) | 86,6 -> 92,5% | 8 -> 5 | 92,5 -> 94,0% |
| khung câu chưa từng thấy (72) | 84,7 -> 93,1% | 11 -> 4 | 84,7 -> 91,7% |
| ngoài chủ đề (129) | 97,7 -> 96,1% | 3 -> 1 | 98,4 -> 97,7% |
| tổng hợp v2 (315) | 93,0 -> 91,7% | 16 -> 21 | 97,1 -> 94,9% |

Thắng rõ ở khung/món chưa từng thấy (mục tiêu), kém nhẹ ở bộ v2 máy sinh và vài câu viết tay: v8 không phải tốt hơn ở mọi chỗ.

Chốt chặn phía luật (`transaction_parser`), vì model không bao giờ hết sai mà chắc:
- Luật và Rasa bất đồng loại giao dịch (kể cả khi Rasa chắc 1,0 và thắng) thì **không tự lưu** ("nạp 400k vào ví grab" từng bị ghi thẳng thành chi tiêu).
- Câu có "gửi" (trừ "gửi xe") không tự lưu vì mơ hồ chiều tiền ("bạn ở ghép gửi tiền trọ" là tiền vào).
- "tổng tiền" là câu hỏi tổng; "tien luong" không dấu là thu nhập; Rasa ép câu hỏi tổng thành khoản chi thì dùng luật.

Kết quả sản phẩm (657 câu qua `/chat/parse`): sai mà không được nhắc xem lại 1,2%, **tự lưu sai 0** (trước đó 1). Còn lại chủ yếu là câu mơ hồ ("xóa dữ liệu", "uống nước mía khoảng 2 lít 5")
và vài câu Rasa chắc 1,0 mà luật không có dấu hiệu phản đối ("tiền lãi tiết kiệm 85k" bị hiểu là chi tiêu). Khoản chi tự lưu có ngưỡng 2 triệu (`chat_auto_save_below`) và hoàn tác được.
