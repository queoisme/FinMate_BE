import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "benchmark"))

from run_items import match_labels  # noqa: E402


def test_match_labels_needs_name_and_amount_and_uses_each_item_once():
    items = [{"name": "Kem BINGGRAE", "amount": 30000}, {"name": "Sira chua uong", "amount": 12000}]
    labels = [{"name_contains": "binggrae", "amount": 30000}, {"name_contains": "binggrae", "amount": 30000},
              {"name_contains": "sua chua", "amount": 12000}]
    assert match_labels(labels, items) == [True, False, False]
    assert match_labels(labels, items, by_name=False) == [True, False, True]


def test_fuzzy_name_tolerates_ocr_misspelling_but_not_a_different_item():
    items = [{"name": "Sira chua uong SCU 200ml", "amount": 12000}, {"name": "Banh mi thit", "amount": 30000},
             {"name": "lanh Bao TP T.Heo", "amount": 192000}]
    labels = [{"name_contains": "sua chua uong", "amount": 12000}, {"name_contains": "nuoc cam", "amount": 30000},
              {"name_contains": "banh bao", "amount": 192000}]
    assert match_labels(labels, items, fuzzy=True) == [True, False, True]
    assert match_labels(labels, items) == [False, False, False]
