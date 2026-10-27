"""Fixture cần Postgres thật.

Nằm riêng ở thư mục integration chứ không ở conftest gốc: fixture dọn bảng là ``autouse``, mà
autouse ở conftest gốc sẽ kéo MỌI test — kể cả test đơn vị thuần — vào phụ thuộc database, và
trên máy không có Postgres thì cả bộ test bị SKIP thay vì chỉ phần tích hợp.

Dùng thẳng server Postgres của ``docker compose``, tạo một database riêng cho phiên test rồi
xoá đi — không thêm dependency (``testcontainers`` Python không có trong TECH_STACK.md §2.2).
Lý do cần Postgres thật giống hệt lý do backend dùng Testcontainers: CHECK constraint, partial
unique index và TIMESTAMPTZ chỉ tồn tại ở Postgres.
"""

from __future__ import annotations

import asyncio
import json
import uuid
from collections.abc import AsyncGenerator

import pytest
import pytest_asyncio
from sqlalchemy import text
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker, create_async_engine
from sqlalchemy.pool import NullPool

from app.db.category_seed import seed_categories
from app.db import audit_sql, category_guard, ml_sql
from app.db.model_seed import seed_models
from app.db.models.all_models import Base
from app.db.seed_patterns import SEED_PATH

TEST_DB_NAME = f"finmate_ai_test_{uuid.uuid4().hex[:8]}"


def pattern_rows() -> list[dict]:
    return json.loads(SEED_PATH.read_text(encoding="utf-8"))


def _admin_url(url: str) -> str:
    base, _, _ = url.rpartition("/")
    return f"{base}/postgres"


def _test_url(url: str) -> str:
    base, _, _ = url.rpartition("/")
    return f"{base}/{TEST_DB_NAME}"


async def _create_database(admin_url: str, test_url: str) -> None:
    admin = create_async_engine(
        admin_url, isolation_level="AUTOCOMMIT", poolclass=NullPool
    )
    try:
        async with admin.connect() as connection:
            await connection.execute(text(f'CREATE DATABASE "{TEST_DB_NAME}"'))
    finally:
        await admin.dispose()

    engine = create_async_engine(test_url, poolclass=NullPool)
    try:
        async with engine.begin() as connection:
            # create_all thay vì chạy alembic: bước verify đã có `alembic check` bảo đảm
            # migration khớp model, nên ở đây chỉ cần schema đúng và dựng nhanh.
            await connection.run_sync(Base.metadata.create_all)
            # kb_categories là bảng cha của mọi khóa ngoại category_slug; clean_tables không xóa nó nên chỉ cần seed một lần.
            await seed_categories(connection)
            # ml_models là bảng cha của phiên bản/lần huấn luyện; clean_tables không xóa nó nên cũng chỉ seed một lần.
            await seed_models(connection)
            # create_all không tạo view và trigger: dựng chúng như migration 0024 để mọi test chạy trên cùng schema với DB thật.
            for statement in (*category_guard.OBJECTS, *ml_sql.OBJECTS, *audit_sql.OBJECTS):
                await connection.execute(text(statement))
    finally:
        await engine.dispose()


async def _drop_database(admin_url: str) -> None:
    admin = create_async_engine(
        admin_url, isolation_level="AUTOCOMMIT", poolclass=NullPool
    )
    try:
        async with admin.connect() as connection:
            await connection.execute(
                text(f'DROP DATABASE IF EXISTS "{TEST_DB_NAME}" WITH (FORCE)')
            )
    finally:
        await admin.dispose()


@pytest.fixture(scope="session")
def database_url() -> str:
    """Tạo một database riêng cho cả phiên test rồi xoá đi.

    Fixture này CỐ Ý đồng bộ và tự gọi ``asyncio.run``: pytest-asyncio chạy mỗi test trong
    một event loop mới, nên một AsyncEngine tạo ở fixture session-scope sẽ mang connection
    thuộc loop khác và nổ "attached to a different loop" ngay ở test thứ hai. Ở đây mọi
    engine được tạo và dispose trọn vẹn bên trong một loop dùng một lần.
    """
    from app.core.config import settings

    admin_url = _admin_url(settings.database_url)
    test_url = _test_url(settings.database_url)

    try:
        asyncio.run(_create_database(admin_url, test_url))
    except Exception as error:  # noqa: BLE001
        pytest.skip(f"Không kết nối được Postgres cho test tích hợp: {error}")

    yield test_url

    asyncio.run(_drop_database(admin_url))


@pytest_asyncio.fixture
async def engine(database_url: str):
    """Engine mới cho mỗi test, gắn đúng event loop của test đó."""
    test_engine = create_async_engine(database_url, poolclass=NullPool)
    yield test_engine
    await test_engine.dispose()


@pytest_asyncio.fixture(autouse=True)
async def clean_tables(engine):
    """Mỗi test bắt đầu từ bảng rỗng — dò trùng và idempotency phụ thuộc vào dữ liệu đã
    có, nên rò rỉ giữa các test sẽ tạo lỗi phụ thuộc thứ tự chạy."""
    async with engine.begin() as connection:
        await connection.execute(
            text(
                "TRUNCATE ml_user_feedback, ml_item_feedback, ml_feedback_batches, ml_evaluation_predictions,"
                " ml_evaluation_category_metrics, ml_evaluation_runs, ml_model_versions,"
                " ml_training_jobs, ml_sample_splits, ml_labeled_samples, ops_analyze_log,"
                " ml_raw_samples, kb_provider_formats, kb_provider_patterns, bill_unknown_scans,"
                " user_merchant_memory, kb_item_categories, ml_training_samples, ops_usage_daily, kb_audit_log, kb_keywords, kb_brands, nlu_model_versions, nlu_candidates, nlu_examples RESTART IDENTITY CASCADE"
            )
        )
    yield


@pytest_asyncio.fixture
async def session(engine) -> AsyncGenerator[AsyncSession, None]:
    factory = async_sessionmaker(engine, expire_on_commit=False)
    async with factory() as db:
        yield db


@pytest_asyncio.fixture
async def seeded_session(session: AsyncSession) -> AsyncSession:
    """Session đã có sẵn pattern (qua đúng đường seed của ứng dụng: định dạng active + trường + mẫu) — Extractor không có pattern thì vô dụng."""
    from app.db.seed_patterns import seed

    await seed(session)
    return session


@pytest_asyncio.fixture
async def client(engine):
    """FastAPI client với get_db trỏ vào database test."""
    import httpx

    from app.api.deps import get_db
    from app.db.seed_patterns import seed
    from app.main import app
    from app.pipeline.models.model_registry import registry
    from app.pipeline.orchestrator import pipeline

    factory = async_sessionmaker(engine, expire_on_commit=False)

    async def override_get_db() -> AsyncGenerator[AsyncSession, None]:
        async with factory() as db:
            yield db

    app.dependency_overrides[get_db] = override_get_db

    # Registry và matcher là singleton dùng chung cả tiến trình — nạp lại từ DB test để
    # một test không thừa hưởng model/pattern của test trước.
    registry.reset()
    async with factory() as db:
        await seed(db)
        await pipeline.refresh(db, force=True)

    transport = httpx.ASGITransport(app=app)
    async with httpx.AsyncClient(
        transport=transport, base_url="http://ai-test"
    ) as http:
        yield http

    app.dependency_overrides.clear()


@pytest.fixture
def auth_headers() -> dict[str, str]:
    from app.core.config import settings

    return {"Authorization": f"Bearer {settings.internal_api_key}"}
