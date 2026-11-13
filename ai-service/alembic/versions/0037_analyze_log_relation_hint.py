"""analyze log relation hint

Phase 81: lưu gợi ý "khoản tiền vào này là hoàn tiền / đảo giao dịch" cạnh kết quả trích xuất, để response trả cho backend và dòng nhật ký
không lệch nhau (response luôn dựng từ dòng đã lưu). Cột rỗng được, dòng cũ giữ NULL.

Revision ID: 0037
Revises: 0036
"""
from typing import Sequence, Union

import sqlalchemy as sa
from alembic import op

revision: str = '0037'
down_revision: Union[str, Sequence[str], None] = '0036'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.add_column('ops_analyze_log', sa.Column('relation_hint', sa.String(length=16), nullable=True))
    op.create_check_constraint(
        'chk_ops_analyze_log_relation_hint', 'ops_analyze_log', "relation_hint IS NULL OR relation_hint IN ('refund', 'reversal')"
    )


def downgrade() -> None:
    op.drop_constraint('chk_ops_analyze_log_relation_hint', 'ops_analyze_log', type_='check')
    op.drop_column('ops_analyze_log', 'relation_hint')
