"""Gọi API embedding của Gemini bằng ``httpx`` (không thêm package). Mô hình và số chiều lấy từ cấu hình (``AGENT_EMBEDDING_MODEL``, ``AGENT_EMBEDDING_DIM``).

Đã đối chiếu trang chính thức của Google (embeddings, 2026-11-07): ``gemini-embedding-2`` KHÔNG nhận ``taskType``; loại tác vụ nằm trong chính chữ gửi đi —
truy vấn ``task: search result | query: …``, tài liệu ``title: … | text: …``. Mô hình tự chuẩn hóa khi cắt còn 768 chiều. Phản hồi ``embedContent`` là ``{"embedding": {"values": [...]}}``.
Độ dài vector khác số chiều cột thì BỊ TỪ CHỐI (ghi vào cột sai chiều sẽ hỏng cả bảng, hoặc làm so khoảng cách vô nghĩa). Lỗi chỉ mang mã cố định, không mang nội dung."""

from __future__ import annotations

from collections.abc import Callable
from typing import Literal

import httpx
import structlog

from app.core.config import settings
from app.services import secret_store

logger = structlog.get_logger(__name__)

_URL = "https://generativelanguage.googleapis.com/v1beta/models/{model}:embedContent"


class EmbeddingError(Exception):
    """``code`` là mã cố định: ``embedding_not_configured``, ``embedding_key_missing``, ``embedding_unavailable``, ``embedding_bad_response``, ``embedding_dim_mismatch``."""

    def __init__(self, code: str) -> None:
        super().__init__(code)
        self.code = code


def format_input(text: str, kind: Literal["document", "query"], title: str | None = None) -> str:
    if kind == "query":
        return f"task: search result | query: {text}"
    return f"title: {title or 'none'} | text: {text}"


async def embed(
    texts: list[str],
    *,
    kind: Literal["document", "query"] = "document",
    title: str | None = None,
    model: str | None = None,
    dim: int | None = None,
    key_source: Callable[[], str | None] | None = None,
    transport: httpx.AsyncBaseTransport | None = None,
    timeout: float = 20.0,
) -> list[list[float]]:
    model = settings.agent_embedding_model if model is None else model
    dim = settings.agent_embedding_dim if dim is None else dim
    if not model:
        raise EmbeddingError("embedding_not_configured")
    key = (key_source or secret_store.getter("gemini_api_key"))()
    if not key:
        raise EmbeddingError("embedding_key_missing")

    vectors: list[list[float]] = []
    async with httpx.AsyncClient(timeout=timeout, transport=transport) as client:
        for text in texts:
            body = {"content": {"parts": [{"text": format_input(text, kind, title)}]}, "output_dimensionality": dim}
            try:
                response = await client.post(_URL.format(model=model), json=body, headers={"x-goog-api-key": key})
            except httpx.HTTPError as error:
                logger.warning("embedding_failed", error_type=type(error).__name__)
                raise EmbeddingError("embedding_unavailable") from error
            if response.status_code != 200:
                logger.warning("embedding_failed", status=response.status_code)
                raise EmbeddingError("embedding_unavailable")
            try:
                values = response.json()["embedding"]["values"]
            except (ValueError, KeyError, TypeError) as error:
                raise EmbeddingError("embedding_bad_response") from error
            if not isinstance(values, list) or len(values) != dim or not all(isinstance(v, (int, float)) for v in values):
                raise EmbeddingError("embedding_dim_mismatch")
            vectors.append([float(v) for v in values])
    return vectors
