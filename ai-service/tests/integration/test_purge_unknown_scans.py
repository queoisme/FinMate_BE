"""Dọn hàng đợi bill lạ ``bill_unknown_scans`` quá hạn, trên Postgres thật."""

import sys
from pathlib import Path

from sqlalchemy import text
from sqlalchemy.ext.asyncio import async_sessionmaker

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts"))

from purge_unknown_scans import purge  # noqa: E402

USER = "a" * 64


async def add(session, number: int, age_days: int, status: str = "new") -> None:
    await session.execute(
        text("INSERT INTO bill_unknown_scans (user_id_hash, merchant_key, lines, content_hash, status, last_seen_at) "
             "VALUES (:u, 'quan la', 'x', :h, :s, now() - make_interval(days => :d))"),
        {"u": USER, "h": f"{number:064x}", "s": status, "d": age_days},
    )


async def remaining(session) -> int:
    return (await session.execute(text("SELECT count(*) FROM bill_unknown_scans"))).scalar_one()


async def test_only_scans_not_seen_within_the_retention_are_deleted_whatever_their_status(engine, session):
    for number, (age, status) in enumerate([(100, "new"), (91, "labeled"), (89, "new"), (1, "ignored")]):
        await add(session, number, age, status)
    await session.commit()

    deleted = await purge(async_sessionmaker(engine, expire_on_commit=False), days=90)

    assert deleted == 2 and await remaining(session) == 2


async def test_dry_run_counts_without_deleting_and_batches_cover_everything(engine, session):
    for number in range(5):
        await add(session, number, 200)
    await session.commit()
    factory = async_sessionmaker(engine, expire_on_commit=False)

    assert await purge(factory, days=90, dry_run=True) == 5 and await remaining(session) == 5
    assert await purge(factory, days=90, batch_size=2) == 5 and await remaining(session) == 0
