"""receipt source and column cleanup

Ba việc dọn còn lại của ERD v5_1:
  - ``ml_raw_samples.source``: ``bill_scan`` thành ``receipt``. Mẫu thô của hóa đơn (ocr-service ghi, đã gắn nhãn) và văn bản OCR của ``POST /ocr`` là cùng một thứ (chữ của một hóa đơn);
    phần đã có nhãn chính là bộ vàng của hóa đơn. Cột ``package_name`` của hàng ocr-service vẫn là ``bill_scan``: bước lùi dựa vào đó để biết hàng nào phải trả về ``bill_scan``.
  - ``ml_training_samples``: bỏ ``total_amount`` (không nơi nào đọc) và ``label_kind`` (suy được: nhãn cuối khác dự đoán thì là sửa). Bước lùi dựng lại ``label_kind`` từ hai cột đó;
    ``total_amount`` thì mất.
  - ``kb_provider_patterns``: bỏ ``provider_key`` và ``package_name`` (lặp lại của ``kb_provider_formats``, chỗ duy nhất giữ chúng) cùng chỉ mục tra cứu theo ``package_name``.
    Bước lùi chép lại từ định dạng của từng pattern.

Revision ID: 0028
Revises: 0027
"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


# revision identifiers, used by Alembic.
revision: str = '0028'
down_revision: Union[str, Sequence[str], None] = '0027'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None

SOURCES_NEW = "'notification', 'manual_entry', 'receipt', 'seed'"
SOURCES_OLD = "'notification', 'manual_entry', 'receipt', 'seed', 'bill_scan'"


def _check(table: str, name: str, expr: str) -> None:
    op.execute(f"ALTER TABLE {table} ADD CONSTRAINT {name} CHECK ({expr}) NOT VALID")
    op.execute(f"ALTER TABLE {table} VALIDATE CONSTRAINT {name}")


def upgrade() -> None:
    op.execute("ALTER TABLE ml_raw_samples DROP CONSTRAINT chk_raw_samples_source")
    op.execute("UPDATE ml_raw_samples SET source = 'receipt' WHERE source = 'bill_scan'")
    _check('ml_raw_samples', 'chk_raw_samples_source', f'source IN ({SOURCES_NEW})')

    op.drop_constraint('chk_bill_training_samples_label_kind', 'ml_training_samples', type_='check')
    op.drop_column('ml_training_samples', 'label_kind')
    op.drop_column('ml_training_samples', 'total_amount')

    op.drop_index('idx_provider_patterns_lookup', table_name='kb_provider_patterns')
    op.drop_column('kb_provider_patterns', 'package_name')
    op.drop_column('kb_provider_patterns', 'provider_key')


def downgrade() -> None:
    op.add_column('kb_provider_patterns', sa.Column('provider_key', sa.String(length=120), nullable=True))
    op.add_column('kb_provider_patterns', sa.Column('package_name', sa.String(length=120), nullable=True))
    op.execute(
        "UPDATE kb_provider_patterns p SET provider_key = f.provider_key, package_name = f.package_name "
        "FROM kb_provider_formats f WHERE f.id = p.format_id"
    )
    op.alter_column('kb_provider_patterns', 'provider_key', nullable=False)
    op.alter_column('kb_provider_patterns', 'package_name', nullable=False)
    op.create_index('idx_provider_patterns_lookup', 'kb_provider_patterns', ['package_name', 'is_active', 'priority'], unique=False)

    op.add_column('ml_training_samples', sa.Column('total_amount', sa.BigInteger(), nullable=True))
    op.add_column('ml_training_samples', sa.Column('label_kind', sa.String(length=12), nullable=True))
    op.execute(
        "UPDATE ml_training_samples SET label_kind = CASE WHEN predicted_slug = final_slug THEN 'confirmed' ELSE 'corrected' END "
        "WHERE final_slug IS NOT NULL"
    )
    _check('ml_training_samples', 'chk_bill_training_samples_label_kind', "label_kind IS NULL OR label_kind IN ('confirmed', 'corrected')")

    op.execute("ALTER TABLE ml_raw_samples DROP CONSTRAINT chk_raw_samples_source")
    op.execute("UPDATE ml_raw_samples SET source = 'bill_scan' WHERE source = 'receipt' AND package_name = 'bill_scan'")
    _check('ml_raw_samples', 'chk_raw_samples_source', f'source IN ({SOURCES_OLD})')
