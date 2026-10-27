"""Bộ đếm mức dùng trên Postgres thật: cộng dồn theo ngày và theo chỉ số, và chỉ chứa số đếm."""

from sqlalchemy import text
from sqlalchemy.ext.asyncio import async_sessionmaker

from app.bill.classify_stats import ClassifyStatsRecorder


async def values(session):
    rows = (await session.execute(text("SELECT metric, value, day = current_date AS today FROM ops_usage_daily ORDER BY metric"))).all()
    return {r.metric: r.value for r in rows}, all(r.today for r in rows)


async def test_counts_accumulate_across_requests_for_the_same_day(session, engine):
    recorder = ClassifyStatsRecorder(async_sessionmaker(engine, expire_on_commit=False))
    await recorder.record({"requests": 1, "items": 4, "src_ai": 3, "src_cache": 1})
    await recorder.record({"requests": 1, "items": 2, "src_cache": 2, "ai_failed": 1})

    got, same_day = await values(session)

    assert got == {"requests": 2, "items": 6, "src_ai": 3, "src_cache": 3, "ai_failed": 1}
    assert same_day


async def test_the_table_has_only_day_metric_and_value_columns(session):
    columns = (await session.execute(text("SELECT column_name FROM information_schema.columns WHERE table_name = 'ops_usage_daily' ORDER BY 1"))).scalars().all()
    assert columns == ["day", "metric", "value"]  # không có chỗ nào để lọt tên món, cửa hàng hay người dùng
