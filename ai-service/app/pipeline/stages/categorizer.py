"""Stage 3 — gán danh mục cho giao dịch.

Đây là chỗ ML thực sự có việc để làm: đầu vào là tên đơn vị bán hàng, một trường văn bản
mở mà không template nào phủ hết được. Thứ tự: từ điển từ khoá (``app/data/merchant_categories.json``,
admin sửa được) trước; trượt từ điển mới hỏi model (nếu có model active); không có model thì "other".

Ràng buộc cứng: slug trả về phải nằm trong 11 slug hệ thống mà ``CategorySeeder`` của
backend seed. Slug lạ làm ``GetSystemBySlugAsync`` trả NULL và giao dịch mất danh mục mà
không báo lỗi ở đâu cả.
"""

from __future__ import annotations

import json
import pathlib
from dataclasses import dataclass

from app.core.enums import (
    CATEGORY_SLUGS,
    FALLBACK_CATEGORY_SLUG,
    ExtractionMethod,
    ModelStage,
    TransactionType,
)
from app.core import confidence as table  # tên khác: dataclass và hàm dưới có trường và biến `confidence`
from app.pipeline.models.model_registry import ModelRegistry
from app.utils.text_utils import normalize_for_model

_LEXICON_PATH = (
    pathlib.Path(__file__).resolve().parents[2] / "data" / "merchant_categories.json"
)


def default_lexicon_data() -> dict[str, list[str]]:
    """Từ điển mặc định (tệp trong image) ``{slug: [từ khóa]}``; admin sửa bằng tài liệu ``merchant_lexicon`` trong ``config_documents``."""
    return json.loads(_LEXICON_PATH.read_text(encoding="utf-8"))


def build_lexicon(data: dict[str, list[str]]) -> tuple[tuple[str, str], ...]:
    """(từ khoá, slug) sắp theo độ dài giảm dần.

    Khớp từ khoá dài trước: "the coffee house" phải thắng "cafe", nếu không mọi chuỗi
    chứa từ ngắn đều rơi vào cùng một danh mục.
    """
    pairs = [
        (keyword.lower(), slug)
        for slug, keywords in data.items()
        for keyword in keywords
        if slug in CATEGORY_SLUGS
    ]
    pairs.sort(key=lambda pair: len(pair[0]), reverse=True)
    return tuple(pairs)


_lexicon_data = default_lexicon_data()
_lexicon = build_lexicon(_lexicon_data)
_lexicon_listeners: list = []


def load_lexicon() -> tuple[tuple[str, str], ...]:
    """Bảng đang chạy: mặc định, hoặc bản admin vừa kích hoạt (``apply_lexicon``). Gán MỘT lần nên đọc xen giữa không thấy bảng dở dang."""
    return _lexicon


def current_lexicon_data() -> dict[str, list[str]]:
    return _lexicon_data


def subscribe_lexicon(callback) -> None:
    """Đăng ký hàm (không tham số) được gọi sau mỗi lần ``apply_lexicon`` (``category_resolver`` dựng lại chỉ mục của nó)."""
    _lexicon_listeners.append(callback)


def apply_lexicon(data: dict[str, list[str]]) -> None:
    """Đổi từ điển đang chạy trong tiến trình này. Dựng xong rồi mới gán (sai giữa chừng thì không đổi gì), sau đó báo cho các bên đăng ký."""
    global _lexicon_data, _lexicon
    built = build_lexicon(data)
    _lexicon_data, _lexicon = data, built
    for callback in _lexicon_listeners:
        callback()


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
            return CategorizationOutcome("income", table.get("categorizer.credit_income"), ExtractionMethod.RULE)

        text = " ".join(part for part in (merchant_name, description) if part)
        if not text.strip():
            return CategorizationOutcome(
                FALLBACK_CATEGORY_SLUG, table.get("categorizer.empty_text"), ExtractionMethod.RULE
            )

        # Luật trước, model sau: trúng từ điển là khớp đúng một cửa hàng đã biết, chắc hơn mọi
        # phỏng đoán, và từ khoá Staff vừa sửa phải có hiệu lực ngay chứ không đợi train lại.
        # Model chỉ lo phần luật bó tay: cửa hàng chưa có trong từ điển.
        hit = _lexicon_hit(text)
        if hit is not None:
            return hit

        model = self._registry.get(ModelStage.CATEGORIZER)
        if model is not None:
            slug, confidence = model.predict(normalize_for_model(text))
            if slug in CATEGORY_SLUGS:
                return CategorizationOutcome(
                    slug, confidence, ExtractionMethod.MODEL, model.version
                )
            # Model trả slug ngoài taxonomy nghĩa là artifact lệch pha với CategorySeeder.
            # Rơi về luật thay vì đẩy slug rác sang backend.

        return _rule_fallback()


# Trúng từ điển là khớp CHÍNH XÁC một tên cửa hàng đã biết ("highlands" → Ăn uống), nên nó
# đáng tin hơn hẳn một phỏng đoán. Con số này phải nằm TRÊN ngưỡng 85% của Flow 1 bước 5.2:
# 0,80 đặt từ Phase 9 — trước khi tồn tại ngưỡng nào để đối chiếu — khiến mọi khoản CHI đều
# rớt xuống nhánh hộp thoại, kể cả ca "Highlands Coffee" mà chính docx lấy làm ví dụ cho nhánh
# một chạm. Đổi ở đây chứ không hạ ngưỡng: ngưỡng là quy định nghiệp vụ, còn đây là mức tin
# cậy thật của một phép khớp chính xác.
_LEXICON_HIT_CONFIDENCE = table.get("categorizer.lexicon_hit")  # lý do và ràng buộc với ngưỡng 85%: app/data/confidence.json


def _lexicon_hit(text: str) -> CategorizationOutcome | None:
    lowered = f" {text.lower()} "
    for keyword, slug in load_lexicon():
        if keyword in lowered:
            return CategorizationOutcome(slug, _LEXICON_HIT_CONFIDENCE, ExtractionMethod.RULE)
    return None


def _rule_fallback() -> CategorizationOutcome:
    # Không khớp từ khoá nào: vẫn trả "other" nhưng với độ tin cậy thấp, để ngưỡng 85%
    # của Flow 1 đẩy sang hộp thoại cho người dùng chọn.
    return CategorizationOutcome(FALLBACK_CATEGORY_SLUG, table.get("categorizer.fallback"), ExtractionMethod.RULE)


def categorize_by_rules(text: str) -> CategorizationOutcome:
    return _lexicon_hit(text) or _rule_fallback()
