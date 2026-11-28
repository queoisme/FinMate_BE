# FinMate AI Service — Python 3.11 FastAPI

Pipeline: Classifier → Extractor → Categorizer → Duplicate Detector.
Chi tiết đầy đủ: xem `../.context/ARCHITECTURE.md` §3, `../.context/TECH_STACK.md` §2.

## Phân vai rule / ML

```
analyze()
 ├ Classifier   ML (scikit-learn, có version)  → chưa promote thì rơi về luật
 ├ Extractor    LUẬT (regex theo provider)     ← không bao giờ dùng ML
 ├ Categorizer  ML (scikit-learn, có version)  → chưa promote thì rơi về từ điển
 └ Duplicate    SQL trên ops_analyze_log, cửa sổ 5 phút — chỉ là gợi ý
```

Từ Phase 82, **backend** quyết định bản trùng (`DuplicateMatcher`); `is_potential_duplicate` của
ai-service chỉ còn dùng để xếp hạng khi hoà, không làm rơi thông báo nào.

**Extractor cố ý không dùng ML.** Thông báo ngân hàng là chuỗi template cố định, nên regex
vừa chính xác hơn vừa giải thích được: số tiền ra sai thì `matched_pattern_name` chỉ thẳng
vào dòng `kb_provider_patterns` cần sửa. Một model sinh ra số tiền thì không truy vết được, mà
sai số tiền là loại lỗi đắt nhất trong ứng dụng này.

ML đứng ở hai chỗ đầu vào mở: *có phải thông báo tài chính không* và *merchant này thuộc
danh mục nào*. Cả hai nạp qua `model_registry` theo version, nên thay bằng PhoBERT sau này
chỉ cần một lớp hiện thực `StageModel` và một artifact mới — không stage nào phải sửa.

**Chưa promote model nào là trạng thái hợp lệ**, không phải lỗi: pipeline chạy hoàn toàn
bằng luật. `GET /api/v1/health` cho biết stage nào đang có model.

## Cấu trúc

```
app/
├── main.py             # FastAPI entry point + lifespan (seed pattern, nạp model)
├── api/v1/             # Routes: pipeline thông báo, chat, bill/OCR, ocr-store (cho ocr-service),
│                       #   agent + kho tri thức, market-data, finance-methods, stats, feedback,
│                       #   các route quản trị (*_admin) và nội bộ (internal_*)
├── pipeline/           # Orchestrator + 4 stages + model registry
├── agent/              # Trợ lý: planner, composer, verifier, RAG
├── bill/               # Phân loại hoá đơn và từng món (luật, bộ nhớ, Gemini)
├── services/           # Đọc số tiền/ngày tiếng Việt, intent classifier, parser chat, tài liệu cấu hình có phiên bản,
│                       #   chọn phương pháp tài chính, đọc tin thị trường, kho khoá bí mật
├── schemas/            # Pydantic v2 request/response — khớp AIServiceApiContracts.cs
├── db/                 # SQLAlchemy models + repositories (AI DB riêng biệt)
├── core/               # config, security (internal API key), anonymizer, enums
├── utils/              # provider patterns, parser tiền/ngày tiếng Việt
└── data/               # seed: provider_patterns.json, merchant_categories.json, từ vựng, ngưỡng
data/corpus/            # corpus huấn luyện (*.jsonl)
data/intent/            # mẫu câu cho intent classifier của chat (train trong tiến trình)
dictionaries/           # từ điển câu chat theo danh mục (bản chạy thật nằm trong config_documents)
agent_eval/             # bộ đo trợ lý (dataset, chỉ số)
models/                 # artifact model (không commit — sinh bởi scripts/train.py)
```

AI DB (`finmate_ai`) hoàn toàn tách biệt với Backend DB — không FK, chỉ dùng `_hash` fields
để tham chiếu (xem `../.context/ARCHITECTURE.md` §4.3).

## Chạy

```bash
cp .env.example .env
pip install -r requirements.txt          # runtime
pip install -r requirements-training.txt # thêm torch/transformers, chỉ khi train

alembic upgrade head
uvicorn app.main:app --reload
```

Trong `docker compose`, container tự chạy `alembic upgrade head` trước `uvicorn`, và tự seed
`kb_provider_patterns` từ `app/data/provider_patterns.json` lúc khởi động (idempotent, chỉ
THÊM — pattern đã sửa tay trong DB không bị ghi đè).

## Vòng đời model

```bash
python scripts/seed_dataset.py                                  # corpus → AI DB
python scripts/train.py --stage classifier                      # → candidate
python scripts/evaluate.py --stage classifier --version 1.0.0   # → điểm trên split test
python scripts/promote.py --stage classifier --version 1.0.0    # → active
```

`promote.py` từ chối model chưa được chấm trên split `test` hoặc có accuracy dưới 0.70.
AI Service nhận model mới trong vòng 60 giây, không cần restart.

Phản hồi người dùng quay về dataset:

```bash
python scripts/feedback_batch.py   # ml_user_feedback → labeled_samples
```

**Dọn dữ liệu chạy tự động trong tiến trình**, không cần cron ngoài (`app/main.py` khởi động hai vòng lặp lúc start):

- `retention_loop` (`app/services/retention.py`): xoá `ops_analyze_log`, mẫu thô chưa nhãn (`ml_raw_samples`) và hàng đợi bill lạ
  (`bill_unknown_scans`) quá `RETENTION_DAYS` ngày (mặc định 90), mỗi `RETENTION_INTERVAL_HOURS` giờ (mặc định 24), lần đầu sau 5 phút.
  Bật mặc định; `RETENTION_ENABLED=false` tắt vòng lặp này, dành cho triển khai muốn tự chạy bằng cron ngoài.
- `purge_loop` (`app/bill/samples.py`): xoá mẫu huấn luyện chưa nhãn quá 7 ngày, mỗi giờ. Luôn chạy, không có biến tắt.

Các script `purge_*` gọi đúng các hàm trên và chỉ để **chạy tay** (`--dry-run`, đổi `--days`) hoặc cho triển khai đã đặt
`RETENTION_ENABLED=false`, ví dụ `0 4 * * * cd /app && python scripts/purge_pipeline_requests.py`. Đừng đặt cron cho chúng khi vòng lặp
trong tiến trình đang bật: chỉ là việc trùng lặp phải bảo trì. `purge_training_samples` không bao giờ cần cron vì `purge_loop` luôn chạy.

`scripts/provider_format.py` quản lý phiên bản định dạng thông báo của từng ngân hàng (`list`, `clone <package>`, `check <id>`, `activate <id>`, `archive <id>`):
sửa regex bằng cách nhân bản bản mới nhất thành draft, sửa, `check` rồi `activate`; chỉ kích hoạt được khi mọi mẫu của định dạng khớp. Bản đang chạy không bị đụng tới.

## Corpus

`data/corpus/*.jsonl` hiện là dữ liệu **tổng hợp** do `scripts/generate_bootstrap_corpus.py`
sinh ra, để pipeline chạy được end-to-end trước khi có mẫu thật. Model train trên nó đạt
điểm rất cao vì mọi câu sinh từ chính các template mà Extractor đã biết — **con số đó không
nói lên độ chính xác ngoài đời.**

Thay bằng mẫu thật: ghi đè file trong `data/corpus/`, sửa `app/data/provider_patterns.json`
cho khớp template thật, rồi chạy lại `seed_dataset.py` + `train.py`. Không phải sửa code —
regex sống trong bảng `kb_provider_patterns`, đổi là UPDATE dữ liệu chứ không deploy lại.

## Script

| Nhóm | Script |
|---|---|
| Vòng đời model | `seed_dataset`, `train`, `evaluate`, `promote`, `feedback_batch`, `train_bill_model`, `generate_bootstrap_corpus` |
| Dataset và học từ phản hồi | `export_category_dataset`, `learn_items`, `promote_item_consensus`, `learning_report`, `classify_stats` |
| Đánh giá | `eval_intent` (intent classifier của chat), `eval_money`, `eval_goal`, `eval_flows`, `eval_chat_live`, `eval_voice_text`, `eval_ocr_synthetic`, `eval_category_dataset` |
| Dọn dữ liệu (chạy tay; tự động đã có trong tiến trình, xem trên) | `purge_raw_samples`, `purge_pipeline_requests`, `purge_training_samples`, `purge_unknown_scans` |
| Định dạng thông báo | `provider_format` |

## Test

```bash
pytest                    # test đơn vị chạy ở mọi nơi
pytest tests/integration  # cần Postgres (docker compose up -d postgres-ai); test kho tri thức cần pgvector
black . && isort . && ruff check .
```

Test tích hợp tự tạo một database riêng trên cùng server rồi xoá đi; không có Postgres thì
chúng được SKIP kèm lý do, test đơn vị vẫn chạy.
