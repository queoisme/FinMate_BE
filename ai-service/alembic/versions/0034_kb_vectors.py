"""kb vectors

Kho tri thức của AI Financial Agent: bật extension ``vector`` (image postgres-ai đã đổi sang pgvector ở Task 1), thêm ``agent_kb_documents`` và ``agent_kb_chunks``
(cột ``tsv`` sinh từ ``text`` với cấu hình 'simple' + chỉ mục GIN; cột ``embedding vector(768)``). Bảng mới, không đụng bảng nào khác.

Bước lùi xóa hai bảng nhưng KHÔNG gỡ extension (các bảng khác sau này có thể dùng).

Revision ID: 0034
Revises: 0033
"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa
from sqlalchemy.dialects import postgresql

from app.db.vector import Vector

revision: str = '0034'
down_revision: Union[str, Sequence[str], None] = '0033'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.execute("CREATE EXTENSION IF NOT EXISTS vector")
    op.create_table('agent_kb_documents',
    sa.Column('id', postgresql.UUID(as_uuid=True), server_default=sa.text('gen_random_uuid()'), nullable=False),
    sa.Column('title', sa.String(length=200), nullable=False),
    sa.Column('source_name', sa.String(length=200), nullable=False),
    sa.Column('source_url', sa.String(length=500), nullable=True),
    sa.Column('as_of', sa.DateTime(timezone=True), nullable=False),
    sa.Column('effective_from', sa.DateTime(timezone=True), nullable=False),
    sa.Column('effective_to', sa.DateTime(timezone=True), nullable=True),
    sa.Column('status', sa.String(length=10), server_default=sa.text("'Draft'"), nullable=False),
    sa.Column('version', sa.Integer(), server_default=sa.text('1'), nullable=False),
    sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.Column('updated_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.CheckConstraint("status IN ('Draft', 'Approved', 'Retired')", name='chk_agent_kb_documents_status'),
    sa.CheckConstraint('effective_to IS NULL OR effective_to > effective_from', name='chk_agent_kb_documents_window'),
    sa.PrimaryKeyConstraint('id')
    )
    op.create_index('ix_agent_kb_documents_status', 'agent_kb_documents', ['status'], unique=False)
    op.create_table('agent_kb_chunks',
    sa.Column('id', postgresql.UUID(as_uuid=True), server_default=sa.text('gen_random_uuid()'), nullable=False),
    sa.Column('document_id', postgresql.UUID(as_uuid=True), nullable=False),
    sa.Column('chunk_index', sa.Integer(), nullable=False),
    sa.Column('text', sa.Text(), nullable=False),
    sa.Column('tsv', postgresql.TSVECTOR(), sa.Computed("to_tsvector('simple', text)", persisted=True), nullable=False),
    sa.Column('embedding', Vector(768), nullable=False),
    sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.ForeignKeyConstraint(['document_id'], ['agent_kb_documents.id'], ondelete='CASCADE'),
    sa.PrimaryKeyConstraint('id'),
    sa.UniqueConstraint('document_id', 'chunk_index', name='uq_agent_kb_chunks_doc_index')
    )
    op.create_index('ix_agent_kb_chunks_tsv', 'agent_kb_chunks', ['tsv'], unique=False, postgresql_using='gin')


def downgrade() -> None:
    op.drop_index('ix_agent_kb_chunks_tsv', table_name='agent_kb_chunks', postgresql_using='gin')
    op.drop_table('agent_kb_chunks')
    op.drop_index('ix_agent_kb_documents_status', table_name='agent_kb_documents')
    op.drop_table('agent_kb_documents')
