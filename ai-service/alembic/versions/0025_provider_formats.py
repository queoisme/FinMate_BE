"""provider formats

Giai đoạn 5 của ERD v5_1: định dạng thông báo theo nhà cung cấp, có phiên bản. Ba bảng mới ``kb_provider_formats``, ``kb_provider_format_fields``, ``kb_provider_format_samples`` và cột
``kb_provider_patterns.format_id`` (NOT NULL, khóa ngoại RESTRICT). Mỗi ``package_name`` đang có pattern được chép thành MỘT định dạng version 1 (``active`` nếu còn pattern đang bật,
ngược lại ``archived``); khai báo trường dựng từ named group mà các regex thực sự dùng; mẫu kiểm tra dựng từ ``sample_text`` của từng pattern (chạy chính bộ khớp để lấy giá trị mong đợi,
pattern không khớp mẫu của chính nó thì bị bỏ qua). Tên pattern chỉ còn duy nhất TRONG một định dạng.

Bước lùi bỏ ba bảng và cột, rồi dựng lại ràng buộc ``(package_name, pattern_name)``: nếu đã có hai phiên bản cùng tên pattern thì bước lùi dừng và nói rõ (xóa bản dư trước).

Revision ID: 0025
Revises: 0024
"""
import hashlib
import json
import uuid
from typing import Sequence, Union

from alembic import op
import regex
import sqlalchemy as sa
from sqlalchemy.dialects import postgresql

from app.utils.provider_patterns import PatternMatcher, PatternSpec


# revision identifiers, used by Alembic.
revision: str = '0025'
down_revision: Union[str, Sequence[str], None] = '0024'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None

SYSTEM_ACTOR = hashlib.sha256(b"system").hexdigest()
# Bản sao cố định (migration không đổi theo code): trường hợp đồng -> (named group mặc định, data_type, normalizer).
CONTRACT = {
    "amount_cents": ("amount", "amount", "vnd_amount"),
    "transaction_type": ("sign", "text", "clean_text"),
    "merchant_name": ("merchant", "text", "clean_text"),
    "description": ("description", "text", "clean_text"),
    "transacted_at": ("occurred_at", "datetime", "clean_text"),
    "balance_after_cents": ("balance", "amount", "vnd_amount"),
}
_FLAGS = regex.IGNORECASE | regex.UNICODE | regex.DOTALL


def _expected(match) -> dict:
    expected = {"amount_cents": match.amount_cents}
    for key, value in (
        ("transaction_type", match.transaction_type.value if match.transaction_type else None),
        ("merchant_name", match.merchant_name),
        ("description", match.description),
        ("balance_after_cents", match.balance_after_cents),
        ("transacted_at", match.occurred_at_raw),
    ):
        if value is not None:
            expected[key] = value
    return expected


def _backfill(conn) -> None:
    rows = conn.execute(sa.text(
        "SELECT id, provider_key, package_name, pattern_name, regex, transaction_type, priority, sample_text, is_active "
        "FROM kb_provider_patterns ORDER BY package_name, priority, pattern_name"
    )).mappings().all()
    by_package: dict[str, list] = {}
    for row in rows:
        by_package.setdefault(row["package_name"], []).append(row)

    for package, patterns in by_package.items():
        format_id = uuid.uuid4()
        status = "active" if any(p["is_active"] for p in patterns) else "archived"
        conn.execute(sa.text(
            "INSERT INTO kb_provider_formats (id, provider_key, package_name, version, status, priority, notes, created_by) "
            "VALUES (:id, :key, :package, 1, :status, 100, 'Chép từ kb_provider_patterns lúc nâng cấp 0025', :actor)"
        ), {"id": format_id, "key": min(p["provider_key"] for p in patterns), "package": package, "status": status, "actor": SYSTEM_ACTOR})
        conn.execute(sa.text("UPDATE kb_provider_patterns SET format_id = :f WHERE package_name = :p"), {"f": format_id, "p": package})

        groups: set[str] = set()
        for pattern in patterns:
            groups |= set(regex.compile(pattern["regex"], _FLAGS).groupindex)
        for contract_key, (group, data_type, normalizer) in CONTRACT.items():
            if group in groups:
                conn.execute(sa.text(
                    "INSERT INTO kb_provider_format_fields (format_id, field_key, maps_to, data_type, is_required, source_kind, source_ref, normalizer) "
                    "VALUES (:f, :key, :maps_to, :data_type, :required, 'regex_group', :ref, :normalizer)"
                ), {"f": format_id, "key": group, "ref": group, "maps_to": contract_key, "data_type": data_type, "required": contract_key == "amount_cents", "normalizer": normalizer})

        for pattern in patterns:
            if not pattern["sample_text"]:
                continue
            spec = PatternSpec(
                provider_key=pattern["provider_key"], package_name=package, pattern_name=pattern["pattern_name"], regex=pattern["regex"],
                transaction_type=pattern["transaction_type"], priority=pattern["priority"],
            )
            matched = PatternMatcher([spec]).match(package, pattern["sample_text"])
            if matched is None:
                continue
            conn.execute(sa.text(
                "INSERT INTO kb_provider_format_samples (format_id, sample_text, expected_fields) VALUES (:f, :text, CAST(:expected AS jsonb))"
            ), {"f": format_id, "text": pattern["sample_text"], "expected": json.dumps(_expected(matched), ensure_ascii=False)})


def upgrade() -> None:
    op.create_table('kb_provider_formats',
    sa.Column('id', sa.UUID(), server_default=sa.text('gen_random_uuid()'), nullable=False),
    sa.Column('provider_key', sa.String(length=120), nullable=False),
    sa.Column('package_name', sa.String(length=120), nullable=False),
    sa.Column('version', sa.Integer(), nullable=False),
    sa.Column('status', sa.String(length=10), nullable=False),
    sa.Column('priority', sa.Integer(), server_default=sa.text('100'), nullable=False),
    sa.Column('notes', sa.Text(), nullable=True),
    sa.Column('created_by', sa.String(length=64), nullable=False),
    sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.Column('updated_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.CheckConstraint("status IN ('draft', 'active', 'archived')", name='chk_kb_provider_formats_status'),
    sa.CheckConstraint('version >= 1', name='chk_kb_provider_formats_version'),
    sa.CheckConstraint('priority >= 0', name='chk_kb_provider_formats_priority'),
    sa.CheckConstraint("created_by ~ '^[0-9a-f]{64}$'", name='chk_kb_provider_formats_created_by'),
    sa.PrimaryKeyConstraint('id'),
    sa.UniqueConstraint('package_name', 'version', name='uq_kb_provider_formats_package_version')
    )
    op.create_index('idx_kb_provider_formats_match', 'kb_provider_formats', ['package_name', 'status', 'priority'], unique=False)

    op.create_table('kb_provider_format_fields',
    sa.Column('id', sa.UUID(), server_default=sa.text('gen_random_uuid()'), nullable=False),
    sa.Column('format_id', sa.UUID(), nullable=False),
    sa.Column('field_key', sa.String(length=64), nullable=False),
    sa.Column('maps_to', sa.String(length=32), nullable=True),
    sa.Column('data_type', sa.String(length=16), nullable=False),
    sa.Column('is_required', sa.Boolean(), server_default=sa.text('false'), nullable=False),
    sa.Column('source_kind', sa.String(length=16), nullable=False),
    sa.Column('source_ref', sa.Text(), nullable=False),
    sa.Column('normalizer', sa.String(length=32), nullable=True),
    sa.Column('normalizer_arg', sa.Text(), nullable=True),
    sa.Column('is_sensitive', sa.Boolean(), server_default=sa.text('false'), nullable=False),
    sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.CheckConstraint("maps_to IS NULL OR maps_to IN ('amount_cents', 'transaction_type', 'merchant_name', 'description', 'transacted_at', 'balance_after_cents')", name='chk_kb_provider_format_fields_maps_to'),
    sa.CheckConstraint("data_type IN ('text', 'amount', 'datetime')", name='chk_kb_provider_format_fields_data_type'),
    sa.CheckConstraint("source_kind IN ('regex_group', 'literal')", name='chk_kb_provider_format_fields_source_kind'),
    sa.CheckConstraint("normalizer IS NULL OR normalizer IN ('vnd_amount', 'clean_text')", name='chk_kb_provider_format_fields_normalizer'),
    sa.ForeignKeyConstraint(['format_id'], ['kb_provider_formats.id'], ondelete='CASCADE'),
    sa.PrimaryKeyConstraint('id'),
    sa.UniqueConstraint('format_id', 'field_key', name='uq_kb_provider_format_fields_key')
    )
    op.create_index('uq_kb_provider_format_fields_maps_to', 'kb_provider_format_fields', ['format_id', 'maps_to'], unique=True, postgresql_where=sa.text('maps_to IS NOT NULL'))

    op.create_table('kb_provider_format_samples',
    sa.Column('id', sa.UUID(), server_default=sa.text('gen_random_uuid()'), nullable=False),
    sa.Column('format_id', sa.UUID(), nullable=False),
    sa.Column('sample_text', sa.Text(), nullable=False),
    sa.Column('expected_fields', postgresql.JSONB(astext_type=sa.Text()), nullable=False),
    sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.CheckConstraint("jsonb_typeof(expected_fields) = 'object'", name='chk_kb_provider_format_samples_expected'),
    sa.CheckConstraint('char_length(sample_text) <= 2000', name='chk_kb_provider_format_samples_text_len'),
    sa.ForeignKeyConstraint(['format_id'], ['kb_provider_formats.id'], ondelete='CASCADE'),
    sa.PrimaryKeyConstraint('id')
    )
    op.create_index('idx_kb_provider_format_samples_format', 'kb_provider_format_samples', ['format_id'], unique=False)

    op.add_column('kb_provider_patterns', sa.Column('format_id', sa.UUID(), nullable=True))
    _backfill(op.get_bind())
    op.alter_column('kb_provider_patterns', 'format_id', nullable=False)
    op.create_foreign_key('kb_provider_patterns_format_id_fkey', 'kb_provider_patterns', 'kb_provider_formats', ['format_id'], ['id'], ondelete='RESTRICT')
    op.drop_constraint('uq_provider_patterns_name', 'kb_provider_patterns', type_='unique')
    op.create_unique_constraint('uq_kb_provider_patterns_format_name', 'kb_provider_patterns', ['format_id', 'pattern_name'])
    op.create_index('idx_kb_provider_patterns_format', 'kb_provider_patterns', ['format_id', 'is_active', 'priority'], unique=False)


def downgrade() -> None:
    op.drop_index('idx_kb_provider_patterns_format', table_name='kb_provider_patterns')
    op.drop_constraint('uq_kb_provider_patterns_format_name', 'kb_provider_patterns', type_='unique')
    op.create_unique_constraint('uq_provider_patterns_name', 'kb_provider_patterns', ['package_name', 'pattern_name'])
    op.drop_constraint('kb_provider_patterns_format_id_fkey', 'kb_provider_patterns', type_='foreignkey')
    op.drop_column('kb_provider_patterns', 'format_id')
    op.drop_index('idx_kb_provider_format_samples_format', table_name='kb_provider_format_samples')
    op.drop_table('kb_provider_format_samples')
    op.drop_index('uq_kb_provider_format_fields_maps_to', table_name='kb_provider_format_fields', postgresql_where=sa.text('maps_to IS NOT NULL'))
    op.drop_table('kb_provider_format_fields')
    op.drop_index('idx_kb_provider_formats_match', table_name='kb_provider_formats')
    op.drop_table('kb_provider_formats')
