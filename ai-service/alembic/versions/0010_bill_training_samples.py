"""bill training samples

Thêm ``bill_training_samples``: chữ ĐÃ lọc của một lần quét + nhãn cuối do người dùng chọn, chỉ khi họ đồng ý đóng góp dữ liệu. Không ảnh, không chữ gốc.
CHỈ THÊM một bảng.

Revision ID: 0010
Revises: 0009
"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa
from sqlalchemy.dialects import postgresql


# revision identifiers, used by Alembic.
revision: str = '0010'
down_revision: Union[str, Sequence[str], None] = '0009'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.create_table('bill_training_samples',
    sa.Column('scan_id', postgresql.UUID(as_uuid=True), nullable=False),
    sa.Column('user_id_hash', sa.String(length=64), nullable=False),
    sa.Column('merchant_key', sa.Text(), nullable=True),
    sa.Column('lines', sa.Text(), nullable=False),
    sa.Column('total_amount', sa.BigInteger(), nullable=True),
    sa.Column('predicted_slug', sa.String(length=32), nullable=True),
    sa.Column('predicted_source', sa.String(length=16), nullable=True),
    sa.Column('final_slug', sa.String(length=32), nullable=True),
    sa.Column('label_kind', sa.String(length=12), nullable=True),
    sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.Column('labeled_at', sa.DateTime(timezone=True), nullable=True),
    sa.CheckConstraint("final_slug IS NULL OR final_slug IN ('food', 'transport', 'shopping', 'education', 'housing', 'bills', 'entertainment', 'health', 'family', 'other')", name='chk_bill_training_samples_final_slug'),
    sa.CheckConstraint("label_kind IS NULL OR label_kind IN ('confirmed', 'corrected')", name='chk_bill_training_samples_label_kind'),
    sa.PrimaryKeyConstraint('scan_id')
    )
    op.create_index('ix_bill_training_samples_user', 'bill_training_samples', ['user_id_hash'], unique=False)
    op.create_index('ix_bill_training_samples_pending', 'bill_training_samples', ['created_at'], unique=False, postgresql_where=sa.text('final_slug IS NULL'))


def downgrade() -> None:
    op.drop_index('ix_bill_training_samples_pending', table_name='bill_training_samples', postgresql_where=sa.text('final_slug IS NULL'))
    op.drop_index('ix_bill_training_samples_user', table_name='bill_training_samples')
    op.drop_table('bill_training_samples')
