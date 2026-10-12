"""Bộ phân loại chung: một cụm từ ra cùng một danh mục dù đi bằng chat hay bằng món trên bill."""

import pytest

from app.bill import classifier
from app.core import category_resolver
from app.core.category_resolver import Resolution, invalidate, resolve
from app.core.enums import CATEGORY_SLUGS


@pytest.mark.parametrize(
    ("phrase", "slug"),
    [
        ("ta quan Bobby", "family"),
        ("tã quần Bobby", "family"),
        ("sua bot Enfa", "family"),
        ("sữa bột Enfa A+ 800g", "family"),
        ("hoc phi", "education"),
        ("kham benh", "health"),
        ("bot giat Omo", "shopping"),
        ("vitamin C", "health"),
        ("nuoc ngot Pepsi", "food"),
        ("khan uot", "shopping"),
        ("sach giao khoa", "education"),
    ],
)
def test_same_phrase_same_answer_for_the_phrases_that_disagreed_before(phrase, slug):
    assert resolve(phrase) == Resolution(slug, "keyword")


def test_equal_length_tie_goes_to_the_specific_slug():
    # "sua bot" có ở bill (groceries -> food) và ở chat (family): family cụ thể hơn nên thắng
    assert resolve("sua bot").slug == "family"
    # "tien nuoc": housing (bill, chat) và bills (từ điển cửa hàng) cùng dài; nhà ở đứng trước hóa đơn
    assert resolve("tien nuoc").slug == "housing"


def test_longest_keyword_wins_across_sources():
    assert resolve("mua sua bot").slug == "family"  # "mua sua bot" (chat) dài hơn "mua sua" (chat, groceries)
    assert resolve("sua xe may").slug == "transport"  # "sua xe" thắng mọi từ "sua" đứng một mình


@pytest.mark.parametrize("phrase", ["nuoc", "mua nuoc", "sua nha", "SP002993", "A12", "", "   "])
def test_ambiguous_words_and_codes_stay_none(phrase):
    assert resolve(phrase) == Resolution(None, "none")


def test_one_letter_typo_matches_but_two_real_words_do_not():
    assert resolve("khan uott") == Resolution("shopping", "fuzzy")  # thừa một chữ
    assert resolve("kha uot") == Resolution("shopping", "fuzzy")  # thiếu một chữ
    assert resolve("nuoc muoi sinh ly").slug is None  # nước muối ≠ nước suối


def test_a_longer_typo_match_beats_a_shorter_exact_keyword():
    # OCR hay đọc nhầm n/m: "kham uot" là "khan uot" (8 ký tự), không phải "kham" (khám, 4 ký tự) + "uot"
    assert resolve("kham uot") == Resolution("shopping", "fuzzy")
    assert resolve("KHAM UOT Bobby").slug == "shopping"


def test_an_exact_keyword_as_long_or_longer_is_never_overridden_by_a_typo_match():
    assert resolve("kham benh") == Resolution("health", "keyword")
    assert resolve("tien dien") == Resolution("housing", "keyword")


def test_digits_glued_to_a_word_still_match_and_money_words_do_not_crash():
    assert resolve("bap bo200g").slug == "food"
    assert resolve("45.000đ").slug is None


def test_web_addresses_and_wifi_password_lines_are_ignored():
    assert resolve("www.internet.com").slug is None
    assert resolve("Pass Wifi: 12345678").slug is None


def test_invalidate_picks_up_edited_tables(monkeypatch):
    try:
        invalidate()
        assert resolve("zxqv thing").slug is None  # xây chỉ mục lần đầu
        monkeypatch.setitem(classifier._PRODUCT_KEYWORDS, "other", classifier._PRODUCT_KEYWORDS["other"] + ("zxqv thing",))
        assert resolve("zxqv thing").slug is None  # chỉ mục đang dùng chưa đổi cho tới khi bị đánh dấu cũ
        invalidate()
        assert resolve("zxqv thing").slug == "other"
    finally:
        monkeypatch.undo()
        invalidate()


def test_never_returns_income_or_a_slug_outside_the_taxonomy():
    slugs = set(category_resolver._index().values())
    assert slugs <= set(CATEGORY_SLUGS) - {"income"}


def test_baby_bottle_is_family_not_groceries_because_of_the_word_cho():
    assert resolve("bình sữa cho bé").slug == "family"  # "cho" (chợ) đứng một mình là groceries, "binh sua" dài hơn thì thắng


def test_fuzzy_can_be_turned_off_for_typed_text():
    assert resolve("khan uott", fuzzy=False) == Resolution(None, "none")
    assert resolve("hôm nay trời đẹp quá", fuzzy=False).slug is None  # "dep qua" gần "dep quai" nhưng đây là câu chat, không phải lỗi OCR
    assert resolve("khan uot", fuzzy=False).slug == "shopping"


def test_pork_belly_is_an_exact_keyword_not_a_lucky_typo_match():
    assert resolve("thịt ba chỉ") == Resolution("food", "keyword")  # trước đây chỉ ra food nhờ khớp gần đúng "thit ba" ~ "thit bo"
