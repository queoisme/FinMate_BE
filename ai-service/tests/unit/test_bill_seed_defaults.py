"""Bản mặc định của thương hiệu/từ khóa hóa đơn (app/bill/seed_defaults.py): lấy từ các bảng lúc nạp module, không bị DB làm trôi,
và chính là các tệp dữ liệu dùng chung với ocr-service (một nguồn, không còn bản chép trong code)."""

import json

from app.bill import brands, classifier, lexicon, seed_defaults

OCR_DATA = lexicon.LEXICON_DIR


def test_defaults_are_taken_from_the_code_tables_when_the_module_loads_not_when_seed_runs():
    # apply_rows (nạp từ DB) sửa các bảng trong code tại chỗ; nếu seed đọc lúc chạy thì "mặc định" trôi theo DB.
    keys = {row[0] for row in seed_defaults.default_brands()}
    saved = dict(brands.BRAND_CATEGORIES)
    try:
        brands.BRAND_CATEGORIES.pop("highlands coffee")
        brands.BRAND_CATEGORIES["chi co trong bo nho"] = "dining"
        assert {row[0] for row in seed_defaults.default_brands()} == keys
        assert "highlands coffee" in keys and "chi co trong bo nho" not in keys
    finally:
        brands.BRAND_CATEGORIES.clear()
        brands.BRAND_CATEGORIES.update(saved)


def test_a_keyword_is_never_listed_under_two_categories_of_the_same_kind():
    # Seed bỏ qua từ đã có ở bất kỳ nhóm nào của cùng loại; nếu bản mặc định tự lặp từ thì mục thứ hai sẽ không bao giờ vào DB.
    seen = {}
    for kind, category, word, _ in seed_defaults.default_keywords():
        assert (kind, word) not in seen, (kind, word, seen[(kind, word)], category)
        seen[(kind, word)] = category


def test_category_priority_follows_the_order_of_the_tables():
    ranks = {}
    for kind, category, _word, priority in seed_defaults.default_keywords():
        ranks.setdefault((kind, category), priority)
    for kind, table in (("merchant", classifier._CATEGORY_KEYWORDS), ("product", classifier._PRODUCT_KEYWORDS)):
        assert [ranks[(kind, c)] for c in table] == sorted(ranks[(kind, c)] for c in table)


def test_the_defaults_are_exactly_the_shared_data_files():
    ocr = json.loads((OCR_DATA / "brands.json").read_text(encoding="utf-8"))
    assert {b["key"]: (b["category"], b["display"]) for b in ocr["brands"]} == {k: (c, d) for k, c, d, _ in seed_defaults.default_brands()}
    assert set(ocr["whole_word"]) == {k for k, _c, _d, whole in seed_defaults.default_brands() if whole}
    for kind, name in (("merchant", "merchant_keywords.json"), ("product", "product_keywords.json")):
        theirs = json.loads((OCR_DATA / name).read_text(encoding="utf-8"))
        ours: dict[str, set] = {}
        for k, category, word, _ in seed_defaults.default_keywords():
            if k == kind:
                ours.setdefault(category, set()).add(word)
        assert list(theirs) == list(ours), (kind, "thứ tự nhóm là thứ tự ưu tiên")
        assert {c: set(w) for c, w in theirs.items()} == ours, kind
