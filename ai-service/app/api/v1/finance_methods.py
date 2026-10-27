"""``POST /api/v1/finance-methods/recommend`` — signals ẩn danh + danh sách phương pháp được phép → MỘT cách chính và tối đa MỘT cách bổ trợ.

Hợp đồng: ``docs/ai-finance-method-recommend-api.md``. Không nhận ``user_id``, không ghi DB (chỉ đọc ``kb_method_prompts``), không log signals/mã/lý do.
Backend chỉ gọi khi người dùng đã đồng ý gửi khảo sát cho AI; mọi lỗi 5xx = "không có kết quả AI" và backend chạy nhánh rule.
"""

from __future__ import annotations

import time
from typing import Annotated

import structlog
from fastapi import APIRouter, Depends

from app.api.deps import verify_internal_api_key
from app.bill.gemini import GeminiClient, GeminiFailed
from app.bill.rules_admin import RuleError
from app.core.config import settings
from app.schemas.finance_methods import RecommendRequest, RecommendResponse
from app.services import method_picker
from app.services.method_backup import BackupFailed, OpenRouterPicker
from app.services.method_picker import InvalidOutput, MethodPromptRow

logger = structlog.get_logger(__name__)
router = APIRouter(dependencies=[Depends(verify_internal_api_key)])

# Client riêng (cầu dao tách khỏi hóa đơn và chat), dùng chung khóa Gemini. Dự phòng thứ hai (OpenRouter) ở ``_backup`` bên dưới, không nằm trong client này.
_gemini = GeminiClient(
    settings.gemini_api_key,
    settings.method_gemini_model,
    settings.method_gemini_timeout_seconds,
    overall_timeout=settings.method_gemini_overall_seconds,
    fallback_models=tuple(m.strip() for m in settings.method_gemini_fallback_models.split(",") if m.strip()),
)


def get_method_gemini_client() -> GeminiClient:
    """Test thay bằng bản giả qua ``dependency_overrides``."""
    return _gemini


# Dự phòng thứ hai: chỉ chạy khi Gemini hết cách; tắt nếu thiếu khóa hoặc METHOD_OPENROUTER_MODELS trống.
_backup = OpenRouterPicker(
    settings.openrouter_api_key,
    tuple(m.strip() for m in settings.method_openrouter_models.split(",") if m.strip()),
    settings.method_openrouter_timeout_seconds,
)


def get_method_backup_picker() -> OpenRouterPicker:
    """Test thay bằng bản giả qua ``dependency_overrides``."""
    return _backup


async def get_method_prompt() -> MethodPromptRow | None:
    """Test thay bằng bản giả qua ``dependency_overrides``."""
    return await method_picker.get_method_prompt()


def _fail(status: int, code: str, message: str, details: list[str] | None = None) -> RuleError:
    logger.warning("finance_method_recommend_failed", outcome=code, reason=details[0] if details else None)  # chỉ mã cố định
    return RuleError(status, code, message, details)


@router.post("/finance-methods/recommend", response_model=RecommendResponse)
async def recommend_finance_method(
    body: RecommendRequest,
    gemini: Annotated[GeminiClient, Depends(get_method_gemini_client)],
    prompt: Annotated[MethodPromptRow | None, Depends(get_method_prompt)],
    backup: Annotated[OpenRouterPicker, Depends(get_method_backup_picker)],
) -> RecommendResponse:
    started = time.perf_counter()
    if not any(m.role in method_picker.PRIMARY_ROLES for m in body.methods):
        raise RuleError(422, "no_primary_candidate", "Không phương pháp nào có thể làm cách chính (role primary hoặc both).")
    if prompt is None:
        raise _fail(503, "prompt_missing", "Chưa có chỉ dẫn đang hoạt động trong kb_method_prompts.")
    provider = "gemini"
    try:
        raw = await gemini.generate_text(
            method_picker.build_prompt(prompt.text, body.signals, body.methods), method_picker.build_schema(body.methods)
        )
    except GeminiFailed as error:
        gemini_reason = str(error.args[0]) if error.args else "unknown"
        if not backup.enabled:
            raise _fail(503, "ai_unavailable", "Mô hình AI không phản hồi được.", [gemini_reason]) from error
        try:
            raw = await backup.generate(
                method_picker.build_backup_prompt(prompt.text, body.signals, body.methods),
                method_picker.build_backup_schema(body.methods),
                settings.method_total_seconds - (time.perf_counter() - started),
            )
        except BackupFailed as backup_error:
            # details[0] vẫn là lý do của Gemini (nhà cung cấp chính); lý do dự phòng là phần tử thứ hai
            raise _fail(503, "ai_unavailable", "Mô hình AI không phản hồi được.", [gemini_reason, str(backup_error.args[0])]) from backup_error
        provider = "openrouter"
    try:
        primary, support = method_picker.parse_and_validate(raw, body.methods)
    except InvalidOutput as error:
        raise _fail(502, "ai_invalid_output", "Đầu ra của mô hình không qua kiểm tra.", [error.reason]) from error
    processing_ms = int((time.perf_counter() - started) * 1000)
    logger.info("finance_method_recommended", outcome="ok", provider=provider, signals=len(body.signals), methods=len(body.methods), has_support=support is not None, processing_ms=processing_ms)
    return RecommendResponse(primary=primary, support=support, prompt_version=prompt.version, processing_ms=processing_ms)
