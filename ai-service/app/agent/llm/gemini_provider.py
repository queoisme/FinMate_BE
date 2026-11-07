"""Nhà cung cấp Gemini cho agent: ``generateContent`` với ``systemInstruction`` và ``responseSchema``. Dùng cùng khóa (``GEMINI_API_KEY`` / ``secret_store``) với phần còn lại
của ai-service. Thử lại MỘT lần khi 429/503. Gói miễn phí của Gemini dùng dữ liệu để cải thiện sản phẩm của Google: dữ liệu người dùng thật chỉ được đi qua gói trả phí."""

from __future__ import annotations

import asyncio
import time
from collections.abc import Callable
from typing import Any

import httpx
import structlog

from app.agent.llm.provider import LlmFailed, LlmResult, parse_json_object, token_count

logger = structlog.get_logger(__name__)

_URL = "https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent"


def to_gemini_schema(schema: Any) -> Any:
    """JSON Schema chuẩn → ``responseSchema`` của Gemini: kiểu viết hoa, bỏ ``additionalProperties`` (Gemini không nhận)."""
    if isinstance(schema, list):
        return [to_gemini_schema(item) for item in schema]
    if not isinstance(schema, dict):
        return schema
    out: dict[str, Any] = {}
    for key, value in schema.items():
        if key == "additionalProperties":
            continue
        if key == "type" and isinstance(value, str):
            out[key] = value.upper()
        elif key == "properties" and isinstance(value, dict):
            out[key] = {name: to_gemini_schema(sub) for name, sub in value.items()}  # tên thuộc tính có thể trùng từ khóa ("type") nên không đi qua nhánh trên
        else:
            out[key] = to_gemini_schema(value)
    return out


class GeminiProvider:
    def __init__(
        self,
        api_key: str | Callable[[], str | None] | None,
        model: str,
        timeout: float,
        transport: httpx.AsyncBaseTransport | None = None,
        retry_delay: float = 1.5,
    ) -> None:
        self._key_source = api_key
        self._model = model
        self._timeout = timeout
        self._transport = transport
        self._retry_delay = retry_delay

    @property
    def _key(self) -> str | None:
        return self._key_source() if callable(self._key_source) else self._key_source

    async def generate_json(self, system: str, user: str, schema: dict, max_output_tokens: int) -> LlmResult:
        key = self._key
        if not key:
            raise LlmFailed("unavailable")
        body = {
            "systemInstruction": {"parts": [{"text": system}]},
            "contents": [{"role": "user", "parts": [{"text": user}]}],
            "generationConfig": {
                "temperature": 0,
                "maxOutputTokens": max_output_tokens,
                "responseMimeType": "application/json",
                "responseSchema": to_gemini_schema(schema),
            },
        }
        url, headers = _URL.format(model=self._model), {"x-goog-api-key": key}
        started = time.monotonic()
        try:
            async with asyncio.timeout(self._timeout * 2 + self._retry_delay):  # httpx chỉ đặt trần theo từng pha; đây là trần cả lần gọi gồm lần thử lại
                async with httpx.AsyncClient(timeout=self._timeout, transport=self._transport) as client:
                    res = await client.post(url, headers=headers, json=body)
                    if res.status_code in (429, 503):
                        await asyncio.sleep(self._retry_delay)
                        res = await client.post(url, headers=headers, json=body)
            res.raise_for_status()
            payload = res.json()
            text = payload["candidates"][0]["content"]["parts"][0]["text"]
        except TimeoutError as error:
            logger.warning("llm_failed", provider="gemini", code="timeout")
            raise LlmFailed("timeout") from error
        except httpx.HTTPStatusError as error:
            logger.warning("llm_failed", provider="gemini", code=f"http_{error.response.status_code}")
            raise LlmFailed(f"http_{error.response.status_code}") from error
        except (httpx.HTTPError, KeyError, IndexError, TypeError, ValueError) as error:
            logger.warning("llm_failed", provider="gemini", code=type(error).__name__)
            raise LlmFailed(type(error).__name__) from error

        data = parse_json_object(text)
        usage = payload.get("usageMetadata")
        result = LlmResult(
            data=data,
            input_tokens=token_count(usage, "promptTokenCount"),
            output_tokens=token_count(usage, "candidatesTokenCount"),
            provider="gemini",
            model=self._model,
        )
        logger.info("llm_ok", provider="gemini", model=self._model, latency_ms=round((time.monotonic() - started) * 1000), input_tokens=result.input_tokens, output_tokens=result.output_tokens)
        return result
