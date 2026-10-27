#!/usr/bin/env python3
"""Duyệt câu chat chưa rõ mà người dùng (đã đồng ý) gửi về: gán ý định rồi mới thành câu huấn luyện Rasa.

Chạy từ ``ai-service`` (cần DB; từ máy chủ đặt ``DATABASE_URL`` trỏ ``localhost:5434``):
    python scripts/nlu_candidates.py list [--status pending|rejected] [--limit 50]
    python scripts/nlu_candidates.py accept <id> --intent add_expense [--text "câu đã bỏ tên người"]
    python scripts/nlu_candidates.py reject <id>
    python scripts/nlu_candidates.py stats

Cột "máy đoán" chỉ để tham khảo; nhãn là ý định BẠN chọn. Sửa ``--text`` để bỏ tên người, địa chỉ trước khi nhận (cú pháp thực thể ``[45k](amount)`` được phép).
Câu được nhận vào ``nlu_examples`` (``source=user_feedback``, ``tag=user_feedback``) và xóa khỏi bảng chờ; ``python scripts/nlu_model.py train`` mới học từ nó.
"""
from __future__ import annotations

import argparse
import asyncio
import collections
import pathlib
import sys
import uuid

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))

from sqlalchemy import select  # noqa: E402

from app.core.enums import NluCandidateStatus  # noqa: E402
from app.db.models.nlu_candidate import NluCandidate  # noqa: E402
from app.db.session import AsyncSessionLocal  # noqa: E402
from app.services import nlu_candidates  # noqa: E402


async def cmd_list(args: argparse.Namespace) -> int:
    async with AsyncSessionLocal() as session:
        rows = await nlu_candidates.list_candidates(session, args.status, args.limit)
    if not rows:
        print("Không có câu nào.")
    for r in rows:
        guess = f"{r.predicted_intent} {r.predicted_confidence:.2f}" if r.predicted_intent and r.predicted_confidence is not None else "-"
        print(f"{r.id}  {r.unclear_reason:15} máy đoán: {guess:22} {r.text}")
    return 0


async def cmd_accept(args: argparse.Namespace) -> int:
    async with AsyncSessionLocal() as session:
        outcome = await nlu_candidates.accept(session, uuid.UUID(args.id), args.intent, args.text)
    print(outcome)
    return 0 if outcome == "accepted" else 1


async def cmd_reject(args: argparse.Namespace) -> int:
    async with AsyncSessionLocal() as session:
        done = await nlu_candidates.reject(session, uuid.UUID(args.id))
    print("Đã loại." if done else "Không có ứng viên với id này.")
    return 0 if done else 1


async def cmd_stats(_: argparse.Namespace) -> int:
    async with AsyncSessionLocal() as session:
        rows = (await session.scalars(select(NluCandidate))).all()
    count = collections.Counter((r.status, r.unclear_reason) for r in rows)
    print(f"{len(rows)} câu")
    for (status, reason), n in sorted(count.items()):
        print(f"  {status:9} {reason:15} {n}")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="cmd", required=True)
    p = sub.add_parser("list")
    p.add_argument("--status", default=NluCandidateStatus.PENDING.value, choices=NluCandidateStatus.values())
    p.add_argument("--limit", type=int, default=50)
    p = sub.add_parser("accept")
    p.add_argument("id")
    p.add_argument("--intent", required=True)
    p.add_argument("--text")
    sub.add_parser("reject").add_argument("id")
    sub.add_parser("stats")
    args = parser.parse_args()
    return asyncio.run({"list": cmd_list, "accept": cmd_accept, "reject": cmd_reject, "stats": cmd_stats}[args.cmd](args))


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.exit(main())
