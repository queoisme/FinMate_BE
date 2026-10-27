"""Từ vựng hóa đơn dùng chung (ocr-service/data/shared_words.json): bộ phân loại bill và category_resolver của ai-service đọc từ tệp đó,
không giữ bản chép riêng như trước (ocr-service/parsing/extract.py từng có đúng các danh sách này)."""
import ast
import pathlib
import re

import pytest

from app.bill import classifier, lexicon
from app.services import category_resolver

APP = pathlib.Path(__file__).resolve().parents[2] / "app"
SHARED = lexicon.load("shared_words.json")
ROLES_USED = ("generic_store", "groceries_not_for", "not_medicine", "password_line")


def test_the_classifier_reads_its_special_case_vocabulary_from_the_shared_file():
    assert classifier._GENERIC_STORE == tuple(SHARED["generic_store"]["words"])
    assert classifier._GROCERIES_NOT_FOR == tuple(SHARED["groceries_not_for"]["words"])
    assert classifier._NOT_MEDICINE == tuple(SHARED["not_medicine"]["words"])
    for word in SHARED["password_line"]["words"]:
        assert classifier._PASSWORD_LINE.search(f"xx {word}: 1234")


def test_a_supermarket_of_appliances_is_not_groceries_and_a_wifi_password_line_is_ignored():
    assert classifier.classify_category_detailed("SIEU THI DIEN MAY XANH", [])[0] != "groceries"
    plain = classifier.classify_category_detailed("QUAN XYZ", ["ca phe"])
    with_wifi = classifier.classify_category_detailed("QUAN XYZ", ["ca phe", "Pass Wifi: internet 1234"])
    assert plain[:2] == with_wifi[:2]  # dòng pass wifi không kéo bill sang "hóa đơn internet"


def test_the_item_resolver_uses_the_same_words():
    assert category_resolver._prepare("thuoc la 555").split() == ["555"]  # thuốc lá bị bỏ như bộ phân loại bill
    assert category_resolver._prepare("pass wifi 1234") == "" and category_resolver._prepare("mat khau 1234") == ""


@pytest.mark.parametrize("relative", ["bill/classifier.py", "services/category_resolver.py"])
def test_no_shared_word_is_written_again_in_the_code(relative):
    tree = ast.parse((APP / relative).read_text(encoding="utf-8"))
    docstrings = {id(node.body[0].value) for node in ast.walk(tree) if isinstance(node, (ast.Module, ast.FunctionDef, ast.ClassDef)) and node.body
                  and isinstance(node.body[0], ast.Expr) and isinstance(node.body[0].value, ast.Constant)}
    strings = [node.value for node in ast.walk(tree) if isinstance(node, ast.Constant) and isinstance(node.value, str) and id(node) not in docstrings]
    words = {word for role in ROLES_USED for word in SHARED[role]["words"] if " " in word or len(word) > 4}
    offenders = sorted({word for text in strings for word in words if re.search(rf"(?<![a-z_]){re.escape(word)}(?![a-z_])", text)})
    assert not offenders, f"{relative}: {offenders} phải đọc từ shared_words.json, không viết lại"
