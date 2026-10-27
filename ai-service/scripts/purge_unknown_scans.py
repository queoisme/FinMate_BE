#!/usr/bin/env python3
"""Dọn hàng đợi bill lạ ``bill_unknown_scans`` quá hạn (mặc định 90 ngày kể từ lần quét cuối), chạy tay.

AI Service tự dọn định kỳ trong tiến trình (``app.services.retention``, bật bằng ``RETENTION_ENABLED``); script này cho người vận hành muốn dọn ngay, đổi số ngày hay xem trước (``--dry-run``).

Bảng này chứa chữ đã lọc của hóa đơn người dùng và mã băm người dùng nên không được giữ vô hạn, kể cả bill chưa ai duyệt (``status = 'new'``): hết hạn mà chưa ai duyệt thì cửa hàng
đó không đủ quan trọng để giữ dữ liệu cá nhân. Bill đang được quét lại thì ``last_seen_at`` mới nên không bị xóa. Xóa theo lô như ``purge_pipeline_requests.py``.
"""

from __future__ import annotations

import argparse
import asyncio
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))

from app.db.session import AsyncSessionLocal
from app.services.retention import BATCH_SIZE, purge_unknown_scans as purge  # noqa: F401  (``purge`` và BATCH_SIZE giữ tên cũ cho test và người gọi)

DEFAULT_RETENTION_DAYS = 90


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--days", type=int, default=DEFAULT_RETENTION_DAYS)
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()
    count = asyncio.run(purge(AsyncSessionLocal, args.days, args.dry_run))
    print(f"{'Sẽ xóa' if args.dry_run else 'Đã xóa'} {count} bill lạ cũ hơn {args.days} ngày.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
