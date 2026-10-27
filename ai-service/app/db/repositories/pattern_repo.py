"""Truy cập ``kb_provider_patterns``."""

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.db.models.provider_format import ProviderFormat
from app.db.models.provider_pattern import ProviderPattern


async def existing_names(session: AsyncSession) -> set[tuple[str, str]]:
    """``(package_name, pattern_name)`` của mọi pattern, mọi phiên bản định dạng: ``seed_patterns`` không thêm lại thứ đã có."""
    result = await session.execute(
        select(ProviderFormat.package_name, ProviderPattern.pattern_name).join(ProviderFormat, ProviderFormat.id == ProviderPattern.format_id)
    )
    return {(row[0], row[1]) for row in result.all()}


async def add(session: AsyncSession, pattern: ProviderPattern) -> ProviderPattern:
    session.add(pattern)
    await session.flush()
    return pattern
