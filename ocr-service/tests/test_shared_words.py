"""Từ vựng hóa đơn dùng chung (data/shared_words.json) có MỘT nguồn: ocr-service, ai-service và backend đều đọc tệp này, không chép riêng.
Test này canh dạng tệp và việc mã OCR không viết lại các từ đó ở chỗ khác."""
import ast
import json
import pathlib
import re
import unicodedata

import pytest

from classification import data_files
from parsing import extract, items, totals

ROOT = pathlib.Path(__file__).resolve().parents[1]
DOCUMENT = {k: v for k, v in json.loads((ROOT / "data" / "shared_words.json").read_text(encoding="utf-8")).items() if not k.startswith("_")}
ACCENTED_ROLES = {"tax_accented"}  # dạng có dấu cho nơi khớp trên chữ chưa bỏ dấu
REGEX_ROLES = {"tax_ocr_misread"}


def fold(text: str) -> str:
    return unicodedata.normalize("NFD", text.replace("đ", "d").replace("Đ", "D")).encode("ascii", "ignore").decode()


def test_every_role_is_documented_and_its_words_are_lowercase_and_unaccented():
    for role, entry in DOCUMENT.items():
        assert entry["doc"].strip(), role
        if role in REGEX_ROLES:
            assert entry["regex"] and all(re.compile(pattern) for pattern in entry["regex"]), role
            continue
        words = entry["words"]
        assert words and len(words) == len(set(words)), role
        for word in words:
            assert word == word.lower() and word == " ".join(word.split()), f"{role}: '{word}'"
            if role not in ACCENTED_ROLES:
                assert word == fold(word), f"{role}: '{word}' phải bỏ dấu (mục có dấu duy nhất là {sorted(ACCENTED_ROLES)})"


def test_a_role_accented_only_for_consumers_that_match_unfolded_text():
    assert all(fold(word) in DOCUMENT["tax"]["words"] for word in DOCUMENT["tax_accented"]["words"])  # mỗi dạng có dấu có dạng bỏ dấu tương ứng


@pytest.mark.parametrize("role,pattern", [("discount", items._DISCOUNT_RE), ("discount", totals._DISCOUNT), ("service", items._SURCHARGE_RE), ("service", totals._SERVICE),
                                          ("service_glued", items._SURCHARGE_RE), ("service_glued", totals._SERVICE), ("tax", items._SURCHARGE_RE),
                                          ("tax", totals._TAX), ("tax_glued", items._SURCHARGE_RE), ("tax_glued", totals._TAX), ("pre_tax", items._PRE_TAX_RE),
                                          ("cash_given", totals._CASH), ("cash_given", items._CASH_ROW_RE), ("cash_given", items._PAYMENT_RE),
                                          ("change", totals._CHANGE), ("change", items._CASH_ROW_RE), ("change", items._PAYMENT_RE),
                                          ("payment_tender", items._CASH_ROW_RE), ("payment_tender", items._PAYMENT_RE), ("payment_label", items._CASH_ROW_RE),
                                          ("payment_label", totals._PAYMENT_METHOD), ("payment_tender", extract._CASH_LABEL), ("cash_given", extract._CASH_LABEL), ("change", extract._CASH_LABEL)])
def test_every_word_of_a_role_is_matched_by_the_regexes_built_from_it(role, pattern):
    for word in data_files.shared_words(role):
        assert pattern.search(f"xx {word} yy"), f"{role}: '{word}' phải khớp"


def test_whole_word_roles_do_not_match_inside_other_words():
    assert not items._SURCHARGE_RE.search("vatican") and not items._SURCHARGE_RE.search("thuedi")  # "vat", "thue" khớp nguyên từ
    assert items._SURCHARGE_RE.search("thuegtgt8") and items._SURCHARGE_RE.search("servicecharge")  # nhưng gtgt/service khớp cả khi dính
    assert totals._TAX.search("thu€ gtgt") and totals._TAX.search("thu& 8%")  # chữ "Thuế" bị OCR đọc sai
    assert not items._PRE_TAX_RE.search("chuaxyz") and items._PRE_TAX_RE.search("tong chua thue")


def _strings(path: pathlib.Path) -> list[str]:
    tree = ast.parse(path.read_text(encoding="utf-8"))
    docstrings = {id(node.body[0].value) for node in ast.walk(tree) if isinstance(node, (ast.Module, ast.FunctionDef, ast.ClassDef)) and node.body
                  and isinstance(node.body[0], ast.Expr) and isinstance(node.body[0].value, ast.Constant)}
    return [node.value for node in ast.walk(tree) if isinstance(node, ast.Constant) and isinstance(node.value, str) and id(node) not in docstrings]


@pytest.mark.parametrize("relative", ["parsing/items.py", "parsing/totals.py", "parsing/extract.py"])
def test_no_shared_word_is_written_again_in_the_parsing_code(relative):
    words = {word for role, entry in DOCUMENT.items() if role not in REGEX_ROLES | ACCENTED_ROLES for word in entry["words"] if " " in word or word in {"thue", "gtgt", "chua"}}
    offenders = sorted({word for text in _strings(ROOT / relative) for word in words if re.search(rf"(?<![a-z_]){re.escape(word)}(?![a-z_])", text)})
    assert not offenders, f"{relative}: {offenders} phải đọc từ data/shared_words.json, không viết lại"
