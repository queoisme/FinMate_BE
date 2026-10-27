"""kb method prompts

Thêm ``kb_method_prompts``: chỉ dẫn prompt của bước chọn cách quản lý tài chính (``POST /api/v1/finance-methods/recommend``), theo mẫu ``kb_ai_instructions`` (0008):
sửa ở DB không cần build lại, đúng một dòng ``is_active``. Khác ở ``prompt_version`` (duy nhất): backend ghi nó vào ``method_recommendations.prompt_version``,
nên đổi chỉ dẫn = thêm dòng mới rồi chuyển ``is_active``, dòng cũ giữ lại để truy ngược. Seed một dòng active ``method-select-v1`` từ ``METHOD_PROMPT_TEXT``.

Bước lùi xóa cả bảng (kể cả các phiên bản đã thêm tay): route chỉ dựa vào bảng này, nên lùi là tắt luôn bước AI (backend dùng rule).

Revision ID: 0030
Revises: 0029
"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa

from app.services.method_prompt_seed import METHOD_PROMPT_TEXT, METHOD_PROMPT_VERSION


# revision identifiers, used by Alembic.
revision: str = '0030'
down_revision: Union[str, Sequence[str], None] = '0029'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    table = op.create_table('kb_method_prompts',
    sa.Column('id', sa.UUID(), server_default=sa.text('gen_random_uuid()'), nullable=False),
    sa.Column('prompt_version', sa.String(length=50), nullable=False),
    sa.Column('instruction_text', sa.Text(), nullable=False),
    sa.Column('is_active', sa.Boolean(), server_default=sa.text('true'), nullable=False),
    sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.Column('updated_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.CheckConstraint("prompt_version ~ '^[A-Za-z0-9._-]+$'", name='chk_kb_method_prompts_version'),
    sa.CheckConstraint('char_length(instruction_text) > 0', name='chk_kb_method_prompts_text'),
    sa.PrimaryKeyConstraint('id'),
    sa.UniqueConstraint('prompt_version', name='uq_kb_method_prompts_version'),
    )
    op.create_index('uq_kb_method_prompts_one_active', 'kb_method_prompts', ['is_active'], unique=True, postgresql_where=sa.text('is_active'))
    op.bulk_insert(table, [{'prompt_version': METHOD_PROMPT_VERSION, 'instruction_text': METHOD_PROMPT_TEXT, 'is_active': True}])


def downgrade() -> None:
    op.drop_index('uq_kb_method_prompts_one_active', table_name='kb_method_prompts', postgresql_where=sa.text('is_active'))
    op.drop_table('kb_method_prompts')
