"""Nhà cung cấp OpenRouter cho agent (ví dụ DeepSeek V3.2 để so sánh trong bộ đánh giá, hoặc dự phòng). Giao thức giống OpenAI ``chat/completions``.

Mỗi lần gọi gửi ``provider.data_collection = "deny"``: OpenRouter chỉ chọn bên cung cấp cam kết không lưu và không huấn luyện trên lời nhắc; không có bên nào đáp ứng thì trả lỗi
chứ không âm thầm gửi đi. Đây là hằng số trong code vì dữ liệu của agent là dữ liệu tài chính của người dùng."""

from __future__ import annotations

import time
from collections.abc import Callable

import httpx
import structlog

from app.agent.llm.provider import LlmFailed, LlmResult, parse_json_object, token_count

logger = structlog.get_logger(__name__)

_URL = "https://openrouter.ai/api/v1/chat/completions"


class OpenRouterProvider:
    def __init__(
        self,
        api_key: str | Callable[[], str | None] | None,
        model: str,
        timeout: float,
        transport: httpx.AsyncBaseTransport | None = None,
    ) -> None:
        self._key_source = api_key
        self._model = model
        self._timeout = timeout
        self._transport = transport

    @property
    def _key(self) -> str | None:
        return self._key_source() if callable(self._key_source) else self._key_source

    async def generate_json(self, system: str, user: str, schema: dict, max_output_tokens: int) -> LlmResult:
        key = self._key
        if not key:
            raise LlmFailed("unavailable")
        body = {
            "model": self._model,
            "temperature": 0,
            "max_tokens": max_output_tokens,
            "messages": [{"role": "system", "content": system}, {"role": "user", "content": user}],
            "response_format": {"type": "json_schema", "json_schema": {"name": "agent_output", "strict": True, "schema": schema}},
            "reasoning": {"enabled": False},  # lập kế hoạch và soạn câu không cần chuỗi suy luận dài: chậm hơn và tốn token đầu ra
            "provider": {"data_collection": "deny"},
        }
        started = time.monotonic()
        try:
            async with httpx.AsyncClient(timeout=self._timeout, transport=self._transport) as client:
                res = await client.post(_URL, headers={"Authorization": f"Bearer {key}"}, json=body)
            res.raise_for_status()
            payload = res.json()
            content = payload["choices"][0]["message"]["content"]
        except httpx.HTTPStatusError as error:
            logger.warning("llm_failed", provider="openrouter", code=f"http_{error.response.status_code}")
            raise LlmFailed(f"http_{error.response.status_code}") from error
        except (httpx.HTTPError, KeyError, IndexError, TypeError, ValueError) as error:
            logger.warning("llm_failed", provider="openrouter", code=type(error).__name__)
            raise LlmFailed(type(error).__name__) from error

        data = parse_json_object(content)
        usage = payload.get("usage")
        result = LlmResult(
            data=data,
            input_tokens=token_count(usage, "prompt_tokens"),
            output_tokens=token_count(usage, "completion_tokens"),
            provider="openrouter",
            model=self._model,
        )
        logger.info("llm_ok", provider="openrouter", model=self._model, latency_ms=round((time.monotonic() - started) * 1000), input_tokens=result.input_tokens, output_tokens=result.output_tokens)
        return result
