"""nlu examples

Thêm ``nlu_examples``: câu mẫu của Rasa NLU (huấn luyện và đo) lưu trong DB, mỗi câu đúng một dòng (``text_key`` duy nhất) nên một câu không thể vừa ở tập
train vừa ở tập test. CHỈ THÊM một bảng.

Revision ID: 0017
Revises: 0016
"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


# revision identifiers, used by Alembic.
revision: str = '0017'
down_revision: Union[str, Sequence[str], None] = '0016'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.create_table('nlu_examples',
    sa.Column('id', sa.UUID(), server_default=sa.text('gen_random_uuid()'), nullable=False),
    sa.Column('text', sa.Text(), nullable=False),
    sa.Column('text_key', sa.String(length=400), nullable=False),
    sa.Column('intent', sa.String(length=32), nullable=False),
    sa.Column('split', sa.String(length=16), nullable=False),
    sa.Column('tag', sa.String(length=40), nullable=True),
    sa.Column('source', sa.String(length=32), nullable=False),
    sa.Column('is_active', sa.Boolean(), server_default=sa.text('true'), nullable=False),
    sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.CheckConstraint("split IN ('train', 'val', 'test')", name='chk_nlu_examples_split'),
    sa.CheckConstraint("source IN ('seed', 'user_feedback', 'human')", name='chk_nlu_examples_source'),
    sa.PrimaryKeyConstraint('id'),
    sa.UniqueConstraint('text_key', name='uq_nlu_examples_text_key')
    )
    op.create_index('idx_nlu_examples_split_intent', 'nlu_examples', ['split', 'intent'], unique=False)


def downgrade() -> None:
    op.drop_index('idx_nlu_examples_split_intent', table_name='nlu_examples')
    op.drop_table('nlu_examples')
