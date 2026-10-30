"""secret store

Thêm ``secret_store``: key của bên thứ ba do admin nhập qua trang quản trị, lưu đã mã hóa AES-256-GCM (xem ``app/db/models/secret.py``). Bảng mới, không đụng bảng nào khác.

Mở rộng ràng buộc ``chk_kb_audit_log_resource`` thêm ``secret`` (nhật ký đổi key chỉ ghi tên, 4 ký tự cuối và lý do), cùng cách 0026 đã làm cho ``category``.

Bước lùi xóa bảng và trả ràng buộc cũ ở dạng NOT VALID: ``kb_audit_log`` chỉ được thêm dòng (có trigger chặn DELETE), nên các dòng ``secret`` đã ghi vẫn nằm đó và ràng buộc cũ không kiểm lại chúng. Các key đã nhập qua trang quản trị mất; dịch vụ rơi về biến môi trường (``GEMINI_API_KEY``, ``OPENROUTER_API_KEY``) như trước khi có bảng này.

Revision ID: 0031
Revises: 0030
"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


# revision identifiers, used by Alembic.
revision: str = '0031'
down_revision: Union[str, Sequence[str], None] = '0030'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


AUDIT_OLD = "resource IN ('brand', 'keyword', 'item_cache', 'instruction', 'provider_format', 'category')"
AUDIT_NEW = "resource IN ('brand', 'keyword', 'item_cache', 'instruction', 'provider_format', 'category', 'secret')"


def _recheck(name: str, expr: str, validate: bool = True) -> None:
    op.execute(f"ALTER TABLE kb_audit_log ADD CONSTRAINT {name} CHECK ({expr}) NOT VALID")
    if validate:
        op.execute(f"ALTER TABLE kb_audit_log VALIDATE CONSTRAINT {name}")


def upgrade() -> None:
    op.create_table('secret_store',
    sa.Column('name', sa.String(length=48), nullable=False),
    sa.Column('ciphertext', sa.LargeBinary(), nullable=False),
    sa.Column('nonce', sa.LargeBinary(), nullable=False),
    sa.Column('key_version', sa.SmallInteger(), server_default=sa.text('1'), nullable=False),
    sa.Column('last4', sa.String(length=4), nullable=False),
    sa.Column('updated_by', sa.String(length=64), nullable=False),
    sa.Column('created_at', sa.TIMESTAMP(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.Column('updated_at', sa.TIMESTAMP(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.CheckConstraint("updated_by ~ '^[0-9a-f]{64}$'", name='chk_secret_store_updated_by'),
    sa.CheckConstraint('octet_length(nonce) = 12', name='chk_secret_store_nonce'),
    sa.PrimaryKeyConstraint('name', name=op.f('pk_secret_store')),
    )
    op.execute("ALTER TABLE kb_audit_log DROP CONSTRAINT chk_kb_audit_log_resource")
    _recheck('chk_kb_audit_log_resource', AUDIT_NEW)


def downgrade() -> None:
    op.execute("ALTER TABLE kb_audit_log DROP CONSTRAINT chk_kb_audit_log_resource")
    _recheck('chk_kb_audit_log_resource', AUDIT_OLD, validate=False)  # nhật ký chỉ thêm, không xóa được: giữ các dòng 'secret' đã có
    op.drop_table('secret_store')
