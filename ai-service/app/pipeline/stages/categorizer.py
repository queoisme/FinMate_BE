"""Stage 3 — gán danh mục cho giao dịch.

Đây là chỗ ML thực sự có việc để làm: đầu vào là tên đơn vị bán hàng, một trường văn bản
mở mà không template nào phủ hết được. Chưa có model thì rơi về từ điển từ khoá trong
``app/data/merchant_categories.json``.

Ràng buộc cứng: slug trả về phải nằm trong 11 slug hệ thống mà ``CategorySeeder`` của
backend seed. Slug lạ làm ``GetSystemBySlugAsync`` trả NULL và giao dịch mất danh mục mà
không báo lỗi ở đâu cả.
"""

from __future__ import annotations

import json
import pathlib
from dataclasses import dataclass
from functools import lru_cache

from app.core.enums import (
    CATEGORY_SLUGS,
    FALLBACK_CATEGORY_SLUG,
    ExtractionMethod,
    ModelStage,
    TransactionType,
)
from app.pipeline.models.model_registry import ModelRegistry
from app.utils.text_utils import normalize_for_model

_LEXICON_PATH = (
    pathlib.Path(__file__).resolve().parents[2] / "data" / "merchant_categories.json"
)


@lru_cache(maxsize=1)
def load_lexicon() -> tuple[tuple[str, str], ...]:
    """(từ khoá, slug) sắp theo độ dài giảm dần.

    Khớp từ khoá dài trước: "the coffee house" phải thắng "cafe", nếu không mọi chuỗi
    chứa từ ngắn đều rơi vào cùng một danh mục.
    """
    data = json.loads(_LEXICON_PATH.read_text(encoding="utf-8"))
    pairs = [
        (keyword.lower(), slug)
        for slug, keywords in data.items()
        for keyword in keywords
        if slug in CATEGORY_SLUGS
    ]
    pairs.sort(key=lambda pair: len(pair[0]), reverse=True)
    return tuple(pairs)


@dataclass(frozen=True)
class CategorizationOutcome:
    category_slug: str
    confidence: float
    method: ExtractionMethod
    model_version: str | None = None


class Categorizer:
    def __init__(self, registry: ModelRegistry) -> None:
        self._registry = registry

    def categorize(
        self,
        merchant_name: str | None,
        description: str | None,
        transaction_type: TransactionType,
    ) -> CategorizationOutcome:
        # Tiền vào ví thì luôn là thu nhập trong taxonomy 11 danh mục này — không có danh
        # mục "thu" nào khác, nên hỏi model ở đây chỉ tạo cơ hội sai.
        if transaction_type == TransactionType.CREDIT:
            return CategorizationOutcome("income", 0.95, ExtractionMethod.RULE)

        text = " ".join(part for part in (merchant_name, description) if part)
        if not text.strip():
            return CategorizationOutcome(
                FALLBACK_CATEGORY_SLUG, 0.30, ExtractionMethod.RULE
            )

        model = self._registry.get(ModelStage.CATEGORIZER)
        if model is not None:
            slug, confidence = model.predict(normalize_for_model(text))
            if slug in CATEGORY_SLUGS:
                return CategorizationOutcome(
                    slug, confidence, ExtractionMethod.MODEL, model.version
                )
            # Model trả slug ngoài taxonomy nghĩa là artifact lệch pha với CategorySeeder.
            # Rơi về luật thay vì đẩy slug rác sang backend.

        return categorize_by_rules(text)


def categorize_by_rules(text: str) -> CategorizationOutcome:
    lowered = f" {text.lower()} "
    for keyword, slug in load_lexicon():
        if keyword in lowered:
            return CategorizationOutcome(slug, 0.80, ExtractionMethod.RULE)
    # Không khớp từ khoá nào: vẫn trả "other" nhưng với độ tin cậy thấp, để ngưỡng 85%
    # của Flow 1 đẩy sang hộp thoại cho người dùng chọn.
    return CategorizationOutcome(FALLBACK_CATEGORY_SLUG, 0.35, ExtractionMethod.RULE)
