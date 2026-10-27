"""fk indexes

Thêm chỉ mục cho 4 cột khóa ngoại còn thiếu, để xóa dòng cha không phải quét cả bảng con. CHỈ THÊM chỉ mục, không đổi bảng/cột.

Revision ID: 0020
Revises: 0019
"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


# revision identifiers, used by Alembic.
revision: str = '0020'
down_revision: Union[str, Sequence[str], None] = '0019'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.create_index('idx_pipeline_requests_raw_sample', 'pipeline_requests', ['raw_sample_id'], unique=False, postgresql_where=sa.text('raw_sample_id IS NOT NULL'))
    op.create_index('idx_model_versions_training_job', 'model_versions', ['training_job_id'], unique=False, postgresql_where=sa.text('training_job_id IS NOT NULL'))
    op.create_index('idx_user_feedback_batch_job', 'user_feedback', ['batch_job_id'], unique=False, postgresql_where=sa.text('batch_job_id IS NOT NULL'))
    op.create_index('idx_evaluation_predictions_labeled_sample', 'evaluation_predictions', ['labeled_sample_id'], unique=False)


def downgrade() -> None:
    op.drop_index('idx_evaluation_predictions_labeled_sample', table_name='evaluation_predictions')
    op.drop_index('idx_user_feedback_batch_job', table_name='user_feedback', postgresql_where=sa.text('batch_job_id IS NOT NULL'))
    op.drop_index('idx_model_versions_training_job', table_name='model_versions', postgresql_where=sa.text('training_job_id IS NOT NULL'))
    op.drop_index('idx_pipeline_requests_raw_sample', table_name='pipeline_requests', postgresql_where=sa.text('raw_sample_id IS NOT NULL'))
