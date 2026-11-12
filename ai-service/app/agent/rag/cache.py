"""Bộ nhớ đệm kết quả truy xuất tri thức trong Redis (TTL 1 giờ), khóa theo mã băm của câu truy vấn VÀ phiên bản kho (``kb_version``).

Duyệt hay gỡ một tài liệu gọi ``bump_version()`` nên mọi kết quả cũ tự bỏ (khóa mới); không cần xóa từng khóa. Chỉ lưu KẾT QUẢ truy xuất từ tài liệu đã duyệt (nội dung chung cho mọi người),
khóa chỉ chứa mã băm: không có mã người dùng, không có số liệu cá nhân, không có câu hỏi gốc. Redis lỗi thì bỏ qua đệm (đọc thẳng DB), không làm hỏng lượt hỏi.
``client`` là bất kỳ đối tượng nào có ``get/set/incr`` bất đồng bộ (``redis.asyncio.Redis`` hoặc bản giả trong test)."""

from __future__ import annotations

import hashlib
import json
from collections.abc import Awaitable, Callable
from typing import Any

import structlog

logger = structlog.get_logger(__name__)

TTL_SECONDS = 3600
VERSION_KEY = "agent:kb:version"


def query_hash(query: str, k: int) -> str:
    """Chuẩn hóa nhẹ (cắt khoảng trắng, chữ thường) rồi băm: hai cách viết hoa/thường của cùng câu dùng chung một mục đệm."""
    return hashlib.sha256(f"{' '.join(query.lower().split())}|{k}".encode()).hexdigest()


class KbCache:
    def __init__(self, client: Any) -> None:
        self._client = client

    async def version(self) -> int:
        try:
            value = await self._client.get(VERSION_KEY)
            return int(value) if value is not None else 0
        except Exception as error:  # noqa: BLE001 - Redis hỏng không được làm hỏng truy xuất
            logger.warning("kb_cache_unavailable", error_type=type(error).__name__)
            return 0

    async def bump_version(self) -> None:
        try:
            await self._client.incr(VERSION_KEY)
        except Exception as error:  # noqa: BLE001
            logger.warning("kb_cache_unavailable", error_type=type(error).__name__)

    async def get_or_set(self, query_hash_value: str, kb_version: int, loader: Callable[[], Awaitable[list[dict]]]) -> list[dict]:
        key = f"agent:kb:{kb_version}:{query_hash_value}"
        try:
            cached = await self._client.get(key)
            if cached is not None:
                return json.loads(cached)
        except Exception as error:  # noqa: BLE001
            logger.warning("kb_cache_unavailable", error_type=type(error).__name__)
        value = await loader()
        try:
            await self._client.set(key, json.dumps(value, ensure_ascii=False), ex=TTL_SECONDS)
        except Exception as error:  # noqa: BLE001
            logger.warning("kb_cache_unavailable", error_type=type(error).__name__)
        return value
