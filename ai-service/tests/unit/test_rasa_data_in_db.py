"""Rasa không có dữ liệu viết cứng: câu huấn luyện/đo nằm trong bảng ``nlu_examples`` của DB ai-service (xem rasa/README.md).

Tệp YAML duy nhất được phép trong rasa/data là ``nlu_db.yml`` (sinh từ DB, nằm trong .gitignore). Máy sinh câu (tools/generate_*.py) ghi YAML thẳng vào
``data/``; nếu ai chạy nó rồi huấn luyện từ tệp đó, câu mới không vào DB, không qua cổng chống rò rỉ train/test, và không ai sửa được trên trang quản trị."""

from pathlib import Path

import pytest

RASA = Path(__file__).resolve().parents[2] / "rasa"
pytestmark = pytest.mark.skipif(not RASA.is_dir(), reason="rasa/ không có trong image")

ALLOWED_DATA = {"nlu_db.yml", "rules.yml", "stories.yml"}


def test_training_data_comes_only_from_the_db_export():
    stray = sorted(p.name for p in (RASA / "data").glob("*.yml") if p.name not in ALLOWED_DATA)
    assert not stray, f"nhập vào DB bằng `scripts/nlu_examples.py import <tệp> --split train` rồi xóa: {stray}"


def test_domain_has_no_hard_coded_replies():
    """ai-service chỉ gọi /model/parse; câu trả lời nằm ở app/data/intent_cues.json (sửa được từ trang quản trị), không lặp lại trong domain.yml."""
    domain = (RASA / "domain.yml").read_text(encoding="utf-8")
    assert "\nresponses:" not in domain and "utter_" not in domain


def test_rules_and_stories_stay_empty():
    for name in ("rules.yml", "stories.yml"):
        text = (RASA / "data" / name).read_text(encoding="utf-8")
        assert "[]" in text, f"{name} phải rỗng: Rasa ở đây chỉ làm NLU"
