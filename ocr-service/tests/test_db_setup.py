"""Thiết lập DB (db_setup.py): schema do Alembic của ai-service quản lý, db_setup chỉ nạp dữ liệu."""
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))

import db_setup  # noqa: E402


def test_main_needs_the_ai_service_address_and_key(monkeypatch):
    monkeypatch.delenv("AI_SERVICE_URL", raising=False)
    monkeypatch.delenv("AI_SERVICE_API_KEY", raising=False)
    assert db_setup.main(["--seed"]) == 1


def test_db_setup_no_longer_creates_schema():
    # Schema nằm trong alembic của ai-service (migration 0006); db_setup tạo bảng ở finmate_ai sẽ lệch khỏi `alembic check`.
    assert not hasattr(db_setup, "apply_migrations") and not hasattr(db_setup, "pending_migrations")
