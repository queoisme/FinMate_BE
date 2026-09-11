# FinMate AI Service — Python 3.11 FastAPI

Pipeline: Classifier → Extractor → Categorizer → Duplicate Detector.
Chi tiết đầy đủ: xem `../.context/ARCHITECTURE.md` §3, `../.context/TECH_STACK.md` §2.

## Phân vai rule / ML

```
analyze()
 ├ Classifier   ML (scikit-learn, có version)  → chưa promote thì rơi về luật
 ├ Extractor    LUẬT (regex theo provider)     ← không bao giờ dùng ML
 ├ Categorizer  ML (scikit-learn, có version)  → chưa promote thì rơi về từ điển
 └ Duplicate    SQL trên pipeline_requests, cửa sổ 5 phút
```

**Extractor cố ý không dùng ML.** Thông báo ngân hàng là chuỗi template cố định, nên regex
vừa chính xác hơn vừa giải thích được: số tiền ra sai thì `matched_pattern_name` chỉ thẳng
vào dòng `provider_patterns` cần sửa. Một model sinh ra số tiền thì không truy vết được, mà
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
├── api/v1/             # Routes: pipeline, feedback, health
├── pipeline/           # Orchestrator + 4 stages + model registry
├── schemas/            # Pydantic v2 request/response — khớp AIServiceApiContracts.cs
├── db/                 # SQLAlchemy models + repositories (AI DB riêng biệt)
├── core/               # config, security (internal API key), anonymizer, enums
├── utils/              # provider patterns, parser tiền/ngày tiếng Việt
└── data/               # provider_patterns.json, merchant_categories.json (seed)
data/corpus/            # corpus huấn luyện (*.jsonl)
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
`provider_patterns` từ `app/data/provider_patterns.json` lúc khởi động (idempotent, chỉ
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
python scripts/feedback_batch.py   # user_feedback → labeled_samples
```

`scripts/purge_raw_samples.py` dọn mẫu thô cũ chưa gán nhãn — chạy bằng cron ngoài, AI
Service không có scheduler (`celery` bị cấm ở `TECH_STACK.md` §2.3).

## Corpus

`data/corpus/*.jsonl` hiện là dữ liệu **tổng hợp** do `scripts/generate_bootstrap_corpus.py`
sinh ra, để pipeline chạy được end-to-end trước khi có mẫu thật. Model train trên nó đạt
điểm rất cao vì mọi câu sinh từ chính các template mà Extractor đã biết — **con số đó không
nói lên độ chính xác ngoài đời.**

Thay bằng mẫu thật: ghi đè file trong `data/corpus/`, sửa `app/data/provider_patterns.json`
cho khớp template thật, rồi chạy lại `seed_dataset.py` + `train.py`. Không phải sửa code —
regex sống trong bảng `provider_patterns`, đổi là UPDATE dữ liệu chứ không deploy lại.

## Test

```bash
pytest                    # test đơn vị chạy ở mọi nơi
pytest tests/integration  # cần Postgres (docker compose up -d postgres-ai)
black . && isort . && ruff check .
```

Test tích hợp tự tạo một database riêng trên cùng server rồi xoá đi; không có Postgres thì
chúng được SKIP kèm lý do, test đơn vị vẫn chạy.
