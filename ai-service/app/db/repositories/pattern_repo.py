"""Truy cập ``provider_patterns``."""

from collections.abc import Sequence

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.db.models.provider_pattern import ProviderPattern


async def list_active(
    session: AsyncSession, package_name: str | None = None
) -> Sequence[ProviderPattern]:
    """Pattern đang bật, sắp theo ``priority`` tăng dần.

    Thứ tự quan trọng: template hẹp phải được thử trước template rộng, nếu không template
    rộng khớp trước và các named group chi tiết không bao giờ được bóc ra.
    """
    stmt = select(ProviderPattern).where(ProviderPattern.is_active.is_(True))
    if package_name is not None:
        stmt = stmt.where(ProviderPattern.package_name == package_name)
    stmt = stmt.order_by(ProviderPattern.priority, ProviderPattern.pattern_name)
    result = await session.execute(stmt)
    return result.scalars().all()


async def list_all(session: AsyncSession) -> Sequence[ProviderPattern]:
    result = await session.execute(
        select(ProviderPattern).order_by(
            ProviderPattern.package_name, ProviderPattern.priority
        )
    )
    return result.scalars().all()


async def existing_names(session: AsyncSession) -> set[tuple[str, str]]:
    result = await session.execute(
        select(ProviderPattern.package_name, ProviderPattern.pattern_name)
    )
    return {(row[0], row[1]) for row in result.all()}


async def add(session: AsyncSession, pattern: ProviderPattern) -> ProviderPattern:
    session.add(pattern)
    await session.flush()
    return pattern
