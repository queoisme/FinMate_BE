"""Bảng ``bill_*``: kiến thức nhận diện và dữ liệu huấn luyện của luồng quét hóa đơn.

Nằm cạnh bộ bảng thông báo ngân hàng nhưng KHÔNG dùng chung tên với chúng: bộ bảng cũ có ràng buộc riêng (11 slug của backend, nguồn mẫu cố
định, người gắn nhãn cố định), còn luồng hóa đơn dùng 9 nhóm của bộ luật bill, nguồn ``bill_scan`` và nhãn do người dùng, bộ chuẩn hay bill giả.
Gộp hai bên vào một bảng nghĩa là nới ràng buộc của bảng cũ hoặc ép 9 nhóm thành 11 slug (mất phân biệt Ăn uống / Siêu thị).

Nội dung:
  - kiến thức: ``bill_brands`` (thương hiệu -> nhóm), ``bill_keywords`` (từ khóa theo cửa hàng / mặt hàng / chat), ``bill_user_merchant_category``
    (cửa hàng người dùng đã chốt, theo ``user_id_hash``);
  - dữ liệu huấn luyện: ``bill_raw_samples`` + ``bill_labeled_samples`` (chữ đã lọc dữ liệu cá nhân, không ảnh), và lịch sử huấn luyện
    ``bill_training_jobs``, ``bill_model_versions``, ``bill_evaluation_runs``, ``bill_evaluation_category_metrics``;
  - ``bill_unknown_scans``: bill lạ người dùng đồng ý chia sẻ, chờ người duyệt.

Hai view ``bill_unknown_merchants`` và ``bill_merchant_votes`` chỉ nằm trong migration (view không phải model ORM).
"""

from sqlalchemy import (
    BigInteger,
    Boolean,
    CheckConstraint,
    Float,
    ForeignKey,
    Index,
    Integer,
    String,
    Text,
    UniqueConstraint,
    text,
)
from sqlalchemy.dialects.postgresql import JSONB, UUID
from sqlalchemy.orm import Mapped, mapped_column

from app.core.enums import CATEGORY_SLUGS
from app.db.base import Base, created_at, long_text, timestamptz, uuid_pk

BILL_CATEGORY_GROUPS = (
    "dining",
    "transport",
    "shopping",
    "groceries",
    "housing",
    "utilities",
    "healthcare",
    "entertainment",
    "other",
)
BILL_LABELED_BY = ("user", "imported", "gold", "synthetic_ocr")
BILL_KEYWORD_KINDS = ("merchant", "product", "chat")
# Danh mục người dùng chọn khi lưu một khoản chi từ hóa đơn: slug của backend, trừ "income" (hóa đơn là tiền ra).
BILL_CHOICE_SLUGS = tuple(slug for slug in CATEGORY_SLUGS if slug != "income")


def _in(column: str, values: tuple[str, ...]) -> str:
    return f"{column} IN ({', '.join(repr(v) for v in values)})"


class BillRawSample(Base):
    """Chữ ĐÃ lọc dữ liệu cá nhân của một hóa đơn đã gắn nhãn. Không ảnh, không chữ gốc."""

    __tablename__ = "bill_raw_samples"

    id: Mapped[uuid_pk]
    source: Mapped[str] = mapped_column(String(32), nullable=False)
    package_name: Mapped[str] = mapped_column(Text, nullable=False)
    notification_title: Mapped[str | None] = mapped_column(Text)  # tên cửa hàng
    notification_body: Mapped[long_text]  # các dòng chữ đã lọc, nối bằng xuống dòng
    received_at: Mapped[timestamptz]
    user_id_hash: Mapped[str | None] = mapped_column(String(64))
    content_hash: Mapped[str] = mapped_column(String(64), nullable=False)
    created_at: Mapped[created_at]

    __table_args__ = (
        UniqueConstraint("content_hash", name="uq_bill_raw_samples_content_hash"),
        Index("idx_bill_raw_samples_source", "source"),
        Index("idx_bill_raw_samples_created_at", "created_at"),
        CheckConstraint("source = 'bill_scan'", name="chk_bill_raw_samples_source"),
    )


class BillLabeledSample(Base):
    __tablename__ = "bill_labeled_samples"

    id: Mapped[uuid_pk]
    raw_sample_id: Mapped[UUID] = mapped_column(
        UUID(as_uuid=True),
        ForeignKey("bill_raw_samples.id", ondelete="CASCADE"),
        nullable=False,
    )
    is_financial: Mapped[bool] = mapped_column(Boolean, nullable=False)
    transaction_type: Mapped[str | None] = mapped_column(String(16))
    amount_cents: Mapped[int | None] = mapped_column(BigInteger)
    merchant_name: Mapped[str | None] = mapped_column(Text)
    category_slug: Mapped[str | None] = mapped_column(String(32))
    labeled_by: Mapped[str] = mapped_column(String(32), nullable=False)
    labeled_at: Mapped[timestamptz] = mapped_column(nullable=False, server_default=text("now()"))
    is_gold: Mapped[bool] = mapped_column(Boolean, nullable=False, server_default=text("false"))

    __table_args__ = (
        UniqueConstraint("raw_sample_id", name="uq_bill_labeled_samples_raw_sample"),
        Index("idx_bill_labeled_samples_category_slug", "category_slug"),
        CheckConstraint(_in("labeled_by", BILL_LABELED_BY), name="chk_bill_labeled_samples_labeled_by"),
        CheckConstraint(
            f"category_slug IS NULL OR {_in('category_slug', BILL_CATEGORY_GROUPS)}",
            name="chk_bill_labeled_samples_category",
        ),
    )


class BillTrainingJob(Base):
    __tablename__ = "bill_training_jobs"

    id: Mapped[uuid_pk]
    stage: Mapped[str] = mapped_column(String(32), nullable=False)
    status: Mapped[str] = mapped_column(String(16), nullable=False)
    params: Mapped[dict | None] = mapped_column(JSONB)
    sample_count: Mapped[int | None] = mapped_column(Integer)
    started_at: Mapped[timestamptz | None]
    finished_at: Mapped[timestamptz | None]
    error_message: Mapped[str | None] = mapped_column(Text)
    created_at: Mapped[created_at]

    __table_args__ = (Index("idx_bill_training_jobs_stage_created", "stage", "created_at"),)


class BillModelVersion(Base):
    __tablename__ = "bill_model_versions"

    id: Mapped[uuid_pk]
    stage: Mapped[str] = mapped_column(String(32), nullable=False)
    version: Mapped[str] = mapped_column(String(32), nullable=False)
    artifact_path: Mapped[str] = mapped_column(Text, nullable=False)
    status: Mapped[str] = mapped_column(String(16), nullable=False)
    metrics: Mapped[dict | None] = mapped_column(JSONB)
    training_job_id: Mapped[UUID | None] = mapped_column(
        UUID(as_uuid=True), ForeignKey("bill_training_jobs.id", ondelete="SET NULL")
    )
    trained_at: Mapped[timestamptz | None]
    notes: Mapped[str | None] = mapped_column(Text)
    created_at: Mapped[created_at]
    updated_at: Mapped[timestamptz] = mapped_column(nullable=False, server_default=text("now()"))

    __table_args__ = (UniqueConstraint("stage", "version", name="uq_bill_model_versions_stage_version"),)


class BillEvaluationRun(Base):
    __tablename__ = "bill_evaluation_runs"

    id: Mapped[uuid_pk]
    model_version_id: Mapped[UUID] = mapped_column(
        UUID(as_uuid=True), ForeignKey("bill_model_versions.id", ondelete="CASCADE"), nullable=False
    )
    split: Mapped[str] = mapped_column(String(16), nullable=False)
    sample_count: Mapped[int] = mapped_column(Integer, nullable=False)
    # Float là điểm số, không phải tiền: lệnh cấm float của AGENTS.md §3.2 nhắm vào amount_cents.
    accuracy: Mapped[float] = mapped_column(Float, nullable=False)
    macro_f1: Mapped[float] = mapped_column(Float, nullable=False)
    metrics: Mapped[dict | None] = mapped_column(JSONB)
    notes: Mapped[str | None] = mapped_column(Text)
    created_at: Mapped[created_at]

    __table_args__ = (Index("idx_bill_evaluation_runs_model_created", "model_version_id", "created_at"),)


class BillEvaluationCategoryMetric(Base):
    __tablename__ = "bill_evaluation_category_metrics"

    id: Mapped[uuid_pk]
    evaluation_run_id: Mapped[UUID] = mapped_column(
        UUID(as_uuid=True), ForeignKey("bill_evaluation_runs.id", ondelete="CASCADE"), nullable=False
    )
    label: Mapped[str] = mapped_column(String(64), nullable=False)
    precision: Mapped[float] = mapped_column(Float, nullable=False)
    recall: Mapped[float] = mapped_column(Float, nullable=False)
    f1: Mapped[float] = mapped_column(Float, nullable=False)
    support: Mapped[int] = mapped_column(Integer, nullable=False)

    __table_args__ = (UniqueConstraint("evaluation_run_id", "label", name="uq_bill_eval_category_metrics"),)


class BillUserMerchantCategory(Base):
    """Cửa hàng người dùng đã chốt danh mục, theo ``user_id_hash`` (SHA-256 của user_id, không phải id thật)."""

    __tablename__ = "bill_user_merchant_category"

    id: Mapped[uuid_pk]
    user_id_hash: Mapped[str] = mapped_column(String(64), nullable=False)
    merchant_key: Mapped[str] = mapped_column(Text, nullable=False)  # tên bỏ dấu, chữ thường, gọn khoảng trắng
    display_name: Mapped[str | None] = mapped_column(Text)
    category_slug: Mapped[str] = mapped_column(String(32), nullable=False)  # 9 nhóm của bộ luật bill
    created_at: Mapped[created_at]
    updated_at: Mapped[timestamptz] = mapped_column(nullable=False, server_default=text("now()"))

    __table_args__ = (
        UniqueConstraint("user_id_hash", "merchant_key", name="uq_bill_user_merchant"),
        Index("idx_bill_user_merchant_vote", "merchant_key", "category_slug"),
    )


class BillUserMerchantChoice(Base):
    """Cửa hàng người dùng đã chốt danh mục KHI LƯU GIAO DỊCH, theo ``user_id_hash``. Lưu đúng slug backend (11 slug) người dùng đã chọn.

    Khác ``bill_user_merchant_category`` (nhóm 9 nhóm của bộ luật bill, dữ liệu cũ): người dùng chỉ chọn được trong danh mục của backend, nên
    nhớ nguyên slug đó là không mất gì, còn đổi ngược về 9 nhóm thì mất (food là Ăn uống hay Siêu thị?). Tra cứu xem bảng này trước, bảng cũ sau.
    """

    __tablename__ = "bill_user_merchant_choice"

    id: Mapped[uuid_pk]
    user_id_hash: Mapped[str] = mapped_column(String(64), nullable=False)
    merchant_key: Mapped[str] = mapped_column(Text, nullable=False)  # tên bỏ dấu, chữ thường, gọn khoảng trắng
    display_name: Mapped[str | None] = mapped_column(Text)
    category_slug: Mapped[str] = mapped_column(String(32), nullable=False)
    created_at: Mapped[created_at]
    updated_at: Mapped[timestamptz] = mapped_column(nullable=False, server_default=text("now()"))

    __table_args__ = (
        UniqueConstraint("user_id_hash", "merchant_key", name="uq_bill_user_merchant_choice"),
        CheckConstraint(_in("category_slug", BILL_CHOICE_SLUGS), name="chk_bill_user_merchant_choice_slug"),
    )


class BillBrand(Base):
    __tablename__ = "bill_brands"

    id: Mapped[uuid_pk]
    brand_key: Mapped[str] = mapped_column(Text, nullable=False)  # tên bỏ dấu, chữ thường
    category_slug: Mapped[str] = mapped_column(String(32), nullable=False)
    display_name: Mapped[str] = mapped_column(Text, nullable=False)
    whole_word: Mapped[bool] = mapped_column(Boolean, nullable=False, server_default=text("false"))
    is_active: Mapped[bool] = mapped_column(Boolean, nullable=False, server_default=text("true"))
    source: Mapped[str] = mapped_column(String(16), nullable=False, server_default=text("'seed'"))
    created_at: Mapped[created_at]
    updated_at: Mapped[timestamptz] = mapped_column(nullable=False, server_default=text("now()"))

    __table_args__ = (UniqueConstraint("brand_key", name="uq_bill_brands_brand_key"),)


class BillKeyword(Base):
    __tablename__ = "bill_keywords"

    id: Mapped[uuid_pk]
    kind: Mapped[str] = mapped_column(String(16), nullable=False)
    category_slug: Mapped[str] = mapped_column(String(32), nullable=False)
    keyword: Mapped[str] = mapped_column(Text, nullable=False)  # đã bỏ dấu, chữ thường
    # Nhóm có số nhỏ hơn được xét trước khi một tên trùng nhiều nhóm (giữ đúng thứ tự ưu tiên trong code).
    category_priority: Mapped[int] = mapped_column(Integer, nullable=False, server_default=text("100"))
    is_active: Mapped[bool] = mapped_column(Boolean, nullable=False, server_default=text("true"))
    source: Mapped[str] = mapped_column(String(16), nullable=False, server_default=text("'seed'"))
    created_at: Mapped[created_at]

    __table_args__ = (
        UniqueConstraint("kind", "category_slug", "keyword", name="uq_bill_keyword"),
        CheckConstraint(_in("kind", BILL_KEYWORD_KINDS), name="chk_bill_keywords_kind"),
    )


class BillAiInstruction(Base):
    """Luật phân loại từng món cho Gemini (nhóm, luật chốt khi nhập nhằng, ví dụ). Sửa ở DB, không cần build lại.

    Đúng MỘT dòng ``is_active`` (partial unique index); phần nhiệm vụ/an toàn và schema kết quả do code giữ, không nằm ở đây.
    """

    __tablename__ = "bill_ai_instructions"

    id: Mapped[uuid_pk]
    instruction_text: Mapped[str] = mapped_column(Text, nullable=False)
    is_active: Mapped[bool] = mapped_column(Boolean, nullable=False, server_default=text("true"))
    updated_at: Mapped[timestamptz] = mapped_column(nullable=False, server_default=text("now()"))

    __table_args__ = (
        Index("uq_bill_ai_instructions_one_active", "is_active", unique=True, postgresql_where=text("is_active")),
    )


class BillUnknownScan(Base):
    """Bill lạ: chưa nhận ra cửa hàng. Chỉ ghi khi người dùng đồng ý chia sẻ; chữ đã lọc, không ảnh."""

    __tablename__ = "bill_unknown_scans"

    id: Mapped[uuid_pk]
    user_id_hash: Mapped[str] = mapped_column(String(64), nullable=False)
    merchant_read: Mapped[str | None] = mapped_column(Text)
    merchant_key: Mapped[str] = mapped_column(Text, nullable=False, server_default=text("''"))
    predicted_category: Mapped[str | None] = mapped_column(String(32))
    predicted_source: Mapped[str | None] = mapped_column(String(16))
    confidence: Mapped[float | None] = mapped_column(Float)
    total_amount: Mapped[int | None] = mapped_column(BigInteger)
    lines: Mapped[long_text]
    content_hash: Mapped[str] = mapped_column(String(64), nullable=False)
    seen_count: Mapped[int] = mapped_column(Integer, nullable=False, server_default=text("1"))
    status: Mapped[str] = mapped_column(String(16), nullable=False, server_default=text("'new'"))
    first_seen_at: Mapped[timestamptz] = mapped_column(nullable=False, server_default=text("now()"))
    last_seen_at: Mapped[timestamptz] = mapped_column(nullable=False, server_default=text("now()"))

    __table_args__ = (
        UniqueConstraint("content_hash", name="uq_bill_unknown_scans_content_hash"),
        Index("idx_bill_unknown_scans_status_merchant", "status", "merchant_key"),
        CheckConstraint("status IN ('new', 'labeled', 'ignored')", name="chk_bill_unknown_scans_status"),
    )


__all__ = [
    "BILL_CATEGORY_GROUPS",
    "BillBrand",
    "BillEvaluationCategoryMetric",
    "BillEvaluationRun",
    "BillKeyword",
    "BillLabeledSample",
    "BillModelVersion",
    "BillRawSample",
    "BillTrainingJob",
    "BillUnknownScan",
    "BillUserMerchantCategory",
    "BillUserMerchantChoice",
]
