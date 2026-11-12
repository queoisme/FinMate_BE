"""Cổng gọi LLM dùng chung của AI Financial Agent: một giao diện, nhiều nhà cung cấp, đổi bằng cấu hình (``AGENT_PROVIDER`` / ``AGENT_MODEL``).

Quy tắc giữ ở mọi nhà cung cấp: ``temperature = 0``; đầu ra BẮT BUỘC là một object JSON theo schema; lỗi luôn là ``LlmFailed`` với ``args[0]`` là MÃ CỐ ĐỊNH
(``http_503``, ``bad_json``, ``timeout``...) — không bao giờ mang thân phản hồi hay lời nhắc; và KHÔNG bao giờ log lời nhắc, phản hồi hay khóa."""

from __future__ import annotations

import json
from collections.abc import Callable
from typing import Any, Protocol

from pydantic import BaseModel


class LlmFailed(Exception):
    """Nhà cung cấp lỗi, quá hạn hoặc trả sai dạng. ``args[0]`` là mã cố định."""


class LlmResult(BaseModel):
    data: dict[str, Any]
    input_tokens: int
    output_tokens: int
    provider: str
    model: str


class LlmProvider(Protocol):
    async def generate_json(self, system: str, user: str, schema: dict, max_output_tokens: int) -> LlmResult: ...


def parse_json_object(text: object) -> dict[str, Any]:
    """Chữ do mô hình trả → object JSON. Không phải JSON → ``bad_json``; JSON nhưng không phải object → ``not_object``."""
    if not isinstance(text, str):
        raise LlmFailed("bad_json")
    try:
        data = json.loads(text)
    except ValueError as error:
        raise LlmFailed("bad_json") from error
    if not isinstance(data, dict):
        raise LlmFailed("not_object")
    return data


def token_count(usage: object, name: str) -> int:
    """Số token từ khối ``usage`` của nhà cung cấp; thiếu hay sai kiểu thì 0 (số đếm chỉ để tính chi phí, không được làm hỏng một lượt)."""
    value = usage.get(name) if isinstance(usage, dict) else None
    return value if isinstance(value, int) and not isinstance(value, bool) and value >= 0 else 0


class ChainProvider:
    """Thử lần lượt từng nhà cung cấp; chỉ khi TẤT CẢ lỗi mới ném lỗi của nhà cung cấp cuối."""

    def __init__(self, providers: list[LlmProvider]) -> None:
        self.providers = providers

    async def generate_json(self, system: str, user: str, schema: dict, max_output_tokens: int) -> LlmResult:
        last: LlmFailed | None = None
        for provider in self.providers:
            try:
                return await provider.generate_json(system, user, schema, max_output_tokens)
            except LlmFailed as error:
                last = error
        raise last or LlmFailed("no_provider")


def build_provider(settings: Any, key_getter: Callable[[str], Callable[[], str | None]]) -> LlmProvider:
    """Dựng nhà cung cấp theo cấu hình. ``key_getter(name)`` trả hàm lấy khóa hiện tại (``secret_store.getter``), nên đổi khóa qua trang quản trị không cần khởi động lại.

    Nhà cung cấp dự phòng chỉ có khi ``AGENT_BACKUP_MODEL`` được đặt (OpenRouter): dữ liệu người dùng không được tự ý đi tới một bên chưa ai chọn."""
    from app.agent.llm.gemini_provider import GeminiProvider
    from app.agent.llm.openrouter_provider import OpenRouterProvider

    timeout = settings.agent_timeout_seconds
    chain: list[LlmProvider] = []
    if settings.agent_provider == "gemini":
        chain.append(GeminiProvider(key_getter("gemini_api_key"), settings.agent_model, timeout))
        # Khóa Gemini dự phòng (project khác): khi khóa chính bị 429/lỗi thì thử khóa này. Chưa nhập khóa thì GeminiProvider báo "unavailable" mà không gọi mạng.
        chain.append(GeminiProvider(key_getter("gemini_api_key_backup"), settings.agent_model, timeout))
    else:
        chain.append(OpenRouterProvider(key_getter("openrouter_api_key"), settings.agent_model, timeout))
    if settings.agent_backup_model:
        chain.append(OpenRouterProvider(key_getter("openrouter_api_key"), settings.agent_backup_model, timeout))
    return chain[0] if len(chain) == 1 else ChainProvider(chain)
