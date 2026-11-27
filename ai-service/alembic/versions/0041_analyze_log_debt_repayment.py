"""analyze log debt repayment

Phase 90 (BR-DEBT-12 của Phase 86 cũ, S3): gợi ý "khoản này là trả nợ / thu nợ" (``relation_hint = 'debt_repayment'``) cùng độ mạnh (``debt_hint_strength``:
``strong`` / ``weak``). Dòng cũ giữ NULL; CHECK chỉ mở rộng nên không có dòng nào vi phạm.

Revision ID: 0041
Revises: 0040
"""
from collections.abc import Sequence

import sqlalchemy as sa

from alembic import op

revision: str = '0041'
down_revision: str | Sequence[str] | None = '0040'
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    op.drop_constraint('chk_ops_analyze_log_relation_hint', 'ops_analyze_log', type_='check')
    op.create_check_constraint(
        'chk_ops_analyze_log_relation_hint',
        'ops_analyze_log',
        "relation_hint IS NULL OR relation_hint IN ('refund', 'reversal', 'transfer', 'debt_repayment')",
    )
    op.add_column('ops_analyze_log', sa.Column('debt_hint_strength', sa.String(length=8), nullable=True))
    op.create_check_constraint(
        'chk_ops_analyze_log_debt_hint_strength',
        'ops_analyze_log',
        "debt_hint_strength IS NULL OR (relation_hint = 'debt_repayment' AND debt_hint_strength IN ('strong', 'weak'))",
    )


def downgrade() -> None:
    op.drop_constraint('chk_ops_analyze_log_debt_hint_strength', 'ops_analyze_log', type_='check')
    op.drop_column('ops_analyze_log', 'debt_hint_strength')
    op.execute("UPDATE ops_analyze_log SET relation_hint = NULL WHERE relation_hint = 'debt_repayment'")
    op.drop_constraint('chk_ops_analyze_log_relation_hint', 'ops_analyze_log', type_='check')
    op.create_check_constraint(
        'chk_ops_analyze_log_relation_hint', 'ops_analyze_log', "relation_hint IS NULL OR relation_hint IN ('refund', 'reversal', 'transfer')"
    )
