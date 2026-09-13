"""receipt sample source

Nới CHECK của raw_samples.source để nhận thêm 'receipt'.

Text đọc được từ hóa đơn là dữ liệu huấn luyện thật cho một model OCR/bóc trường sau này,
nên nó vào raw_samples như mọi nguồn khác. Nhét nó vào 'manual_entry' cho đỡ phải migration
sẽ làm nhãn nguồn nói dối, và bộ dữ liệu là thứ cuối cùng nên nói dối.

(ẢNH thì không lưu ở đâu cả — xem app/api/v1/ocr.py.)

Revision ID: 0005
Revises: 0004
"""

from alembic import op

revision: str = "0005"
down_revision: str | None = "0004"
branch_labels: None = None
depends_on: None = None

_CONSTRAINT = "chk_raw_samples_source"


def upgrade() -> None:
    op.drop_constraint(_CONSTRAINT, "raw_samples", type_="check")
    op.create_check_constraint(
        _CONSTRAINT,
        "raw_samples",
        "source IN ('notification', 'manual_entry', 'receipt', 'seed')",
    )


def downgrade() -> None:
    # Dòng 'receipt' đã có sẽ chặn việc thu hẹp lại — xóa chúng trước, chúng chỉ là mẫu
    # huấn luyện thu thập được chứ không phải dữ liệu nghiệp vụ của người dùng.
    op.execute("DELETE FROM raw_samples WHERE source = 'receipt'")
    op.drop_constraint(_CONSTRAINT, "raw_samples", type_="check")
    op.create_check_constraint(
        _CONSTRAINT,
        "raw_samples",
        "source IN ('notification', 'manual_entry', 'seed')",
    )
