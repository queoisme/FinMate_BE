#!/usr/bin/env python3
"""Đưa món đủ phiếu của người dùng vào bảng nhớ tên món ``kb_item_categories`` (nguồn ``consensus``).

Đọc view ``v_item_consensus`` (phiếu ở ``ml_item_feedback``, mỗi người một phiếu cho mỗi món). Một món được thêm khi có từ ``--min-users`` người KHÁC NHAU chọn cùng một
danh mục và danh mục đó chiếm từ ``--min-agreement`` tổng số phiếu. Mặc định CHỈ XEM TRƯỚC; thêm ``--write`` mới ghi. Chỉ thêm món mới: dòng đã có (Gemini, quản trị viên,
lần đồng thuận trước) không bao giờ bị ghi đè; nếu đa số người dùng chọn khác thì món được liệt kê ở "bất đồng" để người duyệt quyết. Mỗi món thêm ghi một dòng ``kb_audit_log``.

AI Service nạp lại bảng nhớ trong vòng 60 giây, không cần restart.
Ví dụ: ``python scripts/promote_item_consensus.py`` (xem trước) rồi ``python scripts/promote_item_consensus.py --write``.
"""

from __future__ import annotations

import argparse
import asyncio
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))

from app.bill import item_votes
from app.core.anonymizer import sha256_hex
from app.db.session import AsyncSessionLocal


async def run(args: argparse.Namespace) -> int:
    actor = args.actor or sha256_hex("cli")
    async with AsyncSessionLocal() as session:
        result = await item_votes.promote_consensus(
            session, actor_hash=actor, min_users=args.min_users, min_agreement=args.min_agreement, write=args.write
        )
    verb = "Đã thêm" if args.write else "Sẽ thêm"
    print(f"{verb} {len(result.promoted)} món vào kb_item_categories; bất đồng với dòng đã có: {len(result.disputes)}; chưa đủ ngưỡng: {len(result.pending)}.")
    for key in result.promoted:
        print(f"  + {key}")
    for key in result.disputes:
        print(f"  ! bất đồng: {key}")
    if not args.write and result.promoted:
        print("Chạy lại với --write để ghi.")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--min-users", type=int, default=item_votes.DEFAULT_MIN_USERS)
    parser.add_argument("--min-agreement", type=float, default=item_votes.DEFAULT_MIN_AGREEMENT)
    parser.add_argument("--write", action="store_true", help="ghi vào DB (mặc định chỉ xem trước)")
    parser.add_argument("--actor", help="mã băm SHA-256 ghi vào kb_audit_log (mặc định: mã băm của 'cli')")
    return asyncio.run(run(parser.parse_args()))


if __name__ == "__main__":
    sys.exit(main())
