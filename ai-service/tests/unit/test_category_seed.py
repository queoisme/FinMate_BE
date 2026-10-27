"""Dữ liệu ban đầu của ``kb_categories`` (phương án B): đủ slug backend, đủ nhóm cũ làm dòng bí danh, và không lệch với bản sao cố định trong migration 0022."""

import importlib.util
from pathlib import Path

from app.bill import lexicon
from app.core.category_map import SLUG_BY_LOCAL_CATEGORY
from app.core.enums import CATEGORY_SLUGS
from app.db.category_seed import LABELS, category_rows

MIGRATION = Path(__file__).resolve().parents[2] / "alembic" / "versions" / "0022_kb_categories.py"


def test_every_backend_slug_has_a_label_and_a_primary_row():
    primary = {slug for slug, _label, active, _target, _kind in category_rows() if active}
    assert set(CATEGORY_SLUGS) == primary == set(LABELS)  # thêm slug backend mà quên nhãn hoặc seed sẽ đỏ ở đây


def test_every_legacy_group_is_a_row_and_aliases_point_at_backend_slugs():
    rows = {slug: (active, target) for slug, _label, active, target, _kind in category_rows()}
    for group in lexicon.load("category_groups.json")["groups"]:
        assert group in rows, f"nhóm cũ {group} chưa có trong kb_categories"
        active, target = rows[group]
        if group in CATEGORY_SLUGS:
            assert active and target is None  # trùng slug backend: dòng chính, không cần bí danh
        else:
            assert not active and target == SLUG_BY_LOCAL_CATEGORY[group] and target in CATEGORY_SLUGS


def test_primary_rows_come_first_so_alias_foreign_keys_can_be_satisfied():
    rows = category_rows()
    seen = set()
    for slug, _label, _active, target, _kind in rows:
        assert target is None or target in seen
        seen.add(slug)


def test_migration_copy_matches_the_seed_module():
    spec = importlib.util.spec_from_file_location("migration_0022", MIGRATION)
    migration = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(migration)
    assert migration.CATEGORIES == [row[:4] for row in category_rows()]  # lệch nghĩa là seed trong code và migration đang kể hai câu chuyện khác nhau


def test_kinds_separate_income_expense_and_legacy_aliases():
    kinds = {slug: kind for slug, _label, _active, _target, kind in category_rows()}
    assert kinds["income"] == "income"
    assert {slug for slug, kind in kinds.items() if kind == "legacy"} == {"dining", "groceries", "utilities", "healthcare"}  # đúng các dòng bí danh
    assert {kind for slug, kind in kinds.items() if slug not in ("income", "dining", "groceries", "utilities", "healthcare")} == {"expense"}
