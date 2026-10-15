"""bill classify stats

Thêm ``bill_classify_stats``: bộ đếm theo ngày của ``/bill/classify-items`` (món được trả lời bởi bộ nhớ, luật, Gemini...), để biết mức phụ thuộc Gemini
và quyết định có đáng làm mô hình học hay không. CHỈ THÊM một bảng, chỉ chứa số đếm.

Revision ID: 0013
Revises: 0012
"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


# revision identifiers, used by Alembic.
revision: str = '0013'
down_revision: Union[str, Sequence[str], None] = '0012'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.create_table('bill_classify_stats',
    sa.Column('day', sa.Date(), nullable=False),
    sa.Column('metric', sa.String(length=32), nullable=False),
    sa.Column('value', sa.BigInteger(), server_default=sa.text('0'), nullable=False),
    sa.PrimaryKeyConstraint('day', 'metric')
    )


def downgrade() -> None:
    op.drop_table('bill_classify_stats')
