#!/usr/bin/env python3
"""Dọn ``raw_samples`` cũ CHƯA được gán nhãn.

Mẫu ĐÃ có nhãn được giữ lại: đó chính là tập dữ liệu huấn luyện, và nội dung trong này đã
qua ``anonymizer`` (không còn số tài khoản/thẻ/điện thoại). Bản gốc chưa che nằm ở
``notification_logs`` phía backend và do ``DataCleanupJob`` xoá sau 90 ngày theo
ARCHITECTURE.md §7.3 — script này không thay thế job đó.

AI Service không có scheduler (``celery`` bị cấm ở TECH_STACK.md §2.3) nên chạy bằng cron
ngoài: ``0 4 * * * cd /src && python scripts/purge_raw_samples.py``
"""

from __future__ import annotations

import argparse
import asyncio
import pathlib
import sys
from datetime import UTC, datetime, timedelta

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))

from sqlalchemy import delete, select

from app.core.enums import SampleSource
from app.db.models.labeled_sample import LabeledSample
from app.db.models.raw_sample import RawSample
from app.db.session import AsyncSessionLocal

DEFAULT_RETENTION_DAYS = 90


async def purge(days: int, dry_run: bool) -> None:
    cutoff = datetime.now(UTC) - timedelta(days=days)

    async with AsyncSessionLocal() as session:
        labeled = select(LabeledSample.raw_sample_id)
        condition = (
            RawSample.created_at < cutoff,
            RawSample.source != SampleSource.SEED.value,
            RawSample.id.not_in(labeled),
        )

        count = len(
            (await session.execute(select(RawSample.id).where(*condition))).all()
        )
        if dry_run:
            print(f"dry-run: sẽ xoá {count} mẫu cũ hơn {days} ngày")
            return

        await session.execute(delete(RawSample).where(*condition))
        await session.commit()

    print(
        f"đã xoá {count} raw_samples cũ hơn {days} ngày (mẫu đã gán nhãn được giữ lại)"
    )


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--days", type=int, default=DEFAULT_RETENTION_DAYS)
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()
    asyncio.run(purge(args.days, args.dry_run))


if __name__ == "__main__":
    main()
