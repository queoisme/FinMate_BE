"""kb candidate topics

``agent_kb_topics``: danh sách chủ đề (mã, nhãn tiếng Việt, từ khóa đã bỏ dấu viết thường) để gán nhãn cho câu hỏi tri thức chưa trả lời được; admin sửa được, không nằm trong code.
``agent_kb_candidates.topic_code``: chủ đề của câu hỏi, NULL = chưa khớp chủ đề nào. Chỉ lưu mã chủ đề, vẫn KHÔNG lưu nội dung câu hỏi.
Hai thay đổi cộng thêm (bảng mới + cột nullable). Bước lùi xóa cột rồi xóa bảng.

Revision ID: 0040
Revises: 0039
"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa
from sqlalchemy.dialects import postgresql

revision: str = '0040'
down_revision: Union[str, Sequence[str], None] = '0039'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None

# Bộ chủ đề khởi đầu. Từ khóa đã bỏ dấu, viết thường; khớp nguyên cụm từ trên câu hỏi đã bỏ dấu. Chủ đề đứng trước thắng khi hai chủ đề khớp bằng nhau.
_STARTER = [
    ('savings_interest', 'Lãi suất & tiết kiệm', ['lai suat', 'tiet kiem', 'gui ngan hang', 'so tiet kiem', 'gui tiet kiem']),
    ('tax', 'Thuế', ['thue', 'thu nhap ca nhan', 'quyet toan', 'giam tru gia canh', 'vat']),
    ('insurance', 'Bảo hiểm', ['bao hiem', 'bhxh', 'bhyt', 'bhtn']),
    ('loan_debt', 'Vay & nợ', ['vay', 'cho vay', 'khoan vay', 'tra gop', 'the tin dung', 'tra no', 'no xau']),
    ('investing', 'Đầu tư', ['dau tu', 'chung khoan', 'co phieu', 'trai phieu', 'chung chi quy', 'vang', 'bitcoin', 'tien ao']),
    ('income_salary', 'Lương & thu nhập', ['luong', 'thuong', 'thu nhap', 'phu cap', 'lam them', 'freelance']),
    ('retirement', 'Hưu trí', ['huu tri', 'nghi huu', 've huu']),
    ('housing', 'Nhà ở & bất động sản', ['mua nha', 'thue nha', 'bat dong san', 'nha dat', 'the chap']),
    ('budgeting', 'Lập ngân sách & chi tiêu', ['ngan sach', '50/30/20', 'chi tieu', 'quy khan cap', 'no luc']),
    ('app_usage', 'Cách dùng ứng dụng', ['ghi chep', 'ung dung', 'finmate', 'quet hoa don', 'quet qr', 'widget', 'thong bao', 'dang nhap', 'mat khau']),
]


def upgrade() -> None:
    topics = op.create_table('agent_kb_topics',
    sa.Column('code', sa.String(length=40), nullable=False),
    sa.Column('label', sa.String(length=100), nullable=False),
    sa.Column('keywords', postgresql.ARRAY(sa.Text()), server_default=sa.text("'{}'"), nullable=False),
    sa.Column('sort_order', sa.Integer(), server_default=sa.text('0'), nullable=False),
    sa.Column('is_active', sa.Boolean(), server_default=sa.text('true'), nullable=False),
    sa.CheckConstraint("code ~ '^[a-z0-9_]{1,40}$'", name='chk_agent_kb_topics_code'),
    sa.PrimaryKeyConstraint('code'),
    )
    op.bulk_insert(topics, [
        {'code': code, 'label': label, 'keywords': keywords, 'sort_order': i * 10}
        for i, (code, label, keywords) in enumerate(_STARTER)
    ])
    op.add_column('agent_kb_candidates', sa.Column('topic_code', sa.String(length=40), nullable=True))
    op.create_index('ix_agent_kb_candidates_topic', 'agent_kb_candidates', ['topic_code'])


def downgrade() -> None:
    op.drop_index('ix_agent_kb_candidates_topic', table_name='agent_kb_candidates')
    op.drop_column('agent_kb_candidates', 'topic_code')
    op.drop_table('agent_kb_topics')
