"""Migration 0027 (ràng buộc), 0028 (receipt, dọn cột) và 0029 (gộp phiếu chọn cửa hàng) chạy alembic THẬT trên dữ liệu cũ, hạ lại và nâng lại.

Cùng kiểu với ``test_migration_0026``: database riêng, nâng tới 0026, đặt dữ liệu cũ (kể cả mã người dùng ``local`` và nhóm cũ của bill), nâng tới head.
"""

import asyncio
import hashlib
import os
import subprocess
import sys
import uuid
from pathlib import Path

import pytest
from sqlalchemy import text
from sqlalchemy.ext.asyncio import create_async_engine
from sqlalchemy.pool import NullPool

ROOT = Path(__file__).resolve().parents[2]
DB_NAME = f"finmate_ai_mig27_{uuid.uuid4().hex[:8]}"
A, B = "a" * 64, "b" * 64
LOCAL = hashlib.sha256(b"local").hexdigest()

LEGACY = [
    # mẫu thô của ocr-service: nguồn bill_scan, mã người dùng mặc định "local"
    "INSERT INTO ml_raw_samples (id, source, package_name, notification_body, received_at, user_id_hash, content_hash) "
    f"VALUES ('00000000-0000-0000-0000-0000000000a1', 'bill_scan', 'bill_scan', 'ca phe', now(), 'local', '{'1' * 64}')",
    "INSERT INTO ml_raw_samples (id, source, package_name, notification_body, received_at, content_hash) "
    f"VALUES ('00000000-0000-0000-0000-0000000000a2', 'receipt', 'receipt_ocr', 'van ban ocr', now(), '{'2' * 64}')",
    # mẫu huấn luyện còn cột total_amount / label_kind
    "INSERT INTO ml_training_samples (scan_id, user_id_hash, lines, total_amount, predicted_slug, final_slug, label_kind) "
    f"VALUES ('00000000-0000-0000-0000-0000000000b1', '{A}', 'x', 90000, 'food', 'food', 'confirmed'), "
    f"('00000000-0000-0000-0000-0000000000b2', '{A}', 'y', NULL, 'food', 'shopping', 'corrected'), "
    f"('00000000-0000-0000-0000-0000000000b3', '{A}', 'z', NULL, NULL, NULL, NULL)",
    # định dạng + pattern còn provider_key / package_name
    "INSERT INTO kb_provider_formats (id, provider_key, package_name, version, status, created_by) "
    f"VALUES ('00000000-0000-0000-0000-0000000000c1', 'test_bank', 'com.test.bank', 1, 'draft', '{A}')",
    "INSERT INTO kb_provider_patterns (id, format_id, provider_key, package_name, pattern_name, regex) "
    "VALUES ('00000000-0000-0000-0000-0000000000c2', '00000000-0000-0000-0000-0000000000c1', 'test_bank', 'com.test.bank', 'p1', 'x')",
    # nhật ký phân tích: một con trỏ pattern có thật, một con trỏ treo
    "INSERT INTO ops_analyze_log (id, backend_request_id, user_id_hash, package_name, received_at, pipeline_result, pattern_id) VALUES "
    f"('00000000-0000-0000-0000-0000000000d1', gen_random_uuid(), '{A}', 'p', now(), 'financial', '00000000-0000-0000-0000-0000000000c2'), "
    f"('00000000-0000-0000-0000-0000000000d2', gen_random_uuid(), '{A}', 'p', now(), 'financial', gen_random_uuid())",
    # phiếu chọn cửa hàng theo 9 nhóm cũ; (A, highlands coffee) đã có bản mới ở user_merchant_memory và phải thắng
    "INSERT INTO bill_user_merchant_category (user_id_hash, merchant_key, display_name, category_slug) VALUES "
    f"('{A}', 'highlands coffee', 'Highlands', 'dining'), ('{A}', 'kisnac', 'Kisnac', 'groceries'), "
    f"('local', 'long chau', 'Long Châu', 'healthcare'), ('{B}', 'tien ich', NULL, 'utilities')",
    f"INSERT INTO user_merchant_memory (user_id_hash, merchant_key, category_slug) VALUES ('{A}', 'highlands coffee', 'health')",
    # hàng đợi bill lạ: mã người dùng "local"
    "INSERT INTO bill_unknown_scans (user_id_hash, merchant_read, merchant_key, lines, content_hash) "
    f"VALUES ('local', 'Quan La', 'quan la', 'x', '{'3' * 64}')",
]


def _alembic(url: str, *args: str) -> subprocess.CompletedProcess:
    return subprocess.run([sys.executable, "-m", "alembic", *args], cwd=ROOT, env={**os.environ, "DATABASE_URL": url}, capture_output=True, text=True)


async def _run(url: str, *statements: str):
    engine = create_async_engine(url, isolation_level="AUTOCOMMIT", poolclass=NullPool)
    try:
        async with engine.connect() as connection:
            result = None
            for statement in statements:
                result = await connection.execute(text(statement))
            return result.all() if result is not None and result.returns_rows else None
    finally:
        await engine.dispose()


def rows(url: str, statement: str):
    return [tuple(r) for r in asyncio.run(_run(url, statement))]


@pytest.fixture(scope="module")
def database():
    from app.core.config import settings

    base, _, _ = settings.database_url.rpartition("/")
    admin_url, url = f"{base}/postgres", f"{base}/{DB_NAME}"
    try:
        asyncio.run(_run(admin_url, f'CREATE DATABASE "{DB_NAME}"'))
    except Exception as error:  # noqa: BLE001
        pytest.skip(f"Không kết nối được Postgres cho test tích hợp: {error}")
    try:
        assert _alembic(url, "upgrade", "0026").returncode == 0
        asyncio.run(_run(url, *LEGACY))
        yield url
    finally:
        asyncio.run(_run(admin_url, f'DROP DATABASE IF EXISTS "{DB_NAME}" WITH (FORCE)'))


def test_upgrade_rewrites_old_data_and_adds_the_constraints(database):
    result = _alembic(database, "upgrade", "head")
    assert result.returncode == 0, result.stderr

    # 0027: mã "local" được băm lại; con trỏ treo về NULL, con trỏ thật còn
    assert rows(database, "SELECT user_id_hash FROM ml_raw_samples WHERE content_hash = repeat('1', 64)") == [(LOCAL,)]
    pointers = dict(rows(database, "SELECT id::text, pattern_id IS NOT NULL FROM ops_analyze_log"))
    assert pointers == {"00000000-0000-0000-0000-0000000000d1": True, "00000000-0000-0000-0000-0000000000d2": False}
    assert rows(database, "SELECT count(*) FROM pg_trigger WHERE tgname = 'trg_kb_audit_log_append_only'") == [(1,)]

    # 0028: bill_scan -> receipt (package_name giữ nguyên để phân biệt), cột bị bỏ
    assert dict(rows(database, "SELECT content_hash, source FROM ml_raw_samples")) == {"1" * 64: "receipt", "2" * 64: "receipt"}
    columns = {r[0] for r in rows(database, "SELECT column_name FROM information_schema.columns WHERE table_name IN ('ml_training_samples', 'kb_provider_patterns')")}
    assert not columns & {"total_amount", "label_kind", "provider_key", "package_name"}
    assert rows(database, "SELECT count(*) FROM ml_training_samples") == [(3,)] and rows(database, "SELECT count(*) FROM kb_provider_patterns") == [(1,)]

    # 0029: phiếu chọn gộp vào user_merchant_memory (slug backend); bản có sẵn thắng; bảng cũ và view biến mất
    merged = rows(database, "SELECT user_id_hash, merchant_key, category_slug, display_name FROM user_merchant_memory ORDER BY merchant_key, user_id_hash")
    assert merged == [
        (A, "highlands coffee", "health", None),  # bản người dùng chọn lúc lưu giao dịch thắng nhóm cũ dining
        (A, "kisnac", "food", "Kisnac"),  # groceries -> food
        (LOCAL, "long chau", "health", "Long Châu"),  # healthcare -> health, mã local được băm
        (B, "tien ich", "bills", None),  # utilities -> bills
    ]
    assert rows(database, "SELECT to_regclass('bill_user_merchant_category'), to_regclass('bill_merchant_votes')") == [(None, None)]
    assert rows(database, "SELECT user_id_hash FROM bill_unknown_scans") == [(LOCAL,)]  # hàng đợi bill lạ còn nguyên, chỉ băm lại mã

    assert _alembic(database, "check").returncode == 0


def test_the_new_constraints_are_live(database):
    with pytest.raises(Exception, match="chk_kb_provider_format_fields_sensitive_mapped"):
        asyncio.run(_run(database, "INSERT INTO kb_provider_format_fields (format_id, field_key, maps_to, data_type, source_kind, source_ref, is_sensitive) "
                                   "VALUES ('00000000-0000-0000-0000-0000000000c1', 'acct', NULL, 'text', 'regex_group', 'a', true)"))
    with pytest.raises(Exception, match="chk_bill_unknown_scans_user_hash"):
        asyncio.run(_run(database, f"INSERT INTO bill_unknown_scans (user_id_hash, lines, content_hash) VALUES ('local', 'x', '{'4' * 64}')"))
    asyncio.run(_run(database, f"INSERT INTO kb_audit_log (resource, resource_key, action, actor_hash) VALUES ('brand', 'k', 'create', '{A}')"))
    with pytest.raises(Exception, match="chỉ được thêm dòng"):
        asyncio.run(_run(database, "UPDATE kb_audit_log SET reason = 'sửa'"))


def test_downgrade_restores_the_old_shape_and_upgrade_works_again(database):
    result = _alembic(database, "downgrade", "0026")
    assert result.returncode == 0, result.stderr

    assert rows(database, "SELECT content_hash, source FROM ml_raw_samples ORDER BY content_hash") == [("1" * 64, "bill_scan"), ("2" * 64, "receipt")]  # chỉ hàng package bill_scan quay lại
    kinds = dict(rows(database, "SELECT scan_id::text, label_kind FROM ml_training_samples"))
    assert kinds == {"00000000-0000-0000-0000-0000000000b1": "confirmed", "00000000-0000-0000-0000-0000000000b2": "corrected", "00000000-0000-0000-0000-0000000000b3": None}
    assert rows(database, "SELECT provider_key, package_name FROM kb_provider_patterns") == [("test_bank", "com.test.bank")]  # chép lại từ định dạng
    assert rows(database, "SELECT count(*) FROM bill_user_merchant_category") == [(0,)]  # dựng lại rỗng: không đổi ngược được slug backend về nhóm cũ
    assert rows(database, "SELECT count(*) FROM user_merchant_memory") == [(4,)]  # dữ liệu đã gộp ở lại
    assert rows(database, "SELECT count(*) FROM pg_trigger WHERE tgname = 'trg_kb_audit_log_append_only'") == [(0,)]

    again = _alembic(database, "upgrade", "head")
    assert again.returncode == 0, again.stderr
    assert _alembic(database, "check").returncode == 0
