"""Nhóm danh mục của OCR đọc từ data/category_groups.json, tệp mà ai-service và ràng buộc CHECK của DB cũng dùng (xem data/NOTES.md)."""
from classification import data_files
from parsing import extract


def test_the_ocr_categories_are_the_shared_group_list():
    document = data_files.load("category_groups.json")
    assert extract.CATEGORIES == tuple(document["groups"])
    assert len(set(extract.CATEGORIES)) == len(extract.CATEGORIES) and "other" in extract.CATEGORIES


def test_every_brand_and_keyword_table_uses_only_known_groups():
    groups = set(extract.CATEGORIES)
    assert {r["category"] for r in data_files.load("brands.json")["brands"]} <= groups
    for table in ("merchant_keywords.json", "product_keywords.json"):
        assert set(data_files.load(table)) <= groups, table
