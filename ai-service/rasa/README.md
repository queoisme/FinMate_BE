# Rasa NLU (chat)

Hiểu ý định và cắt số tiền của câu chat ("đi ăn 200k", "nhận lương 14 củ 5", "tháng này tôi tiêu bao nhiêu"). Chạy thành
service `rasa` trong `docker-compose.yml`; `ai-service` gọi qua `RASA_URL` trong `POST /api/v1/chat/parse`.
Rasa tắt thì `ai-service` vẫn trả lời bằng luật (cầu dao 30 giây trong `app/chat/nlu.py`).

- `config.yml`, `domain.yml`, `data/nlu.yml`: DIET 100 epoch, 10 ý định (add_expense, add_income, transfer_money, ask_spending,
  greet, goodbye, confirm, deny, cancel, out_of_scope), thực thể `amount` và `merchant`. `rules.yml`/`stories.yml` chưa dùng.
- `tests/nlu_test.yml`: 74 câu giữ riêng để đo. Model `finmate-nlu-v3`: độ chính xác ý định 93,2% trên bộ này.
- `models/`: model đã huấn luyện, không nằm trong git.

Đo lại sau khi sửa dữ liệu:

```
docker compose run --rm rasa test nlu --nlu tests/nlu_test.yml -m models/finmate-nlu-v3.tar.gz --out results
```

Rasa hay quá tự tin với từ lạ (từng đọc "cắt kính cận 800k" là thu nhập, độ chắc 1,0), nên `app/chat/parser.py` không tin Rasa
khi câu có từ khóa chi tiêu rõ mà không có dấu hiệu thu hay chuyển tiền.
