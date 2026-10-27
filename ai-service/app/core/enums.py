"""Giá trị enum dùng chung giữa pipeline và AI DB.

Mọi enum ở đây được lưu xuống DB dạng TEXT + CHECK constraint (ARCHITECTURE.md §4.3),
không dùng PostgreSQL ENUM type. Kế thừa ``str`` để so sánh/serialize thẳng ra JSON
mà không phải ``.value`` ở mọi chỗ gọi.
"""

from enum import Enum


class StrEnum(str, Enum):
    """``str`` Enum — Python 3.11 có sẵn ``enum.StrEnum`` nhưng repr khác, giữ bản này
    cho ổn định khi serialize qua Pydantic."""

    def __str__(self) -> str:
        return self.value

    @classmethod
    def values(cls) -> list[str]:
        return [member.value for member in cls]


class PipelineResult(StrEnum):
    """Khớp đúng ``ParsePipelineResult`` phía backend — đổi giá trị ở đây là đổi contract."""

    FINANCIAL = "financial"
    NON_FINANCIAL = "non_financial"
    UNCERTAIN = "uncertain"
    EXTRACTION_FAILED = "extraction_failed"
    ERROR = "error"


class ClassifierLabel(StrEnum):
    FINANCIAL = "financial"
    NON_FINANCIAL = "non_financial"
    UNCERTAIN = "uncertain"


class TransactionType(StrEnum):
    """Chỉ debit/credit. ``transfer`` là khái niệm của backend (chuyển giữa 2 ví của
    chính user, xem ARCHITECTURE.md §0 quyết định #1) — AI Service không bao giờ suy ra
    được điều đó từ một thông báo đơn lẻ nên không có giá trị này."""

    DEBIT = "debit"
    CREDIT = "credit"


class ExtractionMethod(StrEnum):
    RULE = "rule"
    MODEL = "model"


class SampleSource(StrEnum):
    NOTIFICATION = "notification"
    MANUAL_ENTRY = "manual_entry"
    RECEIPT = "receipt"
    SEED = "seed"


class LabelSource(StrEnum):
    SEED = "seed"
    USER_FEEDBACK = "user_feedback"
    HUMAN = "human"


class SplitName(StrEnum):
    TRAIN = "train"
    VAL = "val"
    TEST = "test"


class ModelStage(StrEnum):
    CLASSIFIER = "classifier"
    CATEGORIZER = "categorizer"


class ModelStatus(StrEnum):
    """Vòng đời một phiên bản model (bảng ``ml_model_versions``, dùng chung cho mọi model). ``retired`` là tên cũ ``archived``."""

    CANDIDATE = "candidate"
    ACTIVE = "active"
    RETIRED = "retired"
    REJECTED = "rejected"


class NluModelStatus(StrEnum):
    """Vòng đời một model Rasa NLU (bảng ``nlu_model_versions``). Tách khỏi ``ModelStatus``/``ModelStage``: các vòng lặp ở model_registry/health/stats
    chạy qua ``ModelStage`` và sẽ cố nạp tệp .tar.gz của Rasa như một model sklearn."""

    CANDIDATE = "candidate"
    ACTIVE = "active"
    RETIRED = "retired"
    REJECTED = "rejected"


class NluCandidateStatus(StrEnum):
    """Câu chat chưa rõ đang chờ người duyệt (bảng ``nlu_candidates``). Câu được nhận thì chuyển sang ``nlu_examples`` rồi xóa khỏi bảng chờ, nên không có trạng thái "accepted"."""

    PENDING = "pending"
    REJECTED = "rejected"


class JobStatus(StrEnum):
    PENDING = "pending"
    RUNNING = "running"
    SUCCEEDED = "succeeded"
    FAILED = "failed"


class FeedbackType(StrEnum):
    CATEGORY_CORRECTION = "category_correction"
    AMOUNT_CORRECTION = "amount_correction"
    NOT_A_TRANSACTION = "not_a_transaction"


# 11 slug hệ thống do CategorySeeder của backend seed. Categorizer KHÔNG được trả slug
# ngoài danh sách này: backend tra bằng GetSystemBySlugAsync, slug lạ trả về NULL và
# giao dịch mất danh mục một cách im lặng.
CATEGORY_SLUGS: tuple[str, ...] = (
    "food",
    "transport",
    "shopping",
    "education",
    "housing",
    "bills",
    "entertainment",
    "health",
    "family",
    "income",
    "other",
)

FALLBACK_CATEGORY_SLUG = "other"
