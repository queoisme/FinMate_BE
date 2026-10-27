#!/usr/bin/env python3
"""Dọn mẫu huấn luyện CHƯA gắn nhãn quá hạn (lần quét không bao giờ được lưu).

Mẫu ĐÃ có nhãn được giữ: đó là dữ liệu huấn luyện, và người dùng có thể yêu cầu xóa theo tài khoản (``app.bill.samples.delete_by_user_hash``).
Chạy bằng cron ngoài (AI Service không có scheduler): ``0 4 * * * cd /app && python scripts/purge_training_samples.py --days 7``
"""

from __future__ import annotations

import argparse
import asyncio
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))

from sqlalchemy import text

from app.bill import samples
from app.db.session import AsyncSessionLocal

DEFAULT_RETENTION_DAYS = 7


async def run(days: int, dry_run: bool) -> int:
    async with AsyncSessionLocal() as session:
        if dry_run:
            return (
                await session.execute(
                    text("SELECT count(*) FROM ml_training_samples WHERE final_slug IS NULL AND created_at < now() - make_interval(days => :days)"),
                    {"days": days},
                )
            ).scalar_one()
        return await samples.purge_pending(session, days=days)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--days", type=int, default=DEFAULT_RETENTION_DAYS)
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()
    count = asyncio.run(run(args.days, args.dry_run))
    print(f"{'Sẽ xóa' if args.dry_run else 'Đã xóa'} {count} mẫu chưa gắn nhãn cũ hơn {args.days} ngày.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
