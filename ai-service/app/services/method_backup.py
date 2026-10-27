"""Nhà cung cấp dự phòng thứ hai cho ``POST /finance-methods/recommend``: OpenRouter, CHỈ model ``:free`` trong ``METHOD_OPENROUTER_MODELS``.

Chỉ được gọi khi chuỗi model Gemini đã hết cách. Mỗi lần gọi gửi ``provider.data_collection = "deny"`` — OpenRouter chỉ chọn bên cung cấp cam kết không lưu/huấn luyện
trên prompt; nếu không có bên nào đáp ứng thì trả 404 và model đó bị bỏ qua (nhiều model ``:free`` rơi vào trường hợp này). Đây là hằng số trong code, không cấu hình được:
đồng ý của người dùng chỉ phủ các bên nêu trong câu chữ đồng ý, và nội dung gửi đi vẫn chỉ là signals ẩn danh + danh sách phương pháp.

Không log prompt, phản hồi, mã hay lý do; mã lỗi luôn là chuỗi cố định.
"""

from __future__ import annotations

import asyncio
import time
from collections.abc import Callable

import httpx
import structlog

logger = structlog.get_logger(__name__)

_URL = "https://openrouter.ai/api/v1/chat/completions"
CIRCUIT_SECONDS = 30.0
MIN_BUDGET_SECONDS = 2.0  # còn ít hơn thế thì không bắt đầu một lần gọi mới


class BackupFailed(Exception):
    """Mọi model dự phòng đều lỗi / quá hạn / không còn thời gian. ``args[0]`` là mã cố định."""


class OpenRouterPicker:
    def __init__(
        self,
        api_key: str | None,
        models: tuple[str, ...],
        timeout: float,
        transport: httpx.AsyncBaseTransport | None = None,
        clock: Callable[[], float] = time.monotonic,
    ) -> None:
        self._key = api_key
        self._models = models
        self._timeout = timeout
        self._transport = transport
        self._clock = clock
        self._open_until = 0.0

    @property
    def enabled(self) -> bool:
        return bool(self._key and self._models)

    async def generate(self, prompt: str, schema: dict, budget_seconds: float) -> str:
        """Prompt + ``response_format`` json_schema → chữ JSON do model trả (người gọi tự kiểm). Thử lần lượt từng model trong ngân sách thời gian còn lại."""
        if not self.enabled or self._clock() < self._open_until:
            raise BackupFailed("backup_unavailable")
        deadline = self._clock() + budget_seconds
        last = "backup_no_budget"
        for model in self._models:
            remaining = deadline - self._clock()
            if remaining < MIN_BUDGET_SECONDS:
                break
            try:
                return await self._call(model, prompt, schema, min(self._timeout, remaining))
            except BackupFailed as error:
                last = str(error.args[0])
        self._open_until = self._clock() + CIRCUIT_SECONDS
        logger.warning("method_backup_failed", error=last)  # mã cố định
        raise BackupFailed(last)

    async def _call(self, model: str, prompt: str, schema: dict, timeout: float) -> str:
        body = {
            "model": model,
            "temperature": 0,
            "messages": [{"role": "user", "content": prompt}],
            "response_format": schema,
            "reasoning": {"enabled": False},  # đo 2026-10-27: dots-3-note 28 s → 3-4 s khi tắt suy luận; việc này chỉ là chọn trong danh sách
            "provider": {"data_collection": "deny"},
        }
        started = self._clock()
        try:
            async with asyncio.timeout(timeout):
                async with httpx.AsyncClient(timeout=timeout, transport=self._transport) as client:
                    res = await client.post(_URL, headers={"Authorization": f"Bearer {self._key}"}, json=body)
            res.raise_for_status()
            content = res.json()["choices"][0]["message"]["content"]
        except TimeoutError as error:
            raise BackupFailed("backup_timeout") from error
        except httpx.HTTPStatusError as error:
            raise BackupFailed(f"backup_http_{error.response.status_code}") from error
        except (httpx.HTTPError, KeyError, IndexError, TypeError, ValueError) as error:
            raise BackupFailed(f"backup_{type(error).__name__}") from error
        if not isinstance(content, str) or not content.strip():
            raise BackupFailed("backup_empty")
        logger.info("method_backup_ok", latency_ms=round((self._clock() - started) * 1000))
        return content
