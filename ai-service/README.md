# FinMate AI Service — Python 3.11 FastAPI

Pipeline: Classifier → Extractor → Categorizer → Duplicate Detector.
Chi tiết đầy đủ: xem `../.context/ARCHITECTURE.md` §3, `../.context/TECH_STACK.md` §2.

## Cấu trúc

```
app/
├── main.py            # FastAPI entry point
├── api/v1/             # Routes: pipeline, feedback, health
├── pipeline/           # Orchestrator + 4 stages + model registry
├── schemas/            # Pydantic v2 request/response models
├── db/                 # SQLAlchemy models + repositories (AI DB riêng biệt)
├── core/                # config, security (internal API key), anonymizer (SHA-256)
└── utils/               # provider patterns, VND amount parsing
```

AI DB (`finmate_ai`) hoàn toàn tách biệt với Backend DB — không FK, chỉ dùng `_hash` fields để tham chiếu (xem `../.context/ARCHITECTURE.md` §4.3).

## Trạng thái

Chưa scaffold code — xem `../.context/TASKS.md` Phase 0 và Phase 9 để bắt đầu (tạo FastAPI structure, Alembic, structlog, internal API key middleware).
