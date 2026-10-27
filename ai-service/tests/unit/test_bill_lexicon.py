"""Bộ nạp bảng thương hiệu/từ khóa hóa đơn dùng chung với ocr-service (app/bill/lexicon.py)."""

import json

import pytest

from app.bill import brands, classifier, lexicon


def test_a_missing_data_file_fails_loudly_instead_of_starting_with_empty_tables(tmp_path, monkeypatch):
    monkeypatch.setattr(lexicon, "LEXICON_DIR", tmp_path)
    with pytest.raises(RuntimeError, match="LEXICON_DIR"):
        lexicon.load("brands.json")


def test_keyword_tables_keep_group_order_and_use_tuples(tmp_path, monkeypatch):
    # thứ tự nhóm trong tệp là thứ tự ưu tiên khi trùng từ khóa
    (tmp_path / "k.json").write_text(json.dumps({"zzz": ["a", "b"], "aaa": ["c"]}), encoding="utf-8")
    monkeypatch.setattr(lexicon, "LEXICON_DIR", tmp_path)
    table = lexicon.keyword_table("k.json")
    assert list(table) == ["zzz", "aaa"]
    assert table["zzz"] == ("a", "b") and isinstance(table["zzz"], tuple)


def test_the_environment_variable_chooses_the_directory(tmp_path, monkeypatch):
    monkeypatch.setenv("LEXICON_DIR", str(tmp_path))
    assert lexicon._directory() == tmp_path


def test_brand_tables_come_from_the_shared_file_and_stay_mutable():
    records = lexicon.load("brands.json")["brands"]
    assert list(brands.BRAND_CATEGORIES.items()) == [(r["key"], r["category"]) for r in records]  # thứ tự khóa giữ nguyên
    assert list(brands.BRAND_DISPLAY.items()) == [(r["key"], r["display"]) for r in records]
    assert type(brands.WHOLE_WORD_BRANDS) is set  # knowledge.py cập nhật tại chỗ từ DB


def test_backend_slug_overrides_only_name_brands_that_exist_in_the_brand_table():
    # Một mục nằm ngoài bảng thương hiệu sẽ không bao giờ được dùng: chỉ là chữ chết, dễ gây hiểu nhầm là đã có hiệu lực.
    assert set(brands.BRAND_BACKEND_SLUG) <= set(brands.BRAND_CATEGORIES)


def test_name_only_keywords_are_loaded_from_the_shared_file():
    assert classifier._NAME_ONLY_KEYWORDS == set(lexicon.load("name_only_keywords.json"))
    assert classifier._NAME_ONLY_KEYWORDS  # không rỗng
