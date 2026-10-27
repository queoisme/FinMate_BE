"""Vòng dọn dữ liệu quá hạn chạy ngay trong AI Service (``app.services.retention``), trên Postgres thật."""

import asyncio
import uuid

from sqlalchemy import text
from sqlalchemy.ext.asyncio import async_sessionmaker

from app.services import retention

HASH = "a" * 64


def factory_for(engine):
    return async_sessionmaker(engine, expire_on_commit=False)


async def add_log(session, age_days: int) -> None:
    await session.execute(
        text("INSERT INTO ops_analyze_log (backend_request_id, user_id_hash, package_name, received_at, pipeline_result, created_at) "
             "VALUES (gen_random_uuid(), :u, 'p', now(), 'financial', now() - make_interval(days => :d))"), {"u": HASH, "d": age_days})


async def add_raw(session, number: int, age_days: int, source: str = "notification", labeled: bool = False) -> None:
    raw_id = uuid.uuid4()
    await session.execute(
        text("INSERT INTO ml_raw_samples (id, source, package_name, notification_body, received_at, content_hash, created_at) "
             "VALUES (:id, :s, 'p', 'x', now(), :h, now() - make_interval(days => :d))"), {"id": raw_id, "s": source, "h": f"{number:064x}", "d": age_days})
    if labeled:
        await session.execute(text("INSERT INTO ml_labeled_samples (raw_sample_id, is_financial, labeled_by) VALUES (:id, false, 'seed')"), {"id": raw_id})


async def add_scan(session, number: int, age_days: int) -> None:
    await session.execute(
        text("INSERT INTO bill_unknown_scans (user_id_hash, merchant_key, lines, content_hash, last_seen_at) "
             "VALUES (:u, 'quan la', 'x', :h, now() - make_interval(days => :d))"), {"u": HASH, "d": age_days, "h": f"{number:064x}"})


async def count(session, table: str) -> int:
    return (await session.execute(text(f"SELECT count(*) FROM {table}"))).scalar_one()


async def test_analyze_log_older_than_the_retention_is_deleted(engine, session):
    for age in (120, 91, 89, 1):
        await add_log(session, age)
    await session.commit()

    deleted = await retention.purge_analyze_log(factory_for(engine), days=90)

    assert deleted == 2 and await count(session, "ops_analyze_log") == 2


async def test_old_raw_samples_are_deleted_only_when_unlabeled_and_not_seed(engine, session):
    await add_raw(session, 1, 200)                                  # cũ, chưa nhãn: xóa
    await add_raw(session, 2, 200, labeled=True)                    # cũ nhưng đã có nhãn: là dữ liệu huấn luyện, giữ
    await add_raw(session, 3, 200, source="seed")                   # cũ nhưng là mẫu seed: giữ
    await add_raw(session, 4, 5)                                    # mới: giữ
    await session.commit()

    deleted = await retention.purge_raw_samples(factory_for(engine), days=90)

    assert deleted == 1 and await count(session, "ml_raw_samples") == 3


async def test_unknown_scans_are_judged_by_the_last_time_they_were_seen(engine, session):
    await add_scan(session, 1, 100)
    await add_scan(session, 2, 10)
    await session.commit()

    assert await retention.purge_unknown_scans(factory_for(engine), days=90) == 1
    assert await count(session, "bill_unknown_scans") == 1


async def test_dry_run_counts_without_deleting_and_batches_cover_everything(engine, session):
    for age in (200, 200, 200, 200, 200):
        await add_log(session, age)
    await session.commit()
    factory = factory_for(engine)

    assert await retention.purge_analyze_log(factory, days=90, dry_run=True) == 5 and await count(session, "ops_analyze_log") == 5
    assert await retention.purge_analyze_log(factory, days=90, batch_size=2) == 5 and await count(session, "ops_analyze_log") == 0


async def test_one_failing_table_does_not_stop_the_others(engine, session, monkeypatch):
    await add_log(session, 200)
    await add_scan(session, 1, 200)
    await session.commit()

    async def broken(*args, **kwargs):
        raise RuntimeError("DB tạm hỏng")

    monkeypatch.setitem(retention.JOBS, "ml_raw_samples", broken)

    removed = await retention.run_once(factory_for(engine), days=90)

    assert removed == {"ops_analyze_log": 1, "bill_unknown_scans": 1}  # nơi lỗi vắng mặt, hai nơi kia vẫn được dọn
    assert await count(session, "ops_analyze_log") == 0 and await count(session, "bill_unknown_scans") == 0


async def test_the_background_loop_purges_on_its_own_and_stops_when_cancelled(engine, session):
    await add_log(session, 200)
    await session.commit()

    task = asyncio.create_task(retention.retention_loop(factory_for(engine), days=90, interval_seconds=3600, initial_delay_seconds=0))
    for _ in range(50):
        await asyncio.sleep(0.1)
        if await count(session, "ops_analyze_log") == 0:
            break
    task.cancel()

    assert await count(session, "ops_analyze_log") == 0
    try:
        await task
    except asyncio.CancelledError:
        pass
    assert task.cancelled()
