"""Tập slug danh mục hợp lệ LÚC CHẠY, đọc từ ``kb_categories`` (nguồn: backend đồng bộ qua ``/admin/categories/sync``).

Trước đây mọi nơi nhận slug từ người dùng hoặc backend so với hằng số ``CATEGORY_SLUGS`` trong code, nên một danh mục mới của backend bị bỏ rơi (phản hồi sửa danh mục
sang nó bị bỏ về NULL, bộ nhớ không nhớ được) cho đến khi deploy lại. Giờ các chỗ đó hỏi ``is_known`` / ``is_spend``.

Chưa nạp được DB (khởi động, DB lỗi, test đơn vị) thì dùng ``CATEGORY_SLUGS`` làm mốc: hành vi y như trước. ĐỪNG dùng module này cho thứ mô hình hay Gemini TRẢ RA: bộ phân loại chỉ
biết các lớp nó đã học và prompt chỉ liệt kê danh mục trong code, nên danh mục mới chỉ được chúng đoán sau khi huấn luyện lại / sửa prompt.
"""

from __future__ import annotations

import asyncio
import time

import structlog
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.core.enums import CATEGORY_SLUGS
from app.db.models.category import KbCategory

logger = structlog.get_logger(__name__)

REFRESH_SECONDS = 60.0  # đồng bộ với nhịp nạp pattern/luật: danh mục mới có hiệu lực trên mọi bản chạy trong vòng một phút

_BASELINE_KNOWN = frozenset(CATEGORY_SLUGS)
_BASELINE_SPEND = _BASELINE_KNOWN - {"income"}
_known: frozenset[str] = _BASELINE_KNOWN
_spend: frozenset[str] = _BASELINE_SPEND
_checked_at: float | None = None


def is_known(slug: str | None) -> bool:
    """Slug của backend (chi hoặc thu) đang được bật."""
    return slug in _known


def is_spend(slug: str | None) -> bool:
    """Slug chỉ dành cho khoản chi (mọi thứ trừ thu nhập): hóa đơn, tên món, bộ nhớ cửa hàng."""
    return slug in _spend


def spend_list() -> list[str]:
    """Slug chi theo thứ tự ổn định (các slug có sẵn trong code trước, danh mục mới sắp chữ cái sau) để dựng thông báo lỗi và ô chọn."""
    return [slug for slug in CATEGORY_SLUGS if slug in _spend] + sorted(_spend - _BASELINE_KNOWN)


def reset() -> None:
    """Về mốc ``CATEGORY_SLUGS`` và buộc lần ``ensure_fresh`` kế tiếp đọc lại DB (dùng trong test)."""
    global _known, _spend, _checked_at
    _known, _spend, _checked_at = _BASELINE_KNOWN, _BASELINE_SPEND, None


async def refresh(session: AsyncSession) -> None:
    """Nạp lại từ ``kb_categories``. Bảng rỗng (chưa seed) thì giữ mốc trong code, không biến mọi slug thành lạ."""
    global _known, _spend, _checked_at
    rows = (
        await session.execute(select(KbCategory.slug, KbCategory.kind).where(KbCategory.is_active.is_(True), KbCategory.kind.in_(("expense", "income"))))
    ).all()
    _checked_at = time.monotonic()
    if not rows:
        return
    _known = frozenset(slug for slug, _ in rows)
    _spend = frozenset(slug for slug, kind in rows if kind == "expense")


async def watch(session_factory, interval_seconds: float = REFRESH_SECONDS) -> None:
    """Vòng nạp lại định kỳ (chạy nền từ ``lifespan``): bản chạy KHÔNG nhận lệnh đồng bộ cũng thấy danh mục mới trong vòng ``interval_seconds``. Một lượt lỗi chỉ ghi log rồi thử lại.

    Phải là vòng nền chứ không phải nạp khi có request: các schema kiểm slug chạy TRƯỚC handler, nên một danh mục mới mà bản này chưa biết sẽ bị từ chối trước khi có cơ hội nạp lại."""
    while True:
        try:
            async with session_factory() as session:
                await refresh(session)
        except Exception as error:  # noqa: BLE001 - DB lỗi không được giết vòng lặp; giữ bản đang dùng
            logger.warning("category_registry_refresh_failed", error_type=type(error).__name__)
        await asyncio.sleep(interval_seconds)
