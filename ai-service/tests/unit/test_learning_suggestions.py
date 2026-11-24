"""Gợi ý luật từ phản hồi: từ khóa đúng dạng bộ phân loại dò, nhận ra gợi ý đã có, và chuyển từ khóa giữa danh mục."""

from app.services.learning_suggestions import MIN_USERS, build_suggestion, lexicon_keyword, with_keyword

LEXICON = {"food": ["highlands"], "shopping": ["winmart"]}


def test_keyword_is_lowercase_with_single_spaces():
    assert lexicon_keyword("  GOGI   HOUSE ") == "gogi house"


def test_new_merchant_is_suggested_with_where_rules_put_it_today():
    s = build_suggestion("GOGI HOUSE", "food", 3, 5, ["other", None, "other"], LEXICON)
    assert (s.keyword, s.current_category, s.covered, s.predicted_categories) == ("gogi house", "other", False, ["other"])


def test_merchant_already_in_the_right_category_is_covered():
    assert build_suggestion("Highlands", "food", 2, 2, [], LEXICON).covered


def test_merchant_that_current_rules_already_place_right_is_covered():
    assert build_suggestion("HIGHLANDS COFFEE", "food", 2, 2, [], LEXICON).covered


def test_adding_a_keyword_moves_it_out_of_other_categories():
    assert with_keyword(LEXICON, "WinMart", "food") == {"food": ["highlands", "winmart"], "shopping": []}


def test_privacy_floor_is_at_least_two_people():
    assert MIN_USERS >= 2
