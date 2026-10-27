"""Nạp tài liệu cấu hình đang chạy từ DB vào TIẾN TRÌNH NÀY, lúc khởi động và định kỳ.

Admin kích hoạt một bản thì tiến trình xử lý yêu cầu đó áp dụng ngay (``config_documents.activate``); các tiến trình/instance khác thấy bản mới ở lần kiểm tra kế tiếp
(``POLL_SECONDS``). DB trống hoặc không đọc được thì giữ bản đang chạy; bản trong DB hỏng thì bỏ qua và ghi cảnh báo: một bản cấu hình xấu không được phép làm sập
chat. Không log nội dung tài liệu."""
from __future__ import annotations

import asyncio

import structlog
from sqlalchemy import select

from app.db.models.config_document import ConfigDocument
from app.db.session import AsyncSessionLocal
from app.services import config_documents as docs

logger = structlog.get_logger(__name__)
POLL_SECONDS = 30


async def refresh(session_factory=AsyncSessionLocal) -> dict[str, int]:
    """Áp dụng mọi tài liệu (có ``apply``) mà bản đang chạy trong DB khác bản tiến trình này đang dùng. Trả về {tên: phiên bản vừa áp dụng}."""
    changed: dict[str, int] = {}
    async with session_factory() as session:
        for spec in docs.SPECS.values():
            if spec.apply is None:
                continue
            row = await session.scalar(select(ConfigDocument).where(ConfigDocument.name == spec.name, ConfigDocument.status == "active"))
            version = row.version if row else 0
            if version == docs.APPLIED.get(spec.name, 0):
                continue
            try:
                content = docs.stored_content(spec, row.content) if row else spec.default()  # không còn bản đang chạy (bị gỡ) thì quay về mặc định
                if row:
                    spec.validate(content)
                spec.apply(content)
            except Exception as error:  # noqa: BLE001 - bản hỏng không được làm sập tiến trình: giữ bản đang chạy
                logger.error("config_apply_failed", name=spec.name, version=version, error_type=type(error).__name__)
                continue
            docs.APPLIED[spec.name] = version
            changed[spec.name] = version
            logger.info("config_applied", name=spec.name, version=version)
    return changed


async def watch(interval: float = POLL_SECONDS) -> None:
    """Vòng nền: không bao giờ thoát vì lỗi tạm thời (DB đứt, migration chưa chạy)."""
    while True:
        try:
            await refresh()
        except Exception as error:  # noqa: BLE001
            logger.warning("config_refresh_failed", error_type=type(error).__name__)
        await asyncio.sleep(interval)
