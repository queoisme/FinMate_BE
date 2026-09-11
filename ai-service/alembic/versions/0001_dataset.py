"""dataset

Nhóm dataset — nguồn dữ liệu cho cả pipeline lẫn training.

provider_patterns là bảng duy nhất pipeline production đọc ở nhóm này: nó chứa regex
bóc trường theo từng template ngân hàng. Ba bảng còn lại phục vụ training.

Gộp 4 bảng vào một migration vì chúng ra đời cùng lúc và ràng buộc FK lẫn nhau —
cùng lý do đã ghi ở Phase 1/2 backend, tách ra sẽ thành migration rỗng.

Revision ID: 0001
Revises: (gốc)
"""

import sqlalchemy as sa

from alembic import op

revision: str = "0001"
down_revision: str | None = None
branch_labels: None = None
depends_on: None = None


def upgrade() -> None:
    op.create_table(
        "provider_patterns",
        sa.Column(
            "id", sa.UUID(), server_default=sa.text("gen_random_uuid()"), nullable=False
        ),
        sa.Column("provider_key", sa.String(length=120), nullable=False),
        sa.Column("package_name", sa.String(length=120), nullable=False),
        sa.Column("pattern_name", sa.String(length=120), nullable=False),
        sa.Column("regex", sa.Text(), nullable=False),
        sa.Column("transaction_type", sa.String(length=16), nullable=True),
        sa.Column(
            "priority", sa.Integer(), server_default=sa.text("100"), nullable=False
        ),
        sa.Column("sample_text", sa.Text(), nullable=True),
        sa.Column(
            "is_active", sa.Boolean(), server_default=sa.text("true"), nullable=False
        ),
        sa.Column("notes", sa.Text(), nullable=True),
        sa.Column(
            "created_at",
            sa.DateTime(timezone=True),
            server_default=sa.text("now()"),
            nullable=False,
        ),
        sa.Column(
            "updated_at",
            sa.DateTime(timezone=True),
            server_default=sa.text("now()"),
            nullable=False,
        ),
        sa.CheckConstraint(
            "transaction_type IS NULL OR transaction_type IN ('debit', 'credit')",
            name="chk_provider_patterns_transaction_type",
        ),
        sa.PrimaryKeyConstraint("id"),
        sa.UniqueConstraint(
            "package_name", "pattern_name", name="uq_provider_patterns_name"
        ),
    )
    op.create_index(
        "idx_provider_patterns_lookup",
        "provider_patterns",
        ["package_name", "is_active", "priority"],
        unique=False,
    )
    op.create_table(
        "raw_samples",
        sa.Column(
            "id", sa.UUID(), server_default=sa.text("gen_random_uuid()"), nullable=False
        ),
        sa.Column("source", sa.String(length=32), nullable=False),
        sa.Column("package_name", sa.String(length=120), nullable=False),
        sa.Column("notification_title", sa.Text(), nullable=True),
        sa.Column("notification_body", sa.Text(), nullable=False),
        sa.Column("received_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("user_id_hash", sa.String(length=64), nullable=True),
        sa.Column("content_hash", sa.String(length=64), nullable=False),
        sa.Column(
            "created_at",
            sa.DateTime(timezone=True),
            server_default=sa.text("now()"),
            nullable=False,
        ),
        sa.CheckConstraint(
            "source IN ('notification', 'manual_entry', 'seed')",
            name="chk_raw_samples_source",
        ),
        sa.PrimaryKeyConstraint("id"),
        sa.UniqueConstraint("content_hash", name="uq_raw_samples_content_hash"),
    )
    op.create_index(
        "idx_raw_samples_created_at", "raw_samples", ["created_at"], unique=False
    )
    op.create_index(
        "idx_raw_samples_package_name", "raw_samples", ["package_name"], unique=False
    )
    op.create_table(
        "labeled_samples",
        sa.Column(
            "id", sa.UUID(), server_default=sa.text("gen_random_uuid()"), nullable=False
        ),
        sa.Column("raw_sample_id", sa.UUID(), nullable=False),
        sa.Column("is_financial", sa.Boolean(), nullable=False),
        sa.Column("transaction_type", sa.String(length=16), nullable=True),
        sa.Column("amount_cents", sa.BigInteger(), nullable=True),
        sa.Column("merchant_name", sa.Text(), nullable=True),
        sa.Column("category_slug", sa.String(length=32), nullable=True),
        sa.Column("labeled_by", sa.String(length=32), nullable=False),
        sa.Column(
            "labeled_at",
            sa.DateTime(timezone=True),
            server_default=sa.text("now()"),
            nullable=False,
        ),
        sa.Column(
            "is_gold", sa.Boolean(), server_default=sa.text("false"), nullable=False
        ),
        sa.CheckConstraint(
            "category_slug IS NULL OR category_slug IN ('food', 'transport', 'shopping', 'education', 'housing', 'bills', 'entertainment', 'health', 'family', 'income', 'other')",
            name="chk_labeled_samples_category_slug",
        ),
        sa.CheckConstraint(
            "labeled_by IN ('seed', 'user_feedback', 'human')",
            name="chk_labeled_samples_labeled_by",
        ),
        sa.CheckConstraint(
            "transaction_type IS NULL OR transaction_type IN ('debit', 'credit')",
            name="chk_labeled_samples_transaction_type",
        ),
        sa.CheckConstraint(
            "is_financial OR (amount_cents IS NULL AND category_slug IS NULL AND transaction_type IS NULL)",
            name="chk_labeled_samples_non_financial_shape",
        ),
        sa.ForeignKeyConstraint(
            ["raw_sample_id"], ["raw_samples.id"], ondelete="CASCADE"
        ),
        sa.PrimaryKeyConstraint("id"),
        sa.UniqueConstraint("raw_sample_id", name="uq_labeled_samples_raw_sample"),
    )
    op.create_index(
        "idx_labeled_samples_category",
        "labeled_samples",
        ["category_slug"],
        unique=False,
    )
    op.create_table(
        "sample_splits",
        sa.Column(
            "id", sa.UUID(), server_default=sa.text("gen_random_uuid()"), nullable=False
        ),
        sa.Column("labeled_sample_id", sa.UUID(), nullable=False),
        sa.Column("split", sa.String(length=16), nullable=False),
        sa.Column("split_seed", sa.Integer(), nullable=False),
        sa.Column(
            "assigned_at",
            sa.DateTime(timezone=True),
            server_default=sa.text("now()"),
            nullable=False,
        ),
        sa.CheckConstraint(
            "split IN ('train', 'val', 'test')", name="chk_sample_splits_split"
        ),
        sa.ForeignKeyConstraint(
            ["labeled_sample_id"], ["labeled_samples.id"], ondelete="CASCADE"
        ),
        sa.PrimaryKeyConstraint("id"),
        sa.UniqueConstraint(
            "labeled_sample_id", name="uq_sample_splits_labeled_sample"
        ),
    )
    op.create_index("idx_sample_splits_split", "sample_splits", ["split"], unique=False)


def downgrade() -> None:
    op.drop_table("sample_splits")
    op.drop_table("labeled_samples")
    op.drop_table("raw_samples")
    op.drop_table("provider_patterns")
