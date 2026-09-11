"""production feedback

Nhóm production + feedback — nhật ký chạy thật và vòng phản hồi người dùng.

pipeline_requests vừa là log vận hành vừa là nguồn dữ liệu của Duplicate Detector
(ARCHITECTURE.md §3.2 truy vấn cửa sổ 5 phút trên chính bảng này), nên nó LƯU
amount_cents — AGENTS.md §3.2 cấm log số tiền, không cấm lưu.

Revision ID: 0004
Revises: 0003
"""

import sqlalchemy as sa

from alembic import op

revision: str = "0004"
down_revision: str | None = "0003"
branch_labels: None = None
depends_on: None = None


def upgrade() -> None:
    op.create_table(
        "pipeline_requests",
        sa.Column(
            "id", sa.UUID(), server_default=sa.text("gen_random_uuid()"), nullable=False
        ),
        sa.Column("backend_request_id", sa.UUID(), nullable=False),
        sa.Column("user_id_hash", sa.String(length=64), nullable=False),
        sa.Column("package_name", sa.String(length=120), nullable=False),
        sa.Column("received_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("pipeline_result", sa.String(length=32), nullable=False),
        sa.Column("classifier_label", sa.String(length=32), nullable=True),
        sa.Column("classifier_confidence", sa.Float(), nullable=True),
        sa.Column("amount_cents", sa.BigInteger(), nullable=True),
        sa.Column("transaction_type", sa.String(length=16), nullable=True),
        sa.Column("merchant_name", sa.Text(), nullable=True),
        sa.Column("description", sa.Text(), nullable=True),
        sa.Column("transacted_at", sa.DateTime(timezone=True), nullable=True),
        sa.Column("balance_after_cents", sa.BigInteger(), nullable=True),
        sa.Column("extraction_confidence", sa.Float(), nullable=True),
        sa.Column("extraction_method", sa.String(length=16), nullable=True),
        sa.Column("matched_pattern_name", sa.String(length=120), nullable=True),
        sa.Column("category_slug", sa.String(length=32), nullable=True),
        sa.Column("categorization_confidence", sa.Float(), nullable=True),
        sa.Column(
            "is_potential_duplicate",
            sa.Boolean(),
            server_default=sa.text("false"),
            nullable=False,
        ),
        sa.Column("duplicate_of_request_id", sa.UUID(), nullable=True),
        sa.Column("classifier_version", sa.String(length=32), nullable=True),
        sa.Column("extractor_version", sa.String(length=32), nullable=True),
        sa.Column("categorizer_version", sa.String(length=32), nullable=True),
        sa.Column("raw_sample_id", sa.UUID(), nullable=True),
        sa.Column("processing_ms", sa.Integer(), nullable=True),
        sa.Column("error_message", sa.Text(), nullable=True),
        sa.Column(
            "created_at",
            sa.DateTime(timezone=True),
            server_default=sa.text("now()"),
            nullable=False,
        ),
        sa.CheckConstraint(
            "category_slug IS NULL OR category_slug IN ('food', 'transport', 'shopping', 'education', 'housing', 'bills', 'entertainment', 'health', 'family', 'income', 'other')",
            name="chk_pipeline_requests_category_slug",
        ),
        sa.CheckConstraint(
            "classifier_label IS NULL OR classifier_label IN ('financial', 'non_financial', 'uncertain')",
            name="chk_pipeline_requests_classifier_label",
        ),
        sa.CheckConstraint(
            "extraction_method IS NULL OR extraction_method IN ('rule', 'model')",
            name="chk_pipeline_requests_extraction_method",
        ),
        sa.CheckConstraint(
            "pipeline_result IN ('financial', 'non_financial', 'uncertain', 'extraction_failed', 'error')",
            name="chk_pipeline_requests_result",
        ),
        sa.CheckConstraint(
            "transaction_type IS NULL OR transaction_type IN ('debit', 'credit')",
            name="chk_pipeline_requests_transaction_type",
        ),
        sa.ForeignKeyConstraint(
            ["raw_sample_id"], ["raw_samples.id"], ondelete="SET NULL"
        ),
        sa.PrimaryKeyConstraint("id"),
        sa.UniqueConstraint(
            "backend_request_id", name="uq_pipeline_requests_backend_id"
        ),
    )
    op.create_index(
        "idx_pipeline_requests_created_at",
        "pipeline_requests",
        ["created_at"],
        unique=False,
    )
    op.create_index(
        "idx_pipeline_requests_dedup",
        "pipeline_requests",
        ["user_id_hash", "transacted_at"],
        unique=False,
    )
    op.create_table(
        "feedback_batch_jobs",
        sa.Column(
            "id", sa.UUID(), server_default=sa.text("gen_random_uuid()"), nullable=False
        ),
        sa.Column("status", sa.String(length=16), nullable=False),
        sa.Column("started_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("finished_at", sa.DateTime(timezone=True), nullable=True),
        sa.Column(
            "feedback_count", sa.Integer(), server_default=sa.text("0"), nullable=False
        ),
        sa.Column(
            "created_labeled_samples",
            sa.Integer(),
            server_default=sa.text("0"),
            nullable=False,
        ),
        sa.Column("error_message", sa.Text(), nullable=True),
        sa.Column(
            "created_at",
            sa.DateTime(timezone=True),
            server_default=sa.text("now()"),
            nullable=False,
        ),
        sa.CheckConstraint(
            "status IN ('pending', 'running', 'succeeded', 'failed')",
            name="chk_feedback_batch_jobs_status",
        ),
        sa.PrimaryKeyConstraint("id"),
    )
    op.create_table(
        "user_feedback",
        sa.Column(
            "id", sa.UUID(), server_default=sa.text("gen_random_uuid()"), nullable=False
        ),
        sa.Column("backend_transaction_id_hash", sa.String(length=64), nullable=False),
        sa.Column("user_id_hash", sa.String(length=64), nullable=False),
        sa.Column("pipeline_request_id", sa.UUID(), nullable=True),
        sa.Column("package_name", sa.String(length=120), nullable=False),
        sa.Column("predicted_category", sa.String(length=32), nullable=True),
        sa.Column("corrected_category", sa.String(length=32), nullable=True),
        sa.Column("feedback_type", sa.String(length=32), nullable=False),
        sa.Column("processed_at", sa.DateTime(timezone=True), nullable=True),
        sa.Column("batch_job_id", sa.UUID(), nullable=True),
        sa.Column(
            "created_at",
            sa.DateTime(timezone=True),
            server_default=sa.text("now()"),
            nullable=False,
        ),
        sa.CheckConstraint(
            "corrected_category IS NULL OR corrected_category IN ('food', 'transport', 'shopping', 'education', 'housing', 'bills', 'entertainment', 'health', 'family', 'income', 'other')",
            name="chk_user_feedback_corrected_category",
        ),
        sa.CheckConstraint(
            "feedback_type IN ('category_correction', 'amount_correction', 'not_a_transaction')",
            name="chk_user_feedback_type",
        ),
        sa.CheckConstraint(
            "predicted_category IS NULL OR predicted_category IN ('food', 'transport', 'shopping', 'education', 'housing', 'bills', 'entertainment', 'health', 'family', 'income', 'other')",
            name="chk_user_feedback_predicted_category",
        ),
        sa.ForeignKeyConstraint(
            ["batch_job_id"], ["feedback_batch_jobs.id"], ondelete="SET NULL"
        ),
        sa.PrimaryKeyConstraint("id"),
    )
    op.create_index(
        "idx_user_feedback_pipeline_request",
        "user_feedback",
        ["pipeline_request_id"],
        unique=False,
    )
    op.create_index(
        "idx_user_feedback_unprocessed",
        "user_feedback",
        ["created_at"],
        unique=False,
        postgresql_where=sa.text("processed_at IS NULL"),
    )


def downgrade() -> None:
    op.drop_table("user_feedback")
    op.drop_table("feedback_batch_jobs")
    op.drop_table("pipeline_requests")
