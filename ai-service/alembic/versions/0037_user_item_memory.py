"""user item memory

Bảng nhớ danh mục của TỪNG MÓN theo người dùng: ``user_item_memory(user_id_hash, item_key, category_id, source, updated_at)``, khóa chính ``(user_id_hash, item_key)``.
``category_id`` là id UUID danh mục của backend (hệ thống hoặc riêng của người dùng) nên không có khóa ngoại và không dùng ``category_guard``.
``source``: ``corrected`` (người dùng sửa) hoặc ``accepted`` (giữ gợi ý AI).

Bước lùi xóa bảng cùng toàn bộ dữ liệu trong đó (bộ nhớ của người dùng, không khôi phục được từ đây).

Revision ID: 0037
Revises: 0036
"""
from typing import Sequence, Union

from alembic import op

revision: str = '0037'
down_revision: Union[str, Sequence[str], None] = '0036'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.execute("""
        CREATE TABLE user_item_memory (
            user_id_hash varchar(64) NOT NULL,
            item_key text NOT NULL,
            category_id uuid NOT NULL,
            source varchar(16) NOT NULL,
            updated_at timestamptz NOT NULL DEFAULT now(),
            CONSTRAINT pk_user_item_memory PRIMARY KEY (user_id_hash, item_key),
            CONSTRAINT chk_user_item_memory_source CHECK (source IN ('corrected', 'accepted')),
            CONSTRAINT chk_user_item_memory_key_lower CHECK (item_key = lower(item_key)),
            CONSTRAINT chk_user_item_memory_key_len CHECK (char_length(item_key) BETWEEN 3 AND 60),
            CONSTRAINT chk_user_item_memory_user_hash CHECK (user_id_hash ~ '^[0-9a-f]{64}$')
        )
    """)


def downgrade() -> None:
    op.execute("DROP TABLE user_item_memory")
