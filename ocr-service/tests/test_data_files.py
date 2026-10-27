"""Bảng dữ liệu mặc định (thương hiệu, từ khóa) nằm trong data/*.json, không viết cứng trong .py.
Module vẫn giữ nguyên tên và kiểu bảng (dict/set sửa tại chỗ được) vì knowledge_api cập nhật chúng từ DB."""
import json
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from classification import brands, data_files
from parsing import extract


def _json(name):
    return json.loads((ROOT / "data" / name).read_text(encoding="utf-8"))


def test_the_data_directory_is_shipped_in_the_image():
    ignored = {line.strip().rstrip("/") for line in (ROOT / ".dockerignore").read_text(encoding="utf-8").splitlines()}
    assert "data" not in ignored  # Dockerfile `COPY . .` phải mang data/ theo, thiếu là service không có thương hiệu nào


def test_brand_records_are_complete_and_unique():
    records = _json("brands.json")["brands"]
    keys = [r["key"] for r in records]
    assert len(keys) == len(set(keys))
    for r in records:
        assert r["key"] == r["key"].lower() and r["category"] in extract.CATEGORIES and r["display"], r
    assert set(_json("brands.json")["whole_word"]) <= set(keys)


def test_brand_tables_come_from_the_file_and_stay_mutable():
    records = _json("brands.json")["brands"]
    assert list(brands.BRAND_CATEGORIES.items()) == [(r["key"], r["category"]) for r in records]  # thứ tự khóa giữ nguyên
    assert list(brands.BRAND_DISPLAY.items()) == [(r["key"], r["display"]) for r in records]
    assert type(brands.WHOLE_WORD_BRANDS) is set  # knowledge_api.apply() gọi .clear()/.add()


def test_keyword_tables_come_from_the_files_with_tuple_values_in_file_order():
    for name, table in (("merchant_keywords.json", extract._CATEGORY_KEYWORDS), ("product_keywords.json", extract._PRODUCT_KEYWORDS)):
        data = _json(name)
        assert list(table) == list(data), name  # thứ tự nhóm = thứ tự ưu tiên khi chấm
        assert all(table[k] == tuple(v) and isinstance(table[k], tuple) for k, v in data.items()), name


def test_a_missing_data_file_fails_loudly_instead_of_starting_with_empty_tables():
    try:
        data_files.load("khong_co.json")
    except FileNotFoundError:
        return
    raise AssertionError("đọc file không có phải báo lỗi")


def test_eyeglasses_stay_health_but_sunglasses_are_shopping_in_every_default_table():
    # Luật 3 của chỉ dẫn đã duyệt (ai-service, migration 0012): kính mát thời trang là shopping. Bản mặc định lệch DB thì seed thêm lại dòng sai.
    for name in ("merchant_keywords.json", "product_keywords.json"):
        assert "kinh mat" not in _json(name).get("healthcare", []), name
