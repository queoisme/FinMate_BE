"""Phiếu của người dùng cho danh mục của MỘT MÓN trên hóa đơn (``ml_item_feedback``) và việc đưa món đủ phiếu vào bảng nhớ tên món (``kb_item_categories``).

Luồng: người dùng sửa danh mục của một món khi lưu hóa đơn -> backend gửi ``POST /bill/items/feedback`` (CHỈ khi người dùng đã đồng ý đóng góp dữ liệu, như
``/bill/samples``) -> mỗi người một phiếu cho mỗi món (phiếu sau thay phiếu trước). View ``v_item_consensus`` đếm số người và tỉ lệ đồng ý;
``promote_consensus`` (``scripts/promote_item_consensus.py``) chỉ THÊM món đủ ngưỡng vào ``kb_item_categories`` với ``source = 'consensus'``.

Cố ý KHÔNG tự ghi đè: món đã có (do Gemini, quản trị viên hay một lần đồng thuận trước) mà đa số người dùng không đồng ý được báo ở ``disputes`` để người duyệt quyết.
Tên món là chữ của người dùng nên qua cùng bộ lọc với bảng nhớ (``usable_item_key``) và bị từ chối nếu còn chuỗi số dài (điện thoại, tài khoản). Không log tên món.
"""

from __future__ import annotations

from dataclasses import dataclass, field

from sqlalchemy import text
from sqlalchemy.ext.asyncio import AsyncSession

from app.bill.item_classifier import usable_item_key
from app.bill.samples import has_long_digit_run
from app.db.models.bill import BillRuleAudit
from app.services import category_registry

# Ngưỡng đề xuất của ERD v5_1 (giống ocr-service/promotion_pg): đủ người KHÁC NHAU và đủ đồng thuận mới vào kiến thức chung. Đổi bằng tham số script, không cần sửa code.
DEFAULT_MIN_USERS = 3
DEFAULT_MIN_AGREEMENT = 0.8

_VOTE_SQL = text(
    "INSERT INTO ml_item_feedback (item_key, user_id_hash, category_slug) VALUES (:key, :user, :slug) "
    "ON CONFLICT (item_key, user_id_hash) DO UPDATE SET category_slug = EXCLUDED.category_slug, created_at = now()"
)


async def record_votes(session: AsyncSession, user_id_hash: str, items: list[tuple[str, str]]) -> int:
    """Ghi phiếu ``(tên món, slug)`` của một người dùng. Trả số phiếu đã ghi; tên không dùng được, slug không phải khoản chi hay tên còn số dài thì bỏ qua (không phải lỗi)."""
    recorded = 0
    for name, slug in items:
        key = usable_item_key(name)
        if key is None or has_long_digit_run([name]) or not category_registry.is_spend(slug):
            continue
        await session.execute(_VOTE_SQL, {"key": key, "user": user_id_hash, "slug": slug})
        recorded += 1
    await session.commit()
    return recorded


async def delete_by_user_hash(session: AsyncSession, user_id_hash: str) -> int:
    """Xóa mọi phiếu của một người dùng (rút đồng ý đóng góp hoặc xóa tài khoản). Trả số phiếu đã xóa."""
    result = await session.execute(text("DELETE FROM ml_item_feedback WHERE user_id_hash = :user"), {"user": user_id_hash})
    await session.commit()
    return result.rowcount or 0


@dataclass
class Promotion:
    promoted: list[str] = field(default_factory=list)  # món thêm mới vào kb_item_categories (hoặc sẽ thêm, khi chạy thử)
    disputes: list[str] = field(default_factory=list)  # đã có dòng khác nhóm mà đa số người dùng chọn nhóm khác: để người duyệt quyết
    pending: list[str] = field(default_factory=list)  # chưa đủ ngưỡng


async def promote_consensus(
    session: AsyncSession, *, actor_hash: str, min_users: int = DEFAULT_MIN_USERS, min_agreement: float = DEFAULT_MIN_AGREEMENT, write: bool = False
) -> Promotion:
    """Đọc ``v_item_consensus`` và thêm món đủ ngưỡng vào ``kb_item_categories`` (``write=False`` chỉ tính, không ghi). Không bao giờ ghi đè dòng đã có."""
    votes = (await session.execute(text("SELECT item_key, category_slug, users, agreement FROM v_item_consensus"))).all()
    existing = {row[0]: row[1] for row in (await session.execute(text("SELECT item_key, category_slug FROM kb_item_categories"))).all()}

    best: dict[str, tuple[str, int, float]] = {}
    for key, slug, users, agreement in votes:
        if key not in best or users > best[key][1]:
            best[key] = (slug, users, agreement)

    result = Promotion()
    for key, (slug, users, agreement) in sorted(best.items()):
        if users < min_users or agreement < min_agreement:
            result.pending.append(key)
            continue
        if key in existing:
            if existing[key] != slug:
                result.disputes.append(key)
            continue
        result.promoted.append(key)
        if write:
            await session.execute(
                text("INSERT INTO kb_item_categories (item_key, category_slug, source) VALUES (:key, :slug, 'consensus') ON CONFLICT (item_key) DO NOTHING"),
                {"key": key, "slug": slug},
            )
            session.add(BillRuleAudit(
                resource="item_cache", resource_key=key, action="create", before=None,
                after={"category_slug": slug, "source": "consensus", "users": users, "agreement": round(agreement, 3)},
                actor_hash=actor_hash, reason="đồng thuận của người dùng (v_item_consensus)",
            ))
    if write:
        await session.commit()
    return result
