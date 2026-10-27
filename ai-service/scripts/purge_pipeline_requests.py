#!/usr/bin/env python3
"""Dọn nhật ký ``ops_analyze_log`` quá hạn (mặc định 90 ngày), chạy tay.

AI Service tự dọn định kỳ trong tiến trình (``app.services.retention``, bật bằng ``RETENTION_ENABLED``); script này cho người vận hành muốn dọn ngay, đổi số ngày hay xem trước (``--dry-run``).

Bảng này chứa số tiền, mô tả và tên cửa hàng của từng giao dịch nên không được giữ vô hạn. 90 ngày là dư cho mọi nhu cầu còn lại: backend retry/replay idempotent chỉ trong vài phút đến vài giờ, cửa sổ dò trùng là 5 phút, và feedback tra ngược dòng gốc tối đa 30 ngày.
Xóa theo lô để không giữ khóa lâu trên bảng đang được ``/analyze`` ghi liên tục. ``ml_raw_samples`` được liên kết KHÔNG bị đụng tới (đó là dữ liệu huấn luyện).
"""

from __future__ import annotations

import argparse
import asyncio
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))

from app.db.session import AsyncSessionLocal
from app.services.retention import BATCH_SIZE, purge_analyze_log as purge  # noqa: F401  (``purge`` và BATCH_SIZE giữ tên cũ cho test và người gọi)

DEFAULT_RETENTION_DAYS = 90


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--days", type=int, default=DEFAULT_RETENTION_DAYS)
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()
    count = asyncio.run(purge(AsyncSessionLocal, args.days, args.dry_run))
    print(f"{'Sẽ xóa' if args.dry_run else 'Đã xóa'} {count} dòng ops_analyze_log cũ hơn {args.days} ngày.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
