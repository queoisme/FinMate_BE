"""Chất lượng và độ phủ của DỮ LIỆU Rasa học và được đo (``rasa/data/nlu_db.yml`` = tập train xuất từ DB, ``rasa/tests/nlu_test*.yml`` = các tập test).

Không cần Rasa chạy. Tệp ``nlu_db.yml`` do ``scripts/nlu_examples.py export-all`` sinh và không nằm trong git, nên thiếu thì bỏ qua.
Chỗ MỎNG đã đo được ghi bằng ``xfail(strict=True)``: bổ sung dữ liệu xong thì test đổi màu và nhắc gỡ dấu."""
from __future__ import annotations

import re
import unicodedata
from collections import Counter, defaultdict
from pathlib import Path

import pytest
import yaml

RASA = Path(__file__).resolve().parents[2] / "rasa"
TRAIN = RASA / "data" / "nlu_db.yml"
pytestmark = pytest.mark.skipif(not TRAIN.is_file(), reason="rasa/data/nlu_db.yml chưa được xuất từ DB")

INTENTS = ("add_expense", "add_income", "transfer_money", "ask_spending", "greet", "goodbye", "confirm", "deny", "cancel", "out_of_scope")
ANNOTATION = re.compile(r"\[([^\]]+)\]\(([^)]+)\)")


def _rows(path: Path) -> list[tuple[str, str]]:
    out = []
    for block in (yaml.safe_load(path.read_text(encoding="utf-8")) or {}).get("nlu", []):
        out += [(block["intent"], line.strip()[2:].strip()) for line in block.get("examples", "").splitlines() if line.strip().startswith("- ")]
    return out


def _key(text: str) -> str:
    return " ".join(unicodedata.normalize("NFC", ANNOTATION.sub(r"\1", text)).lower().split())


@pytest.fixture(scope="module")
def train() -> list[tuple[str, str]]:
    return _rows(TRAIN)


@pytest.fixture(scope="module")
def test() -> list[tuple[str, str]]:
    return [row for path in sorted((RASA / "tests").glob("nlu_test*.yml")) for row in _rows(path)]


def test_every_intent_is_present_in_train_and_test(train, test):
    assert set(INTENTS) <= {i for i, _ in train} and set(INTENTS) <= {i for i, _ in test}
    assert {i for i, _ in train} <= set(INTENTS), "ý định lạ trong dữ liệu train"


def test_no_sentence_is_in_both_train_and_test(train, test):
    assert {_key(t) for _, t in train} & {_key(t) for _, t in test} == set()


def test_no_training_sentence_has_two_different_labels(train):
    labels = defaultdict(set)
    for intent, text in train:
        labels[_key(text)].add(intent)
    assert [k for k, v in labels.items() if len(v) > 1] == []


def test_entity_annotations_are_well_formed_and_known(train):
    known = {"amount", "merchant", "date"}
    for _, text in train:
        rest = ANNOTATION.sub("", text)  # câu rác như "=))" có ngoặc đơn lẻ nhưng vô hại; chỉ chú thích dở dang mới làm Rasa học sai
        assert "[" not in rest and "]" not in rest, text
        assert {m.group(2) for m in ANNOTATION.finditer(text)} <= known, text


def test_a_real_share_of_training_sentences_has_no_accents(train):
    """Người dùng gõ vội không dấu ("an trua 40k"): model chỉ học được nếu thấy loại câu này."""
    def plain(s: str) -> str:
        return "".join(c for c in unicodedata.normalize("NFD", s) if unicodedata.category(c) != "Mn").replace("đ", "d")

    unaccented = sum(plain(ANNOTATION.sub(r"\1", t)) == ANNOTATION.sub(r"\1", t) and any(c.isalpha() for c in t) for _, t in train)
    assert unaccented / len(train) >= 0.10


def test_main_intents_have_enough_training_sentences(train):
    counts = Counter(i for i, _ in train)
    assert {i: counts[i] for i in ("add_expense", "add_income", "transfer_money", "ask_spending", "out_of_scope") if counts[i] < 300} == {}


def test_off_topic_is_a_large_enough_share_to_refuse_confidently(train):
    share = sum(i == "out_of_scope" for i, _ in train) / len(train)
    assert 0.20 <= share <= 0.55, share


def test_small_intents_have_at_least_thirty_training_sentences(train):  # đã sửa 2026-10-31: bổ sung goodbye/greet/confirm/deny/cancel (seed/nlu_coverage_gaps.jsonl)
    counts = Counter(i for i, _ in train)
    assert {i: counts[i] for i in ("greet", "goodbye", "confirm", "deny", "cancel") if counts[i] < 30} == {}


@pytest.mark.xfail(strict=True, reason="RỦI RO: tập test của greet (3), goodbye (2), confirm/deny/cancel (5) quá nhỏ: một câu sai đổi 20-50 điểm phần trăm nên không đo được")
def test_every_intent_has_enough_test_sentences_to_measure(test):
    counts = Counter(i for i, _ in test)
    assert {i: counts[i] for i in INTENTS if counts[i] < 15} == {}


@pytest.mark.xfail(strict=True, reason="RỦI RO: thực thể merchant chỉ có 36 ví dụ (amount 1947): Rasa gần như không tự rút tên cửa hàng, app dựa vào luật")
def test_the_merchant_entity_has_enough_examples(train):
    seen = Counter(m.group(2) for _, t in train for m in ANNOTATION.finditer(t))
    assert seen["merchant"] >= 300, seen["merchant"]
