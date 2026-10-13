"""``dictionaries/*.json``: từ vựng phân loại chat nằm ở tệp dữ liệu (mỗi nhóm một tệp) để sửa không cần động vào code."""
import json
from pathlib import Path

import pytest

from app.services import vocab
from app.services.vi_text import fold

DIR = Path(__file__).resolve().parents[2] / "dictionaries"


def write(directory, name, kind, label, keywords):
    (directory / f"{name}.json").write_text(json.dumps({"kind": kind, "label": label, "keywords": keywords}, ensure_ascii=False), encoding="utf-8")


def test_every_group_has_its_file_and_the_tables_come_from_them():
    names = {p.stem for p in DIR.glob("*.json")}
    assert names == set(vocab.CATEGORY_LABELS) | set(vocab.INCOME_LABELS)
    assert {k for k, v in vocab.CHAT_KEYWORDS.items() if v} <= names


def test_keywords_are_folded_and_unique_across_groups():
    seen = {}
    for path in DIR.glob("*.json"):
        for word in json.loads(path.read_text(encoding="utf-8"))["keywords"]:
            assert word == " ".join(fold(word).split()), f"{path.name}: '{word}' phải viết thường, bỏ dấu, gọn khoảng trắng"
            assert word not in seen, f"'{word}' có ở cả {seen[word]} và {path.name}"
            seen[word] = path.name


def test_the_loader_reads_labels_keywords_and_kinds(tmp_path):
    write(tmp_path, "food_test", "expense", "Ăn thử", ["pho", "bun"])
    write(tmp_path, "salary_test", "income", "Lương thử", ["luong"])

    keywords, categories, incomes = vocab.load_dictionaries(tmp_path)

    assert keywords == {"food_test": ("pho", "bun"), "salary_test": ("luong",)}
    assert categories == {"food_test": "Ăn thử"}
    assert incomes == {"salary_test": "Lương thử"}


def test_a_broken_dictionary_fails_loudly(tmp_path):
    (tmp_path / "bad.json").write_text("{not json", encoding="utf-8")
    with pytest.raises(ValueError):
        vocab.load_dictionaries(tmp_path)
