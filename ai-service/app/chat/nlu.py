"""Gọi Rasa (máy chủ NLU) để hiểu ý định và cắt thực thể của một câu chat.

Bật bằng RASA_URL (vd. http://rasa:5005). Không đặt thì bỏ qua Rasa và parser chỉ dùng luật.
Rasa chậm hoặc tắt thì ``parse`` trả None (không ném lỗi): chat vẫn chạy bằng luật, chỉ ghi cảnh báo (tối đa mỗi 60 giây
một lần), và trong 30 giây sau lỗi không gọi lại Rasa (cầu dao) nên tin nhắn không bị chậm.
"""
from __future__ import annotations

import time

import httpx
import structlog

logger = structlog.get_logger(__name__)

_WARN_EVERY = 60.0
_RETRY_AFTER = 30.0  # giây: sau một lần lỗi, bỏ qua Rasa chừng này thời gian rồi mới thử lại


class RasaNlu:
    def __init__(self, url: str, timeout: float = 2.0, client: httpx.AsyncClient | None = None) -> None:
        self._endpoint = url.rstrip("/") + "/model/parse"
        self._timeout = timeout
        self._client = client
        self._last_warn = 0.0
        self._down_until = 0.0

    async def parse(self, text: str) -> dict | None:
        """{"intent", "confidence", "entities": {tên: giá trị đầu tiên}} hoặc None khi không gọi được Rasa."""
        if time.time() < self._down_until:
            return None
        try:
            if self._client is not None:
                response = await self._client.post(self._endpoint, json={"text": text})
            else:
                async with httpx.AsyncClient(timeout=self._timeout) as client:
                    response = await client.post(self._endpoint, json={"text": text})
            response.raise_for_status()
            data = response.json()
            intent = data["intent"]
            entities: dict[str, str] = {}
            for entity in data.get("entities", []):
                value = entity["value"]
                entities.setdefault(entity["entity"], value if isinstance(value, str) else str(value))
            return {"intent": intent["name"], "confidence": float(intent["confidence"]), "entities": entities}
        except Exception as error:  # noqa: BLE001 - mọi lỗi (mạng, hết giờ, dữ liệu lạ) đều quay về luật
            self._down_until = time.time() + _RETRY_AFTER
            if time.time() - self._last_warn > _WARN_EVERY:
                self._last_warn = time.time()
                logger.warning("rasa_unavailable", error=type(error).__name__)
            return None


def open_nlu(url: str | None) -> RasaNlu | None:
    return RasaNlu(url) if url else None
