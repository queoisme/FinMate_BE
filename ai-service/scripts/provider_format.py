#!/usr/bin/env python3
"""Quản lý định dạng thông báo theo nhà cung cấp (``kb_provider_formats``) từ dòng lệnh. Cùng việc này có API HTTP (``/api/v1/admin/provider-formats``, ``app/api/v1/provider_formats_admin.py``);
script dùng khi không có backend, và cho việc sửa hàng loạt bằng SQL.

Quy trình sửa một regex mà không đụng bản đang chạy:
  1. ``clone <package_name>``   bản draft mới (version + 1) sao từ bản mới nhất, kèm pattern, trường và mẫu
  2. sửa pattern / trường / mẫu của bản draft bằng SQL
  3. ``check <id>``             liệt kê mọi lý do bản draft chưa đủ điều kiện (không đổi dữ liệu)
  4. ``activate <id>``          chỉ thành công khi MỌI mẫu khớp ``expected_fields``; AI Service nạp lại trong vòng 60 giây
  Sai thì ``archive <id>`` bản mới; bản cũ vẫn active suốt thời gian đó.

``--actor`` là mã băm SHA-256 ghi vào nhật ký ``kb_audit_log`` (mặc định: mã băm của ``cli``).
Ví dụ: ``python scripts/provider_format.py activate 3f1c... --reason "thêm mẫu thông báo mới của MB"``
"""

from __future__ import annotations

import argparse
import asyncio
import pathlib
import sys
import uuid

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))

from sqlalchemy import select

from app.core.anonymizer import sha256_hex
from app.db.models.provider_format import ProviderFormat, ProviderFormatSample
from app.db.models.provider_pattern import ProviderPattern
from app.db.session import AsyncSessionLocal
from app.services import provider_formats
from app.services.provider_formats import FormatError


async def list_formats(session) -> None:
    rows = (await session.execute(select(ProviderFormat).order_by(ProviderFormat.package_name, ProviderFormat.version))).scalars().all()
    for fmt in rows:
        patterns = (await session.execute(select(ProviderPattern).where(ProviderPattern.format_id == fmt.id))).scalars().all()
        samples = (await session.execute(select(ProviderFormatSample.id).where(ProviderFormatSample.format_id == fmt.id))).all()
        print(f"{fmt.id}  {fmt.package_name:<30} v{fmt.version:<3} {fmt.status:<9} ưu tiên {fmt.priority:<4} {len(patterns)} pattern, {len(samples)} mẫu")
    if not rows:
        print("Chưa có định dạng nào.")


async def run(args: argparse.Namespace) -> int:
    actor = args.actor or sha256_hex("cli")
    async with AsyncSessionLocal() as session:
        try:
            if args.command == "list":
                await list_formats(session)
            elif args.command == "check":
                problems = await provider_formats.check_format(session, uuid.UUID(args.format_id))
                print("\n".join(f"- {p}" for p in problems) if problems else "Đủ điều kiện: kích hoạt được.")
                return 1 if problems else 0
            elif args.command == "activate":
                fmt = await provider_formats.activate(session, uuid.UUID(args.format_id), actor_hash=actor, reason=args.reason)
                await session.commit()
                print(f"{fmt.package_name} v{fmt.version}: active. AI Service nạp lại trong vòng 60 giây, không cần restart.")
            elif args.command == "archive":
                fmt = await provider_formats.archive(session, uuid.UUID(args.format_id), actor_hash=actor, reason=args.reason)
                await session.commit()
                print(f"{fmt.package_name} v{fmt.version}: archived.")
            elif args.command == "clone":
                fmt = await provider_formats.clone(session, args.package_name, actor_hash=actor, reason=args.reason)
                await session.commit()
                print(f"{fmt.package_name}: tạo bản draft v{fmt.version} ({fmt.id}). Sửa rồi chạy: check, activate.")
        except FormatError as error:
            await session.rollback()
            print("Không làm được:\n" + "\n".join(f"- {p}" for p in error.problems))
            return 1
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--actor", help="mã băm SHA-256 ghi vào nhật ký (mặc định: mã băm của 'cli')")
    commands = parser.add_subparsers(dest="command", required=True)
    commands.add_parser("list", help="liệt kê mọi định dạng")
    for name in ("check", "activate", "archive"):
        sub = commands.add_parser(name)
        sub.add_argument("format_id")
        if name != "check":
            sub.add_argument("--reason")
    clone = commands.add_parser("clone", help="tạo bản draft mới từ bản mới nhất của package")
    clone.add_argument("package_name")
    clone.add_argument("--reason")
    return asyncio.run(run(parser.parse_args()))


if __name__ == "__main__":
    sys.exit(main())
