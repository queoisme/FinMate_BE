"""scripts/nlu_examples.py: chia tập tất định, kiểm tra đầu vào, khóa so trùng (không cần DB)."""

import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts"))

import nlu_examples as script  # noqa: E402

from app.db.models.nlu_example import text_key  # noqa: E402


def test_text_key_ignores_case_and_spacing_but_keeps_accents():
    assert text_key("  Chatgpt   VỚI Claude ") == text_key("chatgpt với claude")
    assert text_key("bạn") != text_key("ban")  # hai câu huấn luyện khác nhau


def test_default_split_is_deterministic_and_roughly_one_third_test():
    keys = [f"cau thu {i}" for i in range(600)]
    first = [script.default_split(k) for k in keys]
    assert first == [script.default_split(k) for k in keys]
    assert 150 < first.count("test") < 250
    assert set(first) == {"train", "test"}


def test_problem_rejects_bad_rows():
    good = {"text": "chatgpt với claude cái nào mạnh hơn", "intent": "out_of_scope"}
    assert script.problem(good) is None
    assert script.problem({**good, "text": "   "})
    assert script.problem({**good, "text": "dòng 1\ndòng 2"})
    assert script.problem({**good, "text": "mua [cà phê"})  # dấu [ lẻ làm Rasa học sai
    assert script.problem({**good, "text": "mua [cà phê](foo)"})  # thực thể không có trong domain.yml
    assert script.problem({**good, "text": "mua [cà phê] (merchant)"})  # sai cú pháp chú thích
    assert script.problem({**good, "text": "x" * 201})
    assert script.problem({**good, "intent": "Out Of Scope"})
    assert script.problem({**good, "split": "dev"})
    assert script.problem({**good, "source": "gemini"})  # nhãn chỉ đến từ seed / người dùng / người soát


def test_entity_annotations_are_valid_and_do_not_change_the_dedup_key():
    from app.db.models.nlu_example import plain_text

    good = {"text": "ăn phở bò [45k](amount) ở [Phở 24](merchant) hôm [qua](date)", "intent": "add_expense"}
    assert script.problem(good) is None
    assert plain_text(good["text"]) == "ăn phở bò 45k ở Phở 24 hôm qua"
    assert text_key(good["text"]) == text_key("ăn phở bò 45k ở Phở 24 hôm qua")  # cùng câu, dù có gắn nhãn hay không


def test_test_file_names_keep_the_old_names_so_audit_and_eval_scripts_still_find_them():
    assert script.test_file_name("nlu_test_fresh2") == "nlu_test_fresh2.yml"
    assert script.test_file_name("ai_tech") == "nlu_test_db_ai_tech.yml"
    assert script.test_file_name(None) == "nlu_test_db.yml"


def test_yaml_import_reads_intents_examples_and_tags_with_the_file_name(tmp_path):
    path = tmp_path / "nlu_test_fresh.yml"
    path.write_text(
        'version: "3.1"\nnlu:\n- intent: add_expense\n  examples: |\n    - cafe [35k](amount)\n    - đi ăn\n- synonym: x\n  examples: |\n    - y\n', encoding="utf-8"
    )
    rows = script.read_rows(path, "test", None)
    assert rows == [
        {"text": "cafe [35k](amount)", "intent": "add_expense", "split": "test", "tag": "nlu_test_fresh"},
        {"text": "đi ăn", "intent": "add_expense", "split": "test", "tag": "nlu_test_fresh"},
    ]
    with pytest.raises(SystemExit):
        script.read_rows(path, None, None)  # YAML không nói câu thuộc tập nào
