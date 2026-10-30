"""Stage 1 — thông báo này có phải giao dịch tài chính không.

Model (nếu đã promote) quyết định; chưa có model thì rơi về luật. Nhánh luật không phải
đồ tạm bợ: nó là lớp đỡ khi gặp ngân hàng chưa từng thấy trong tập train, và là thứ chạy
ở mọi lần triển khai mới trước khi kịp train.
"""

from __future__ import annotations

from dataclasses import dataclass

from app.core import confidence as table  # tên khác: các dataclass/hàm dưới có trường và biến `confidence`
from app.core.enums import ClassifierLabel, ExtractionMethod, ModelStage
from app.pipeline.models.model_registry import ModelRegistry
from app.services import notification_vocab
from app.utils.text_utils import find_amount_candidates, normalize_for_model

# Câu do người dùng tự gõ/đọc trong luồng "thêm giao dịch" — ý định đã rõ từ trước khi
# gửi lên, nên không áp cổng phân loại tài chính/không-tài chính lên nó. Giữ nguyên giá trị
# này khớp với ParseNaturalLanguageCommandHandler.ManualEntryPackageName phía backend.
MANUAL_ENTRY_PACKAGE = "manual_entry"


@dataclass(frozen=True)
class ClassificationOutcome:
    label: ClassifierLabel
    confidence: float
    method: ExtractionMethod
    model_version: str | None = None


class Classifier:
    def __init__(self, registry: ModelRegistry) -> None:
        self._registry = registry

    def classify(self, text: str, package_name: str) -> ClassificationOutcome:
        if package_name == MANUAL_ENTRY_PACKAGE:
            return _classify_manual_entry(text)

        model = self._registry.get(ModelStage.CLASSIFIER)
        if model is not None:
            label, confidence = model.predict(normalize_for_model(text))
            return ClassificationOutcome(
                label=ClassifierLabel(label),
                confidence=confidence,
                method=ExtractionMethod.MODEL,
                model_version=model.version,
            )
        return classify_by_rules(text)


def _classify_manual_entry(text: str) -> ClassificationOutcome:
    """Chỉ hỏi: có đọc được số tiền không. Không có thì chưa đủ để dựng giao dịch."""
    if any(c.has_marker for c in find_amount_candidates(text)):
        return ClassificationOutcome(
            ClassifierLabel.FINANCIAL, table.get("classifier.manual_entry_financial"), ExtractionMethod.RULE
        )
    return ClassificationOutcome(ClassifierLabel.UNCERTAIN, table.get("classifier.manual_entry_uncertain"), ExtractionMethod.RULE)


def classify_by_rules(text: str, package_name: str = "") -> ClassificationOutcome:
    if package_name == MANUAL_ENTRY_PACKAGE:
        return _classify_manual_entry(text)

    lowered = text.lower()

    if notification_vocab.has_non_financial_marker(lowered):
        return ClassificationOutcome(
            ClassifierLabel.NON_FINANCIAL, table.get("classifier.negative_marker"), ExtractionMethod.RULE
        )

    # "Có tiền" nghĩa là có con số MANG ĐƠN VỊ (75k, 250.000đ, 2,500,000 VND). Một dãy số
    # trần thì thường là mã OTP, số tài khoản hay ngày tháng.
    has_amount = any(c.has_marker for c in find_amount_candidates(text))
    signals = notification_vocab.financial_signals(lowered)

    if has_amount and signals >= 2:
        return ClassificationOutcome(
            ClassifierLabel.FINANCIAL, table.get("classifier.financial_strong"), ExtractionMethod.RULE
        )
    if has_amount and signals == 1:
        return ClassificationOutcome(
            ClassifierLabel.FINANCIAL, table.get("classifier.financial_weak"), ExtractionMethod.RULE
        )
    if has_amount:
        # Có số tiền nhưng không có bối cảnh giao dịch nào: đẩy sang uncertain để backend
        # vẫn hỏi người dùng thay vì im lặng bỏ qua một giao dịch thật.
        return ClassificationOutcome(
            ClassifierLabel.UNCERTAIN, table.get("classifier.uncertain"), ExtractionMethod.RULE
        )
    return ClassificationOutcome(
        ClassifierLabel.NON_FINANCIAL, table.get("classifier.non_financial_no_amount"), ExtractionMethod.RULE
    )
