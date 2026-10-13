# Rasa NLU (chat)

Hiểu ý định và cắt số tiền của câu chat ("đi ăn 200k", "nhận lương 14 củ 5", "tháng này tôi tiêu bao nhiêu"). Chạy thành
service `rasa` trong `docker-compose.yml`; `ai-service` gọi qua `RASA_URL` trong `POST /api/v1/chat/parse`.
Rasa tắt thì `ai-service` vẫn trả lời bằng luật (cầu dao 30 giây trong `app/services/nlu.py`).

- `config.yml`, `domain.yml`, `data/nlu.yml`: DIET 100 epoch, 10 ý định (add_expense, add_income, transfer_money, ask_spending,
  greet, goodbye, confirm, deny, cancel, out_of_scope), thực thể `amount` và `merchant`. `rules.yml`/`stories.yml` chưa dùng.
- `tests/nlu_test.yml`: 74 câu giữ riêng để đo. Model `finmate-nlu-v3`: độ chính xác ý định 93,2% trên bộ này.
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
