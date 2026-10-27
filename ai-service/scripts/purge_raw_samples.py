#!/usr/bin/env python3
"""Dọn ``ml_raw_samples`` cũ CHƯA được gán nhãn, chạy tay.

AI Service tự dọn định kỳ trong tiến trình (``app.services.retention``, bật bằng ``RETENTION_ENABLED``); script này cho người vận hành muốn dọn ngay, đổi số ngày hay xem trước (``--dry-run``).

Mẫu ĐÃ có nhãn được giữ lại: đó chính là tập dữ liệu huấn luyện, và nội dung trong này đã
qua ``anonymizer`` (không còn số tài khoản/thẻ/điện thoại). Bản gốc chưa che nằm ở
``notification_logs`` phía backend và do ``DataCleanupJob`` xoá sau 90 ngày theo
ARCHITECTURE.md §7.3 — script này không thay thế job đó. Mẫu ``seed`` cũng được giữ.
"""

from __future__ import annotations

import argparse
import asyncio
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))

from app.db.session import AsyncSessionLocal
from app.services.retention import purge_raw_samples

DEFAULT_RETENTION_DAYS = 90


async def purge(days: int, dry_run: bool) -> None:
    count = await purge_raw_samples(AsyncSessionLocal, days, dry_run)
    if dry_run:
        print(f"dry-run: sẽ xoá {count} mẫu cũ hơn {days} ngày")
    else:
        print(f"đã xoá {count} ml_raw_samples cũ hơn {days} ngày (mẫu đã gán nhãn được giữ lại)")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--days", type=int, default=DEFAULT_RETENTION_DAYS)
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()
    asyncio.run(purge(args.days, args.dry_run))


if __name__ == "__main__":
    main()
