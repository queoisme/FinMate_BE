"""Fixture dùng chung.

Test tích hợp cần Postgres thật (cùng lý do backend dùng Testcontainers thay vì in-memory:
CHECK constraint, partial unique index và kiểu TIMESTAMPTZ chỉ tồn tại ở Postgres). Không
thêm dependency mới — ``testcontainers`` không có trong TECH_STACK.md §2.2 — mà dùng thẳng
server đã có trong ``docker compose``, tạo một database riêng cho test rồi xoá đi.

Không có server nào chạy thì test tích hợp được SKIP kèm lý do, còn test đơn vị (phần lớn
bộ test) vẫn chạy bình thường.
"""

from __future__ import annotations

import asyncio
import json
import pathlib
import uuid
from collections.abc import AsyncGenerator
from datetime import datetime

import pytest
import pytest_asyncio
from sqlalchemy import text
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker, create_async_engine
from sqlalchemy.pool import NullPool

from app.core.anonymizer import sha256_hex
from app.db.models.all_models import Base
from app.db.models.provider_pattern import ProviderPattern
from app.db.seed_patterns import SEED_PATH
from app.utils.provider_patterns import PatternMatcher, PatternSpec
from app.utils.text_utils import VIETNAM_TZ

CORPUS_DIR = pathlib.Path(__file__).resolve().parents[1] / "data" / "corpus"
TEST_DB_NAME = f"finmate_ai_test_{uuid.uuid4().hex[:8]}"


def pattern_rows() -> list[dict]:
    return json.loads(SEED_PATH.read_text(encoding="utf-8"))


@pytest.fixture(scope="session")
def pattern_specs() -> list[PatternSpec]:
    return [
        PatternSpec(**{k: v for k, v in row.items() if k != "notes"})
        for row in pattern_rows()
    ]


@pytest.fixture(scope="session")
def matcher(pattern_specs: list[PatternSpec]) -> PatternMatcher:
    return PatternMatcher(pattern_specs)


@pytest.fixture(scope="session")
def corpus() -> list[dict]:
    rows: list[dict] = []
    for path in sorted(CORPUS_DIR.glob("*.jsonl")):
        for line in path.read_text(encoding="utf-8").splitlines():
            if line.strip():
                rows.append(json.loads(line))
    if not rows:
        pytest.fail(f"Corpus rỗng: {CORPUS_DIR}")
    return rows


@pytest.fixture(scope="session")
def now() -> datetime:
    return datetime(2026, 9, 9, 10, 30, tzinfo=VIETNAM_TZ)


@pytest.fixture
def user_hash() -> str:
    return sha256_hex(str(uuid.uuid4()))


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
                "TRUNCATE user_feedback, feedback_batch_jobs, evaluation_predictions,"
                " evaluation_category_metrics, evaluation_runs, model_versions,"
                " training_jobs, sample_splits, labeled_samples, pipeline_requests,"
                " raw_samples, provider_patterns RESTART IDENTITY CASCADE"
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
    """Session đã có sẵn provider_patterns — Extractor không có pattern thì vô dụng."""
    for row in pattern_rows():
        session.add(
            ProviderPattern(
                provider_key=row["provider_key"],
                package_name=row["package_name"],
                pattern_name=row["pattern_name"],
                regex=row["regex"],
                transaction_type=row.get("transaction_type"),
                priority=row.get("priority", 100),
                sample_text=row.get("sample_text"),
            )
        )
    await session.commit()
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
