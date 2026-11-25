"""Từ vựng tiếng Việt (đơn vị thời gian, từ đệm, kiểu nói đặt mục tiêu...) nằm ở app/data/intent_cues.json, KHÔNG viết cứng trong code.

Quy tắc của dự án: từ khóa và mẫu thay đổi được thì để ở cấu hình (admin sửa được, có hiệu lực không cần deploy); code chỉ giữ cơ chế (tính ngày, kiểm tra, quyết định).
Test này canh hai module hiểu câu đặt mục tiêu: không được có chữ bỏ dấu của từ vựng trong các chuỗi (chú thích và docstring có dấu thì tự do).
"""

import ast
import re
from pathlib import Path

import pytest

SERVICES = Path(__file__).resolve().parents[2] / "app" / "services"
# Chữ đã bỏ dấu của từ vựng hạn / đặt mục tiêu. Nếu cần thêm từ mới vào hành vi: thêm vào intent_cues.json, không vào code.
VOCABULARY = re.compile(r"(?<![a-z_])(thang|tuan|ngay|ruoi|cuoi|quy|nha|nua|toi|tich luy|danh dum|de danh|muc tieu|ke hoach|khoang|trong vong|ky han)(?![a-z_])")


def _string_literals(path: Path):
    for node in ast.walk(ast.parse(path.read_text(encoding="utf-8"))):
        if isinstance(node, ast.Constant) and isinstance(node.value, str):
            if isinstance(getattr(node, "parent", None), ast.Expr):
                continue
            yield node.lineno, node.value


@pytest.mark.parametrize("module", ["goal_parser.py", "chat_ai.py", "item_parser.py"])
def test_no_vietnamese_vocabulary_hardcoded_in_the_module(module):
    tree = ast.parse((SERVICES / module).read_text(encoding="utf-8"))
    for node in ast.walk(tree):
        for child in ast.iter_child_nodes(node):
            child.parent = node  # type: ignore[attr-defined]
    found = []
    for node in ast.walk(tree):
        if isinstance(node, ast.Constant) and isinstance(node.value, str) and not isinstance(getattr(node, "parent", None), ast.Expr):  # bỏ docstring
            if VOCABULARY.search(node.value):
                found.append((node.lineno, node.value[:60]))
    assert not found, f"{module}: từ vựng viết cứng trong code, chuyển sang app/data/intent_cues.json: {found}"


def test_valued_lists_expand_to_their_words_only_and_expose_their_numbers():
    from app.services import intent_cues

    cues = intent_cues.current()

    numbers = intent_cues.valued(cues, "goal_number_words")
    assert numbers["ba"] == 3 and numbers["tu"] == numbers["bon"] == 4
    assert not re.search(r"[a-z]:\d", cues.patterns["goal_relative"].pattern), "mục dạng từ:số chỉ chèn phần từ vào mẫu, không kèm giá trị"
    assert intent_cues.valued(cues, "goal_year_range") == {"min": 2000, "max": 2200}
