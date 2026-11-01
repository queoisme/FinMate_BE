# Rasa NLU (chat)

Hiểu ý định và cắt số tiền của câu chat ("đi ăn 200k", "nhận lương 14 củ 5", "tháng này tôi tiêu bao nhiêu"). Chạy thành
service `rasa` trong `docker-compose.yml`; `ai-service` gọi qua `RASA_URL` trong `POST /api/v1/chat/parse`.
Rasa tắt thì `ai-service` vẫn trả lời bằng luật (cầu dao 30 giây trong `app/services/nlu.py`).

## Dữ liệu huấn luyện và đo nằm trong DB (cập nhật 2026-10-23)

**Nguồn sự thật là bảng `nlu_examples` của DB ai-service** (một câu đúng một dòng nên không thể vừa ở train vừa ở test; chú thích thực thể `[45k](amount)` được giữ nguyên). Trong `rasa/` không còn câu mẫu viết tay hay do
script sinh: `data/nlu_db.yml` và `tests/nlu_test*.yml` là tệp SINH RA từ DB (nằm trong `.gitignore`), không sửa tay.
Câu cũ (3.168 câu train, 710 câu test; tên nhóm test = tên tệp cũ, vd. `nlu_test_fresh2`) đã nhập vào DB ngày 2026-10-23. Khi nhập, DB phát hiện 13 câu test trùng câu train ("xin chào", "hủy đi"...) và 1 câu mang hai nhãn (`chào buổi tối`: greet và out_of_scope; giữ greet): tệp YAML cũ đã âm thầm rò rỉ như vậy.

Quy trình (từ `ai-service`, cần DB; từ máy chủ đặt `DATABASE_URL=postgresql+asyncpg://finmate:finmate@localhost:5434/finmate_ai`, hoặc chạy trong container sau khi build lại image):

```
python scripts/nlu_examples.py import <tệp>.jsonl --write                      # thêm câu mới (bỏ --write để xem trước); tệp YAML: thêm --split train|test
python scripts/nlu_model.py train finmate-nlu-vN                               # xuất DB -> huấn luyện -> ghi "candidate" kèm mã băm bản dữ liệu (bảng nlu_model_versions)
python scripts/nlu_model.py eval finmate-nlu-vN                                # đo trên tập test của DB: Rasa thô VÀ Rasa + luật (bật Rasa tạm, ~7 phút)
python scripts/nlu_model.py promote finmate-nlu-vN                             # bị chặn nếu tụt so với bản đang chạy; qua thì chép thành models/active.tar.gz và khởi động lại Rasa
python scripts/nlu_model.py list                                               # sổ model: trạng thái, bản dữ liệu, số đo
```

Cổng `promote` chặn khi: chưa đo; luật phá nhiều hơn cứu; độ đúng sau luật HOẶC Rasa thô tụt (luật không được gánh thay model); bộ độc lập `nlu_test_fresh`/`nlu_test_fresh2` tụt;
tập test đã đổi so với lúc đo bản đang chạy (phải đo lại bản đó cho cùng tập). `docker-compose.yml` chạy `models/active.tar.gz`, không còn tên model cố định.
Mốc đầu tiên (2026-10-24, tập test DB 710 câu): `finmate-nlu-v11` thô 94,65%, sau luật 98,17% (luật ghi đè 26: cứu 25, phá 0); chưa rõ bản dữ liệu đã học.

**Học từ người dùng (2026-10-24, phía ai-service; backend C# chưa gọi):** `POST /api/v1/chat/unclear` nhận câu chat mà `/chat/parse` đánh dấu `unclear_reason` (kèm đoán của máy) vào bảng chờ `nlu_candidates`,
CHỈ khi người dùng đã đồng ý chia sẻ. Câu có số điện thoại, email, số dài, đường dẫn, `[ ]` hoặc `unclear_reason = cancelled` bị từ chối; câu chưa duyệt/bị loại tự xóa sau 7 ngày;
`POST /chat/unclear/erase` xóa theo người dùng. Câu KHÔNG vào dữ liệu huấn luyện cho tới khi người duyệt gán nhãn (đoán của máy không phải nhãn):

```
python scripts/nlu_candidates.py list                                          # câu đang chờ + máy đoán gì
python scripts/nlu_candidates.py accept <id> --intent add_expense [--text ...]  # nhận (sửa --text để bỏ tên người); thành câu user_feedback trong nlu_examples, xóa khỏi bảng chờ
python scripts/nlu_candidates.py reject <id>
```

Còn thiếu phía backend (xem kế hoạch): thêm `unclear_reason` vào hợp đồng `ChatParseApiResponse`, kiểm tra `TrainingConsentGranted` (nâng `ConsentVersions.Training` vì bản 1 chỉ nói về hóa đơn),
gọi `/chat/unclear` ở `ChatCommandHandler`/`ChatAnalyzeCommandHandler`, và gọi `/chat/unclear/erase` khi rút đồng ý hoặc xóa tài khoản.

`domain.yml` chỉ còn ý định, thực thể và cấu hình phiên: các câu `utter_*` và luật trong `rules.yml` đã bỏ vì ai-service chỉ gọi `/model/parse`, còn câu trả lời nằm ở `app/data/intent_cues.json` (sửa được từ trang quản trị).
`tools/generate_nlu.py`, `generate_contrast.py`, `import_massive.py` chỉ còn là công cụ SINH ỨNG VIÊN: chúng ghi YAML vào `data/`/`tests/`, phải nhập vào DB (`import ... --split`) rồi xóa tệp đó; không huấn luyện từ tệp tĩnh
(`tests/unit/test_rasa_data_in_db.py` canh việc này). Các mục bên dưới là lịch sử đo từ trước ngày này và nhắc tới các tệp YAML cũ.

- `config.yml`, `domain.yml`, `data/nlu_db.yml`: DIET 100 epoch, 10 ý định (add_expense, add_income, transfer_money, ask_spending,
  greet, goodbye, confirm, deny, cancel, out_of_scope), thực thể `amount` và `merchant`. `rules.yml`/`stories.yml` chưa dùng.
- `tests/nlu_test.yml`: 74 câu giữ riêng để đo. Model đang chạy `finmate-nlu-v10` (compose, từ 2026-10-21; v8 giữ trong `models/` để quay lại): 97,3% ý định trên bộ này (v3 từng 93,2%).
- `models/`: model đã huấn luyện, không nằm trong git.

Đo lại sau khi sửa dữ liệu:

```
docker compose run --rm rasa test nlu --nlu tests/nlu_test.yml -m models/finmate-nlu-v3.tar.gz --out results
```

Rasa hay quá tự tin (đo được sai ở độ chắc 1,00: softmax chưa hiệu chỉnh), nên `app/services/transaction_parser.py` phân xử theo NGUYÊN TẮC chứ không
theo từng câu: dấu hiệu cụ thể trong câu (động từ/vai trò) thắng Rasa, bằng chứng yếu chỉ thắng khi Rasa không chắc (xem `.context/ARCHITECTURE.md`,
mục "Gộp Rasa với luật"). Từ vựng nằm ở `app/data/intent_cues.json`. Mỗi thay đổi ở lớp luật phải qua cổng `scripts/audit_overrides.py` (cứu ≥ phá,
không tụt bộ độc lập); đo 2026-10-21 với Rasa v10: Rasa thô 94,2% → Rasa + luật 97,1% trên 795 câu, luật ghi đè 24 câu, cứu 23, phá 0; bộ độc lập
`heldout_intent` (138 câu) 88,4% → 89,9%. Câu không dấu là giới hạn đã biết: "ban" có thể là bạn/bán nên luật nhường Rasa.

## Dữ liệu huấn luyện (cập nhật 2026-10-12)

- `data/nlu.yml`: 267 câu viết tay.
- `data/nlu_generated.yml` + `tests/nlu_test_v2.yml`: dữ liệu TỔNG HỢP do `tools/generate_nlu.py` sinh (train và test dùng mẫu câu/món/cửa hàng tách biệt).
- `data/nlu_contrast.yml` (2026-10-21, đã huấn luyện thành v9 rồi v10; số đo ở dưới): 753 câu TỔNG HỢP "cặp đối lập" do `tools/generate_contrast.py` sinh (`PYTHONPATH=. python rasa/tools/generate_contrast.py`), dạy model nhìn chiều giao dịch: "mẹ gửi cho 20k" (thu) so với "gửi cho mẹ 20k" (chuyển), "phí rút tiền" (chi) so với "rút tiền" (chuyển), "tiền lãi về" (thu) so với "trả lãi vay" (chi), "nạp vào ví" (chuyển) so với "nạp thẻ cào" (chi), "xóa tài khoản" (ngoài chủ đề). Người "ba" cố ý bỏ ra: các câu "ba gửi cho ..." trong bộ thử viết tay dùng để kiểm xem model có học được cấu trúc hay chỉ thuộc câu. Từ v10 có thêm nhóm xã giao ("ăn cơm chưa", "đi ngủ không", "cảm ơn" dạy là out_of_scope vì Rasa không có ý định thanks, "giá ... bao nhiêu"). Đo bằng `rasa test nlu` (độ đúng ý định, Rasa thô; kết quả trong `results/cmp_v8|v9|v10_*`): fresh món lạ 92,5% / 95,5% / 97,0%, fresh2 khung lạ 93,1% / 97,2% / 98,6%, tổng hợp v2 91,7% / 93,0% / 93,0%, ngoài chủ đề 96,9% / 97,7% / 97,7%, viết tay 97,3% cả ba (v8 / v9 / v10). Đo đầu cuối (Rasa + luật mới, 18+8 câu xã giao và đảo chiều): v8 25/26, v10 26/26; chat chuẩn 65/66 và voice không đổi. Điểm yếu còn lại: bộ `results/_probe_direction.yml` (33 câu, người "ba" giữ riêng) v9 đúng 93,9% nhưng v10 chỉ 81,8% (v8 57,6%), tức độ học cấu trúc "ba gửi ..." không ổn định giữa các lần huấn luyện; "ba gửi cho 2 triệu" vẫn sai ở Rasa thô (độ chắc 1,00) và do luật trong `transaction_parser.py` chặn. `merchant` F1 trên v2 vẫn ~0,57.
- `data/nlu_massive_oos.yml` + `tests/nlu_test_massive_oos.yml`: câu ngoài chủ đề lấy từ **MASSIVE 1.1 (Amazon Science, CC BY 4.0, https://github.com/alexa/massive)**, đã điều chỉnh bằng `tools/import_massive.py`. Cần ghi nguồn này khi phân phối.
- Đo: `tools/summarize_report.py results/<thư mục>`; kết quả v3/v4/v5 ở `docs/evaluation.md`.
- `data/nlu_db.yml` (2026-10-22, KHÔNG nằm trong git): câu của bảng `nlu_examples` trong DB (206 câu lạc đề ban đầu: so sánh AI, kiến thức chung, tư vấn đầu tư, câu có chữ tiền nhưng không phải lệnh...). Nguồn sự thật là DB, không sửa file; mỗi câu có đúng một dòng nên không thể vừa ở train vừa ở test. Quy trình, từ gốc repo:
  `docker compose exec -T ai-service python scripts/nlu_examples.py import seed/<tệp>.jsonl --write` (nhập, xem trước nếu bỏ `--write`),
  `cmd /c "docker compose exec -T ai-service python scripts/nlu_examples.py export --split train > ai-service\rasa\data\nlu_db.yml"` (xuất tập train; PowerShell `>` sẽ ghi sai mã hóa),
  rồi huấn luyện như thường. Đo tập test giữ riêng (Rasa + luật, theo nhóm): `docker compose exec -T ai-service python scripts/nlu_examples.py eval --split test`; sau khi khởi động lại ai-service hãy đợi Rasa nóng lên, nếu không cầu dao 30 giây làm số đo là của riêng lớp luật (lệnh tự cảnh báo khi gần như không câu nào do Rasa quyết).
  Đo một model thử mà không đổi model đang chạy: chạy một container Rasa tạm trên mạng compose, một tiến trình `uvicorn` thứ hai trong ai-service với `RASA_URL` trỏ vào nó, rồi đặt `CHAT_PARSE_URL` (các script `eval_*`/`nlu_examples.py eval`) và `RASA_PARSE_URL` (`tools/eval_live.py`).
  **v11** (huấn luyện từ 137 câu train của DB, chưa bật): tập test DB 85,5% → 94,2% (rò rỉ 10 → 4); sau luật trên 657 câu cũ 98,5% → 98,5%, sai-mà-không-nhắc 3 → 1, tự lưu sai 0; Rasa thô có một ca từ chối nhầm lệnh hợp lệ ("mất 1tr2 tiền sửa máy giặt" → `out_of_scope`, độ chắc 1,00) và `nlu_test_fresh2` 98,6% → 94,4%.

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

## Câu hỏi tổng có tháng/năm cụ thể (2026-10-31): thử v13, KHÔNG promote

Triệu chứng: "năm 2025 tôi tiêu bao nhiêu", "tháng 9 năm 2025 tôi chi bao nhiêu" bị Rasa đoán `out_of_scope` với độ chắc 0,86-0,98 vì bộ học chỉ có "tháng này/tuần này/hôm qua". Lớp luật đã bù (`transaction_parser`: hỗ trợ "tháng N", "năm nay/ngoái", "năm 2025"; nguyên tắc phân xử 2b trong `_arbitrate`), nên người dùng đã được trả lời đúng kỳ.

Sửa phía dữ liệu: `tools/generate_periods.py` sinh `seed/nlu_periods.jsonl` (263 câu, nhóm `periods`, đã nhập vào DB): câu hỏi tổng có tháng/năm và câu ĐỐI LẬP ngoài chủ đề cũng có tháng/năm ("giá vàng tháng 9 bao nhiêu"). Train và test dùng KHUNG CÂU khác nhau (test: 68 câu, khung chưa từng thấy).

Đo trên cùng tập test 778 câu (Rasa thô / sau luật): v11 91,26% / **98,33%** (đang chạy); v12 92,16% / 97,04%; v13 (huấn luyện từ DB có thêm nhóm `periods`) 93,44% / 95,89%. Riêng nhóm `periods`: thô v11 55,9% → v12 72,1% → v13 92,6% (dữ liệu mới có tác dụng), sau luật v11 đã 100%. Cổng `promote` CHẶN v13: độ đúng sau luật tụt 98,33% → 95,89% và bộ độc lập `nlu_test_fresh` tụt 100% → 96,72%. Các câu v13 sai mà v11 đúng phần lớn không liên quan tháng/năm (thu nhập kiểu "tiền cashback", "tiền thưởng tết" bị đoán chi tiêu; "3 ngày qua"; "đúng vậy ghi giúp mình"); v12, huấn luyện từ DB trước khi có nhóm `periods`, cũng tụt so với v11, nên phần lớn chênh lệch là mô hình huấn luyện từ DB hiện chưa bằng v11 chứ không do nhóm dữ liệu mới. Chưa tìm ra vì sao (chênh dữ liệu so với lúc huấn luyện v11 hoặc dao động giữa các lần huấn luyện, xem mục "Khung câu lạ" về seed): việc này cần điều tra riêng trước khi thử model tiếp theo.
Trạng thái: model đang chạy vẫn là `finmate-nlu-v11` (`models/active.tar.gz`); nhóm `periods` đã nằm trong DB nên sẽ có trong lần huấn luyện kế tiếp.

**Chọn phiên bản model từ trang admin (2026-10-31).** Backend: `GET /api/v1/admin/rasa/models` liệt kê MỌI phiên bản đã đăng ký trong sổ `nlu_model_versions` (trạng thái, số câu học, số đo thô và sau luật theo từng bộ test, ghi chú, và `gate` = bản đó có qua cổng đo để thay bản đang chạy không, kèm lý do), cùng `running_model_file` (tệp Rasa đang nạp thật), `in_sync` (sổ và Rasa khớp nhau), `activation` (tiến trình nạp) và `can_activate`. `POST /api/v1/admin/rasa/models/{version}/activate` (thân `{}` hoặc `{"expected_active","force","reason"}`) áp dụng một phiên bản, kể cả quay về bản cũ: qua cổng đo như `promote`, `force` + `reason` (≥ 3 ký tự) để ép, ghi `audit_logs` và `notes` của sổ. Trả **202** ngay; Rasa nạp nóng ở nền (`PUT /model`, không khởi động lại), khi nạp xong mới ghi sổ và đổi `models/active.tar.gz` nên khởi động lại sau này vẫn đúng bản. **Nạp một model mất vài phút (đo được 141-255 giây) và trong lúc nạp Rasa không trả lời: chat chạy bằng luật, không ngừng dịch vụ.** Nạp lỗi thì model cũ giữ nguyên và lỗi nằm ở `activation.error`. Rasa lệch sổ (`in_sync: false`, ví dụ ai-service bị tắt giữa lúc nạp) thì kích hoạt lại chính bản đang chạy để nạp lại cho khớp. Cần `RASA_MODELS_DIR` và volume `ai-service/rasa/models` gắn vào ai-service (đã có trong `docker-compose.yml`). Huấn luyện và đo (`train`, `eval`) vẫn chỉ qua dòng lệnh; model chưa có trong sổ (`register`) thì chưa hiện trên trang admin.

Coverage theo chủ đề FinMate và rủi ro tiềm ẩn (bộ 198 câu độc lập, test `tests/unit/test_rasa_live_coverage.py` và `test_rasa_data_coverage.py`): xem `docs/rasa-coverage-report.md`.

Thí nghiệm đổi seed (2026-10-31): huấn luyện lại ĐÚNG dữ liệu của v13 (`nlu_db.yml`, 3363 câu) với `random_seed` 2 và 3 (`models/finmate-nlu-v13-seed2|seed3.tar.gz`, chưa đăng ký vào sổ), đo trên cùng 778 câu. Rasa thô / sau luật: seed 1 (v13) 93,44% / 95,89%; seed 2 94,09% / 96,40%; seed 3 93,06% / 96,92%; v11 91,26% / **98,33%**. Kết luận: nhiễu do seed khoảng ±0,5 điểm (dải 1 điểm) ở cả hai số, các nhóm nhỏ dao động mạnh hơn (`money_lookalike` 7/10, 10/10, 7/10; `nlu_test` 68, 71, 71 trên 71), nên MỘT lần đo không đủ để kết luận về model; nhưng cả ba seed đều dưới v11 sau luật 1,4–2,4 điểm, nên khoảng cách này nhiều khả năng có thật chứ không chỉ là nhiễu (chỉ 3 seed, chưa phải chứng minh). Phần lớn khoảng cách nằm ở `nlu_test_v2` (v11 309/314; v13 298, 298, 304), tập mà lớp luật đã được viết dựa vào; trên hai bộ độc lập gộp (128 câu) v11 đúng 126, v13 đúng 125, 125, 128, tức không khác biệt rõ. Giả thuyết (chưa kiểm chứng): luật hiện tại được chỉnh theo kiểu lỗi của v11 nên "cứu" v11 tốt hơn model mới, vốn sai kiểu khác (thu nhập bị đoán thành chi tiêu). Rasa thô của cả ba seed v13 đều tốt hơn v11 và ít "sai mà chắc" hơn (34–40 so với 57). Cổng `promote` so một lần đo với một lần đo nên nhạy với nhiễu cỡ này; muốn quyết định chắc hơn cần so trung bình nhiều seed.
