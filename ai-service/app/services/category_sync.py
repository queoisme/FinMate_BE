"""Đồng bộ danh mục từ backend vào ``kb_categories`` (nguồn sự thật là danh mục hệ thống của backend).

Chỉ CỘNG THÊM và TẮT/BẬT: không xóa danh mục, không đổi ``kind`` của danh mục đã có dữ liệu (các bảng đã kiểm loại lúc ghi, đổi loại sẽ để dòng cũ sai loại), và không đụng tới dòng
bí danh ``legacy`` của 9 nhóm cũ. Mỗi thay đổi ghi ``kb_audit_log`` (``resource = 'category'``). Xong thì nạp lại ``category_registry`` ngay trên bản chạy này; các bản khác tự nạp
trong vòng ``REFRESH_SECONDS``.

Mô hình và Gemini KHÔNG tự biết danh mục mới: chúng chỉ đoán các lớp đã học / liệt kê trong code. Danh mục mới dùng được ngay cho phản hồi, bộ nhớ cửa hàng, bộ nhớ tên món và nhật ký.
"""

from __future__ import annotations

from dataclasses import dataclass

from sqlalchemy import func, select
from sqlalchemy.ext.asyncio import AsyncSession

from app.db.models.bill import BillRuleAudit
from app.db.models.category import KbCategory
from app.schemas.categories import CategoryIn
from app.services import category_registry


class CategorySyncError(Exception):
    """Yêu cầu hợp lệ về cú pháp nhưng không làm được (HTTP 409)."""


@dataclass(frozen=True)
class SyncResult:
    created: int
    updated: int
    unchanged: int


def _snapshot(row: KbCategory) -> dict:
    return {"label": row.label, "kind": row.kind, "is_active": row.is_active, "taxonomy_version": row.taxonomy_version}


def _audit(session: AsyncSession, slug: str, action: str, before: dict | None, after: dict, actor: str, reason: str | None) -> None:
    session.add(BillRuleAudit(resource="category", resource_key=slug, action=action, before=before, after=after, actor_hash=actor, reason=reason))


async def sync(session: AsyncSession, items: list[CategoryIn], *, taxonomy_version: int | None, actor: str, reason: str | None) -> SyncResult:
    current_version = (await session.execute(select(func.coalesce(func.max(KbCategory.taxonomy_version), 1)))).scalar_one()
    version = taxonomy_version or current_version
    created = updated = unchanged = 0

    for item in items:
        row = await session.get(KbCategory, item.slug)
        if row is None:
            row = KbCategory(slug=item.slug, label=item.label, kind=item.kind, is_active=item.is_active, taxonomy_version=version)
            session.add(row)
            await session.flush()
            _audit(session, item.slug, "create", None, _snapshot(row), actor, reason)
            created += 1
            continue
        if row.kind == "legacy":
            raise CategorySyncError(f"{item.slug!r} là nhóm cũ của bộ luật bill (dòng bí danh), không đồng bộ được từ backend")
        if row.kind != item.kind:
            raise CategorySyncError(f"không đổi được loại của {item.slug!r} ({row.kind} -> {item.kind}): các bảng đã ghi dữ liệu theo loại cũ")

        before = _snapshot(row)
        row.synced_at = func.now()  # backend vừa xác nhận danh mục này còn đó, kể cả khi không có gì đổi
        label_changed = row.label != item.label
        active_changed = row.is_active != item.is_active
        version_changed = taxonomy_version is not None and row.taxonomy_version != version
        if label_changed or active_changed or version_changed:
            action = "update" if (label_changed or version_changed) else ("activate" if item.is_active else "deactivate")
            row.label, row.is_active = item.label, item.is_active
            if version_changed:
                row.taxonomy_version = version
            await session.flush()
            _audit(session, item.slug, action, before, _snapshot(row), actor, reason)
            updated += 1
        else:
            unchanged += 1

    await session.commit()
    await category_registry.refresh(session)
    return SyncResult(created, updated, unchanged)
