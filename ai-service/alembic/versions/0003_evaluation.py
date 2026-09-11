"""evaluation

Nhóm evaluation — điểm số của từng model version trước khi được promote.

evaluation_predictions giữ từng dự đoán để đọc lại đúng những câu model đoán sai;
accuracy tổng không chỉ ra được nó hỏng ở danh mục nào.

Revision ID: 0003
Revises: 0002
"""

import sqlalchemy as sa
from sqlalchemy.dialects import postgresql

from alembic import op

revision: str = "0003"
down_revision: str | None = "0002"
branch_labels: None = None
depends_on: None = None


def upgrade() -> None:
    op.create_table(
        "evaluation_runs",
        sa.Column(
            "id", sa.UUID(), server_default=sa.text("gen_random_uuid()"), nullable=False
        ),
        sa.Column("model_version_id", sa.UUID(), nullable=False),
        sa.Column("split", sa.String(length=16), nullable=False),
        sa.Column("sample_count", sa.Integer(), nullable=False),
        sa.Column("accuracy", sa.Float(), nullable=False),
        sa.Column("macro_f1", sa.Float(), nullable=False),
        sa.Column("metrics", postgresql.JSONB(astext_type=sa.Text()), nullable=True),
        sa.Column("notes", sa.Text(), nullable=True),
        sa.Column(
            "created_at",
            sa.DateTime(timezone=True),
            server_default=sa.text("now()"),
            nullable=False,
        ),
        sa.CheckConstraint(
            "split IN ('train', 'val', 'test')", name="chk_evaluation_runs_split"
        ),
        sa.ForeignKeyConstraint(
            ["model_version_id"], ["model_versions.id"], ondelete="CASCADE"
        ),
        sa.PrimaryKeyConstraint("id"),
    )
    op.create_index(
        "idx_evaluation_runs_model_version",
        "evaluation_runs",
        ["model_version_id", "created_at"],
        unique=False,
    )
    op.create_table(
        "evaluation_category_metrics",
        sa.Column(
            "id", sa.UUID(), server_default=sa.text("gen_random_uuid()"), nullable=False
        ),
        sa.Column("evaluation_run_id", sa.UUID(), nullable=False),
        sa.Column("label", sa.String(length=64), nullable=False),
        sa.Column("precision", sa.Float(), nullable=False),
        sa.Column("recall", sa.Float(), nullable=False),
        sa.Column("f1", sa.Float(), nullable=False),
        sa.Column("support", sa.Integer(), nullable=False),
        sa.ForeignKeyConstraint(
            ["evaluation_run_id"], ["evaluation_runs.id"], ondelete="CASCADE"
        ),
        sa.PrimaryKeyConstraint("id"),
        sa.UniqueConstraint(
            "evaluation_run_id", "label", name="uq_eval_category_metrics"
        ),
    )
    op.create_table(
        "evaluation_predictions",
        sa.Column(
            "id", sa.UUID(), server_default=sa.text("gen_random_uuid()"), nullable=False
        ),
        sa.Column("evaluation_run_id", sa.UUID(), nullable=False),
        sa.Column("labeled_sample_id", sa.UUID(), nullable=False),
        sa.Column("predicted_label", sa.String(length=64), nullable=False),
        sa.Column("true_label", sa.String(length=64), nullable=False),
        sa.Column("confidence", sa.Float(), nullable=False),
        sa.Column("is_correct", sa.Boolean(), nullable=False),
        sa.ForeignKeyConstraint(
            ["evaluation_run_id"], ["evaluation_runs.id"], ondelete="CASCADE"
        ),
        sa.ForeignKeyConstraint(
            ["labeled_sample_id"], ["labeled_samples.id"], ondelete="CASCADE"
        ),
        sa.PrimaryKeyConstraint("id"),
    )
    op.create_index(
        "idx_evaluation_predictions_run",
        "evaluation_predictions",
        ["evaluation_run_id", "is_correct"],
        unique=False,
    )


def downgrade() -> None:
    op.drop_table("evaluation_predictions")
    op.drop_table("evaluation_category_metrics")
    op.drop_table("evaluation_runs")
