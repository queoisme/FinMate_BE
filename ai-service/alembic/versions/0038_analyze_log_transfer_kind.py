"""analyze log transfer kind

Phase 83: gợi ý "khoản này là tiền chuyển giữa các ví của chính người dùng" (``relation_hint = 'transfer'``) cùng loại chuyển
(``transfer_kind``). Dòng cũ giữ NULL; CHECK chỉ mở rộng nên không có dòng nào vi phạm.

Revision ID: 0038
Revises: 0037
"""
from collections.abc import Sequence

import sqlalchemy as sa

from alembic import op

revision: str = '0038'
down_revision: str | Sequence[str] | None = '0037'
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None

KINDS = "('atm_withdrawal', 'atm_deposit', 'wallet_topup', 'wallet_withdrawal', 'own_account')"


def upgrade() -> None:
    op.drop_constraint('chk_ops_analyze_log_relation_hint', 'ops_analyze_log', type_='check')
    op.create_check_constraint(
        'chk_ops_analyze_log_relation_hint', 'ops_analyze_log', "relation_hint IS NULL OR relation_hint IN ('refund', 'reversal', 'transfer')"
    )
    op.add_column('ops_analyze_log', sa.Column('transfer_kind', sa.String(length=24), nullable=True))
    op.create_check_constraint(
        'chk_ops_analyze_log_transfer_kind',
        'ops_analyze_log',
        f"transfer_kind IS NULL OR (relation_hint = 'transfer' AND transfer_kind IN {KINDS})",
    )


def downgrade() -> None:
    op.drop_constraint('chk_ops_analyze_log_transfer_kind', 'ops_analyze_log', type_='check')
    op.drop_column('ops_analyze_log', 'transfer_kind')
    op.execute("UPDATE ops_analyze_log SET relation_hint = NULL WHERE relation_hint = 'transfer'")
    op.drop_constraint('chk_ops_analyze_log_relation_hint', 'ops_analyze_log', type_='check')
    op.create_check_constraint(
        'chk_ops_analyze_log_relation_hint', 'ops_analyze_log', "relation_hint IS NULL OR relation_hint IN ('refund', 'reversal')"
    )
