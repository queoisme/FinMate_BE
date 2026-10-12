"""Bảng nhớ tên món trên Postgres thật: đọc theo lô, ghi một lần (câu trả lời đầu tiên thắng), CHECK chặn slug lạ."""

import pytest
import pytest_asyncio
from sqlalchemy import text
from sqlalchemy.exc import IntegrityError
from sqlalchemy.ext.asyncio import async_sessionmaker

from app.bill.knowledge import DbBillKnowledge


@pytest_asyncio.fixture
async def factory(engine):
    return async_sessionmaker(engine, expire_on_commit=False)


async def test_remembered_items_are_read_back_in_one_batch(factory):
    knowledge = DbBillKnowledge(factory)
    await knowledge.remember_items({"khan uot": "shopping", "banh bao": "food"})
    got = await knowledge.cached_items(["khan uot", "banh bao", "khong co"])
    assert got == {"khan uot": "shopping", "banh bao": "food"}
    assert await knowledge.cached_items([]) == {}


async def test_first_answer_wins_and_hits_are_counted(factory, session):
    knowledge = DbBillKnowledge(factory)
    await knowledge.remember_items({"khan uot": "shopping"})
    await knowledge.remember_items({"khan uot": "food"})
    assert await knowledge.cached_items(["khan uot"]) == {"khan uot": "shopping"}
    hits = (await session.execute(text("SELECT hits FROM bill_item_cache WHERE item_key = 'khan uot'"))).scalar_one()
    assert hits == 2


async def test_check_constraint_rejects_unknown_slug_and_income(session):
    for slug in ("income", "unknown", "nonsense"):
        with pytest.raises(IntegrityError):
            await session.execute(text("INSERT INTO bill_item_cache (item_key, category_slug) VALUES ('x y z', :s)"), {"s": slug})
        await session.rollback()


async def test_database_errors_never_escape(factory):
    class Broken:
        def __call__(self):
            raise RuntimeError("db down")

    knowledge = DbBillKnowledge(Broken())
    assert await knowledge.cached_items(["a b c"]) == {}
    await knowledge.remember_items({"a b c": "food"})
