"""``labeled_samples`` + ``sample_splits`` — tập dữ liệu đã gán nhãn và cách chia train/val/test."""

from sqlalchemy import (
    BigInteger,
    Boolean,
    CheckConstraint,
    ForeignKey,
    Index,
    Integer,
    String,
    Text,
    UniqueConstraint,
    text,
)
from sqlalchemy.dialects.postgresql import UUID
from sqlalchemy.orm import Mapped, mapped_column

from app.core.enums import CATEGORY_SLUGS, LabelSource, SplitName, TransactionType
from app.db.base import Base, created_at, enum_check, uuid_pk

_CATEGORY_LIST = ", ".join(f"'{slug}'" for slug in CATEGORY_SLUGS)


class LabeledSample(Base):
    __tablename__ = "labeled_samples"

    id: Mapped[uuid_pk]
    raw_sample_id: Mapped[UUID] = mapped_column(
        UUID(as_uuid=True),
        ForeignKey("raw_samples.id", ondelete="CASCADE"),
        nullable=False,
    )

    # Nhãn cho Classifier.
    is_financial: Mapped[bool] = mapped_column(Boolean, nullable=False)

    # Nhãn cho Extractor/Categorizer — NULL hết khi is_financial = false.
    transaction_type: Mapped[str | None] = mapped_column(String(16))
    amount_cents: Mapped[int | None] = mapped_column(BigInteger)
    merchant_name: Mapped[str | None] = mapped_column(Text)
    category_slug: Mapped[str | None] = mapped_column(String(32))

    labeled_by: Mapped[str] = mapped_column(String(32), nullable=False)
    labeled_at: Mapped[created_at]

    # Nhãn do người soát thủ công — được ưu tiên khi trùng với nhãn máy sinh.
    is_gold: Mapped[bool] = mapped_column(
        Boolean, nullable=False, server_default=text("false")
    )

    __table_args__ = (
        # Một mẫu thô chỉ mang đúng một nhãn: feedback về sau UPDATE dòng này chứ không
        # thêm dòng thứ hai, nếu không cùng một câu vừa "food" vừa "entertainment" đi
        # vào tập train.
        UniqueConstraint("raw_sample_id", name="uq_labeled_samples_raw_sample"),
        Index("idx_labeled_samples_category", "category_slug"),
        enum_check("labeled_by", LabelSource, "chk_labeled_samples_labeled_by"),
        enum_check(
            "transaction_type",
            TransactionType,
            "chk_labeled_samples_transaction_type",
            nullable=True,
        ),
        CheckConstraint(
            f"category_slug IS NULL OR category_slug IN ({_CATEGORY_LIST})",
            name="chk_labeled_samples_category_slug",
        ),
        # Mẫu không phải giao dịch thì không được mang số tiền/danh mục — nhãn âm bị gán
        # lẫn trường giao dịch là cách nhanh nhất để dạy model một quy luật sai.
        CheckConstraint(
            "is_financial OR (amount_cents IS NULL AND category_slug IS NULL"
            " AND transaction_type IS NULL)",
            name="chk_labeled_samples_non_financial_shape",
        ),
    )


class SampleSplit(Base):
    __tablename__ = "sample_splits"

    id: Mapped[uuid_pk]
    labeled_sample_id: Mapped[UUID] = mapped_column(
        UUID(as_uuid=True),
        ForeignKey("labeled_samples.id", ondelete="CASCADE"),
        nullable=False,
    )
    split: Mapped[str] = mapped_column(String(16), nullable=False)

    # Split gán bằng hash tất định của content_hash, KHÔNG random: một mẫu phải nằm mãi ở
    # cùng một tập. Random mỗi lần chạy làm mẫu trôi từ test sang train và điểm đánh giá
    # đẹp lên vì rò rỉ dữ liệu — lỗi im lặng nhất trong ML.
    split_seed: Mapped[int] = mapped_column(Integer, nullable=False)
    assigned_at: Mapped[created_at]

    __table_args__ = (
        UniqueConstraint("labeled_sample_id", name="uq_sample_splits_labeled_sample"),
        Index("idx_sample_splits_split", "split"),
        enum_check("split", SplitName, "chk_sample_splits_split"),
    )


__all__ = ["LabeledSample", "SampleSplit"]
