"""Stage 1 — thông báo này có phải giao dịch tài chính không.

Model (nếu đã promote) quyết định; chưa có model thì rơi về luật. Nhánh luật không phải
đồ tạm bợ: nó là lớp đỡ khi gặp ngân hàng chưa từng thấy trong tập train, và là thứ chạy
ở mọi lần triển khai mới trước khi kịp train.
"""

from __future__ import annotations

from dataclasses import dataclass

from app.core.enums import ClassifierLabel, ExtractionMethod, ModelStage
from app.pipeline.models.model_registry import ModelRegistry
from app.utils.text_utils import find_amount_candidates, normalize_for_model

# Có mặt là gần như chắc chắn KHÔNG phải giao dịch, kể cả khi trong câu có con số.
_NEGATIVE_MARKERS: tuple[str, ...] = (
    "otp",
    "ma xac thuc",
    "mã xác thực",
    "mat khau",
    "mật khẩu",
    "khuyen mai",
    "khuyến mãi",
    "uu dai",
    "ưu đãi",
    "giam gia",
    "giảm giá",
    "chuc mung nam moi",
    "chúc mừng năm mới",
    "chuc tet",
    "chúc tết",
    "bao tri",
    "bảo trì",
    "cap nhat ung dung",
    "cập nhật ứng dụng",
    "dang nhap",
    "đăng nhập",
    "tich diem",
    "tích điểm",
    "nhac no",
    "nhắc nợ",
    "the le",
    "thể lệ",
    "trung thuong",
    "trúng thưởng",
    "khong chia se",
    "không chia sẻ",
    "hotline",
    "the nang cap",
)

# Dấu hiệu của một biến động số dư thật.
_POSITIVE_MARKERS: tuple[str, ...] = (
    "gd:",
    "sd:",
    "so du",
    "số dư",
    "bien dong so du",
    "biến động số dư",
    "giao dich thanh cong",
    "giao dịch thành công",
    "thanh toan thanh cong",
    "thanh toán thành công",
    "thanh toan",
    "thanh toán",
    "chuyen khoan",
    "chuyển khoản",
    "chuyen tien",
    "chuyển tiền",
    "nap tien",
    "nạp tiền",
    "rut tien",
    "rút tiền",
    "ban da nhan",
    "bạn đã nhận",
    "ban da thanh toan",
    "bạn đã thanh toán",
    "tra luong",
    "trả lương",
    "vnd",
    "noi dung",
    "nội dung",
)


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
            ClassifierLabel.FINANCIAL, 0.90, ExtractionMethod.RULE
        )
    return ClassificationOutcome(ClassifierLabel.UNCERTAIN, 0.40, ExtractionMethod.RULE)


def classify_by_rules(text: str, package_name: str = "") -> ClassificationOutcome:
    if package_name == MANUAL_ENTRY_PACKAGE:
        return _classify_manual_entry(text)

    lowered = text.lower()

    if any(marker in lowered for marker in _NEGATIVE_MARKERS):
        return ClassificationOutcome(
            ClassifierLabel.NON_FINANCIAL, 0.95, ExtractionMethod.RULE
        )

    # "Có tiền" nghĩa là có con số MANG ĐƠN VỊ (75k, 250.000đ, 2,500,000 VND). Một dãy số
    # trần thì thường là mã OTP, số tài khoản hay ngày tháng.
    has_amount = any(c.has_marker for c in find_amount_candidates(text))
    signals = sum(1 for marker in _POSITIVE_MARKERS if marker in lowered)

    if has_amount and signals >= 2:
        return ClassificationOutcome(
            ClassifierLabel.FINANCIAL, 0.92, ExtractionMethod.RULE
        )
    if has_amount and signals == 1:
        return ClassificationOutcome(
            ClassifierLabel.FINANCIAL, 0.75, ExtractionMethod.RULE
        )
    if has_amount:
        # Có số tiền nhưng không có bối cảnh giao dịch nào: đẩy sang uncertain để backend
        # vẫn hỏi người dùng thay vì im lặng bỏ qua một giao dịch thật.
        return ClassificationOutcome(
            ClassifierLabel.UNCERTAIN, 0.50, ExtractionMethod.RULE
        )
    return ClassificationOutcome(
        ClassifierLabel.NON_FINANCIAL, 0.80, ExtractionMethod.RULE
    )
