"""Gom phiếu bầu thành thương hiệu mới (promotion_pg). Chạy bằng SQLite giả lập (xem pg_fake.py)."""
import pathlib
import sys

import pytest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))

from classification import brands  # noqa: E402
from parsing import extract  # noqa: E402
from storage import knowledge_pg  # noqa: E402
from storage.promotion_pg import promote  # noqa: E402
from storage.storage_pg import PgMerchantMemory, _cursor  # noqa: E402
from tests.pg_fake import make_connect  # noqa: E402
from tests.test_knowledge_pg import restore_knowledge  # noqa: E402,F401  (khôi phục danh sách toàn cục sau mỗi test)


@pytest.fixture
def connect(tmp_path):
    return make_connect(tmp_path / "pg.sqlite")


def _vote(connect, user, merchant, category, name=None):
    PgMerchantMemory(connect, user).set(merchant, category, name=name)


def _brand(connect, key):
    with _cursor(connect) as cur:
        cur.execute("SELECT category_slug, display_name, whole_word, is_active, source FROM bill_brands WHERE brand_key = %s", (key,))
        return cur.fetchone()


def test_three_agreeing_users_promote_a_brand_that_recognition_then_uses(connect):
    for u in ("u1", "u2", "u3"):
        _vote(connect, u, "Tiệm Bánh Hạnh Phúc", "dining", name="Tiệm Bánh Hạnh Phúc")
    result = promote(connect, write=True)
    assert result["promoted"] == ["tiem banh hanh phuc"] and not result["pending"]
    category, display, whole, active, source = _brand(connect, "tiem banh hanh phuc")
    assert (category, display, bool(whole), bool(active), source) == ("dining", "Tiệm Bánh Hạnh Phúc", False, True, "vote")
    assert knowledge_pg.reload(connect)
    assert extract.classify_category_detailed("Tiem Banh Hanh Phuc", [])[:2] == ("dining", "brand")


def test_two_users_are_only_a_pending_candidate_until_a_third_agrees(connect):
    _vote(connect, "u1", "Quán Chè Bà Sáu", "dining")
    _vote(connect, "u2", "Quán Chè Bà Sáu", "dining")
    assert promote(connect, write=True)["pending"] == ["quan che ba sau"]
    assert _brand(connect, "quan che ba sau")[3] in (0, False)  # chưa bật
    _vote(connect, "u3", "Quán Chè Bà Sáu", "dining")
    assert promote(connect, write=True)["upgraded"] == ["quan che ba sau"]
    assert _brand(connect, "quan che ba sau")[3] in (1, True)


def test_a_split_vote_never_goes_live_automatically(connect):
    for u in ("u1", "u2"):
        _vote(connect, u, "Cửa Hàng Lạ Lùng", "shopping")
    _vote(connect, "u3", "Cửa Hàng Lạ Lùng", "dining")  # 2/3 = 67% < 80%
    result = promote(connect, write=True)
    assert result["pending"] == ["cua hang la lung"] and not result["promoted"]
    assert _brand(connect, "cua hang la lung")[3] in (0, False)


def test_a_single_user_stays_personal(connect):
    _vote(connect, "u1", "Quán Riêng Của Tôi", "dining")
    result = promote(connect, write=True)
    assert not any(result[k] for k in ("promoted", "pending", "upgraded"))
    assert _brand(connect, "quan rieng cua toi") is None


def test_keys_too_short_to_match_safely_are_skipped(connect):
    for u in ("u1", "u2", "u3", "u4"):
        _vote(connect, u, "An", "dining")  # "an" khớp giữa mọi chữ
    result = promote(connect, write=True)
    assert result["skipped"] == ["an"] and _brand(connect, "an") is None


def test_short_but_safe_keys_become_whole_word_only(connect):
    for u in ("u1", "u2", "u3"):
        _vote(connect, u, "Pizza4", "dining")
    promote(connect, write=True)
    assert _brand(connect, "pizza4")[2] in (1, True)


def test_existing_rows_are_never_overwritten_and_disagreement_is_reported(connect):
    with _cursor(connect) as cur:
        cur.execute("INSERT INTO bill_brands (id, brand_key, category_slug, display_name, whole_word, is_active, source) "
                    "VALUES ('x', 'tiem thuoc xanh', 'healthcare', 'Tiệm Thuốc Xanh', 0, 1, 'review')")
    for u in ("u1", "u2", "u3"):
        _vote(connect, u, "Tiệm Thuốc Xanh", "shopping")
    result = promote(connect, write=True)
    assert result["disputes"] == ["tiem thuoc xanh"]
    assert _brand(connect, "tiem thuoc xanh")[0] == "healthcare"  # giữ nguyên


def test_a_name_already_covered_by_a_known_brand_is_skipped(connect):
    with _cursor(connect) as cur:
        cur.execute("INSERT INTO bill_brands (id, brand_key, category_slug, display_name, whole_word, is_active, source) "
                    "VALUES ('x', 'highlands coffee', 'dining', 'Highlands Coffee', 0, 1, 'seed')")
    for u in ("u1", "u2", "u3"):
        _vote(connect, u, "Highlands Coffee Quận 9", "dining")
    result = promote(connect, write=True)
    assert result["skipped"] == ["highlands coffee quan 9"] and _brand(connect, "highlands coffee quan 9") is None


def test_dry_run_writes_nothing_and_rerun_is_idempotent(connect):
    for u in ("u1", "u2", "u3"):
        _vote(connect, u, "Tiệm Bánh Hạnh Phúc", "dining")
    assert promote(connect, write=False)["promoted"] == ["tiem banh hanh phuc"]
    assert _brand(connect, "tiem banh hanh phuc") is None
    promote(connect, write=True)
    again = promote(connect, write=True)
    assert not again["promoted"] and not again["pending"]
    with _cursor(connect) as cur:
        cur.execute("SELECT count(*) FROM bill_brands")
        assert cur.fetchone()[0] == 1
