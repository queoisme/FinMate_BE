"""Seed bản mặc định của thương hiệu/từ khóa vào Postgres thật: chỉ thêm, chạy lại không đổi gì, DB luôn thắng (đã sửa, đã tắt, đã chuyển nhóm)."""

import pytest_asyncio
from sqlalchemy import text
from sqlalchemy.ext.asyncio import async_sessionmaker

from app.bill import seed_defaults


@pytest_asyncio.fixture
async def factory(engine):
    return async_sessionmaker(engine, expire_on_commit=False)


async def run_seed(factory) -> dict:
    async with factory() as session:
        return await seed_defaults.seed_bill_knowledge(session)


async def scalar(factory, sql: str, **params):
    async with factory() as session:
        return (await session.execute(text(sql), params)).scalar_one()


async def execute(factory, sql: str, **params):
    async with factory() as session:
        await session.execute(text(sql), params)
        await session.commit()


async def test_seeding_an_empty_database_adds_every_default_and_running_again_adds_nothing(factory):
    first = await run_seed(factory)
    assert first["brands"] == len(seed_defaults.default_brands()) > 300
    assert first["keywords"] == len(seed_defaults.default_keywords()) > 300
    assert await scalar(factory, "SELECT count(*) FROM bill_brands WHERE source = 'seed' AND is_active") == first["brands"]
    assert await run_seed(factory) == {"brands": 0, "keywords": 0}


async def test_the_database_wins_over_the_defaults_for_edited_switched_off_and_moved_entries(factory):
    await run_seed(factory)
    await execute(factory, "UPDATE bill_brands SET category_slug = 'shopping' WHERE brand_key = 'highlands coffee'")
    await execute(factory, "UPDATE bill_brands SET is_active = false WHERE brand_key = 'netflix'")
    # từ khóa chuyển nhóm (như migration 0008/0012): không được thêm lại ở nhóm cũ
    await execute(factory, "UPDATE bill_keywords SET category_slug = 'shopping' WHERE kind = 'product' AND keyword = 'bun bo'")
    assert await run_seed(factory) == {"brands": 0, "keywords": 0}
    assert await scalar(factory, "SELECT category_slug FROM bill_brands WHERE brand_key = 'highlands coffee'") == "shopping"
    assert await scalar(factory, "SELECT is_active FROM bill_brands WHERE brand_key = 'netflix'") is False
    assert await scalar(factory, "SELECT count(*) FROM bill_keywords WHERE kind = 'product' AND keyword = 'bun bo'") == 1


async def test_a_default_added_to_the_code_later_reaches_a_database_seeded_earlier(factory):
    await run_seed(factory)
    await execute(factory, "DELETE FROM bill_brands WHERE brand_key = 'vexere'")
    await execute(factory, "DELETE FROM bill_keywords WHERE kind = 'merchant' AND keyword = 'garage'")
    assert await run_seed(factory) == {"brands": 1, "keywords": 1}
    assert await scalar(factory, "SELECT source FROM bill_brands WHERE brand_key = 'vexere'") == "seed"
    assert await scalar(factory, "SELECT category_slug FROM bill_keywords WHERE kind = 'merchant' AND keyword = 'garage'") == "transport"
