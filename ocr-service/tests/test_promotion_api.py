"""Gom phiếu bầu thành thương hiệu mới (promotion_api). Chạy bằng AI Service giả (xem fake_ai_api.py)."""
import pathlib
import sys

import pytest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))

from classification import brands  # noqa: E402
from parsing import extract  # noqa: E402
from storage import knowledge_api  # noqa: E402
from storage.memory import _key  # noqa: E402
from storage.promotion_api import promote  # noqa: E402
from storage.storage_api import BACKEND_SLUG  # noqa: E402
from tests.fake_ai_api import FakeAiApi  # noqa: E402
from tests.test_knowledge_api import restore_knowledge  # noqa: E402,F401  (khôi phục danh sách toàn cục sau mỗi test)


@pytest.fixture
def api():
    return FakeAiApi()


def _vote(api, user, merchant, category, name=None):
    """Thêm một phiếu cho (cửa hàng, nhóm) vào kết quả giả của view v_merchant_consensus (view thật đếm mỗi người một phiếu từ các bill đã đồng ý đóng góp). ``user`` và ``name`` chỉ để đọc test."""
    slug, key = BACKEND_SLUG[category], _key(merchant)
    for row in api.consensus:
        if row["merchant_key"] == key and row["category_slug"] == slug:
            row["users"] += 1
            return
    api.consensus.append({"merchant_key": key, "category_slug": slug, "users": 1})


def _brand(api, key):
    brand = api.brands.get(key)
    return None if brand is None else (brand["category_slug"], brand["display_name"], brand["whole_word"], brand["is_active"], brand["source"])


def _add_brand(api, key, category, display, source, active=True):
    api.brands[key] = {"category_slug": category, "display_name": display, "whole_word": False, "is_active": active, "source": source}


def test_three_agreeing_users_promote_a_brand_that_recognition_then_uses(api):
    for u in ("u1", "u2", "u3"):
        _vote(api, u, "Tiệm Bánh Hạnh Phúc", "dining", name="Tiệm Bánh Hạnh Phúc")
    result = promote(api, write=True)
    assert result["promoted"] == ["tiem banh hanh phuc"] and not result["pending"]
    category, display, whole, active, source = _brand(api, "tiem banh hanh phuc")
    assert (category, display, bool(whole), bool(active), source) == ("dining", "Tiem Banh Hanh Phuc", False, True, "vote")
    assert knowledge_api.reload(api)
    assert extract.classify_category_detailed("Tiem Banh Hanh Phuc", [])[:2] == ("dining", "brand")


def test_two_users_are_only_a_pending_candidate_until_a_third_agrees(api):
    _vote(api, "u1", "Quán Chè Bà Sáu", "dining")
    _vote(api, "u2", "Quán Chè Bà Sáu", "dining")
    assert promote(api, write=True)["pending"] == ["quan che ba sau"]
    assert _brand(api, "quan che ba sau")[3] in (0, False)  # chưa bật
    _vote(api, "u3", "Quán Chè Bà Sáu", "dining")
    assert promote(api, write=True)["upgraded"] == ["quan che ba sau"]
    assert _brand(api, "quan che ba sau")[3] in (1, True)


def test_a_split_vote_never_goes_live_automatically(api):
    for u in ("u1", "u2"):
        _vote(api, u, "Cửa Hàng Lạ Lùng", "shopping")
    _vote(api, "u3", "Cửa Hàng Lạ Lùng", "dining")  # 2/3 = 67% < 80%
    result = promote(api, write=True)
    assert result["pending"] == ["cua hang la lung"] and not result["promoted"]
    assert _brand(api, "cua hang la lung")[3] in (0, False)


def test_a_single_user_stays_personal(api):
    _vote(api, "u1", "Quán Riêng Của Tôi", "dining")
    result = promote(api, write=True)
    assert not any(result[k] for k in ("promoted", "pending", "upgraded"))
    assert _brand(api, "quan rieng cua toi") is None


def test_keys_too_short_to_match_safely_are_skipped(api):
    for u in ("u1", "u2", "u3", "u4"):
        _vote(api, u, "An", "dining")  # "an" khớp giữa mọi chữ
    result = promote(api, write=True)
    assert result["skipped"] == ["an"] and _brand(api, "an") is None


def test_short_but_safe_keys_become_whole_word_only(api):
    for u in ("u1", "u2", "u3"):
        _vote(api, u, "Pizza4", "dining")
    promote(api, write=True)
    assert _brand(api, "pizza4")[2] in (1, True)


def test_existing_rows_are_never_overwritten_and_disagreement_is_reported(api):
    _add_brand(api, "tiem thuoc xanh", "healthcare", "Tiệm Thuốc Xanh", "review")
    for u in ("u1", "u2", "u3"):
        _vote(api, u, "Tiệm Thuốc Xanh", "shopping")
    result = promote(api, write=True)
    assert result["disputes"] == ["tiem thuoc xanh"]
    assert _brand(api, "tiem thuoc xanh")[0] == "healthcare"  # giữ nguyên


def test_a_name_already_covered_by_a_known_brand_is_skipped(api):
    _add_brand(api, "highlands coffee", "dining", "Highlands Coffee", "seed")
    for u in ("u1", "u2", "u3"):
        _vote(api, u, "Highlands Coffee Quận 9", "dining")
    result = promote(api, write=True)
    assert result["skipped"] == ["highlands coffee quan 9"] and _brand(api, "highlands coffee quan 9") is None


def test_dry_run_writes_nothing_and_rerun_is_idempotent(api):
    for u in ("u1", "u2", "u3"):
        _vote(api, u, "Tiệm Bánh Hạnh Phúc", "dining")
    assert promote(api, write=False)["promoted"] == ["tiem banh hanh phuc"]
    assert _brand(api, "tiem banh hanh phuc") is None
    promote(api, write=True)
    again = promote(api, write=True)
    assert not again["promoted"] and not again["pending"]
    assert len(api.brands) == 1
