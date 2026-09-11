"""Nạp ``app/data/provider_patterns.json`` vào bảng ``provider_patterns``.

Idempotent theo ``(package_name, pattern_name)`` và chỉ THÊM, không bao giờ ghi đè: pattern
đã nằm trong DB có thể đã được người vận hành sửa để khớp template thật của ngân hàng, và
một lần restart container không được phép cuốn trôi bản sửa đó.

Cùng vai trò với ``ProviderConfigSeeder`` bên backend, chạy sau khi migration xong.
"""

from __future__ import annotations

import json
import pathlib

import structlog
from sqlalchemy.ext.asyncio import AsyncSession

from app.db.models.provider_pattern import ProviderPattern
from app.db.repositories import pattern_repo

logger = structlog.get_logger(__name__)

SEED_PATH = (
    pathlib.Path(__file__).resolve().parents[1] / "data" / "provider_patterns.json"
)


async def seed(session: AsyncSession) -> int:
    if not SEED_PATH.exists():
        logger.warning("provider_pattern_seed_missing", path=str(SEED_PATH))
        return 0

    rows = json.loads(SEED_PATH.read_text(encoding="utf-8"))
    existing = await pattern_repo.existing_names(session)

    added = 0
    for row in rows:
        key = (row["package_name"], row["pattern_name"])
        if key in existing:
            continue
        await pattern_repo.add(
            session,
            ProviderPattern(
                provider_key=row["provider_key"],
                package_name=row["package_name"],
                pattern_name=row["pattern_name"],
                regex=row["regex"],
                transaction_type=row.get("transaction_type"),
                priority=row.get("priority", 100),
                sample_text=row.get("sample_text"),
                notes=row.get("notes"),
            ),
        )
        added += 1

    if added:
        await session.commit()
    logger.info("provider_patterns_seeded", added=added, total=len(rows))
    return added
