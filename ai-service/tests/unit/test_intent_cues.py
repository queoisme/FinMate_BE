"""Bộ nạp từ vựng (app/data/intent_cues.json) và các NGUYÊN TẮC phân xử Rasa/luật. Mỗi ca dùng một loại câu khác với các câu đã gặp lỗi:
nếu luật chỉ thuộc từng câu thì test này đỏ."""

import json
from datetime import datetime

import pytest

from app.services import intent_cues
from app.services.transaction_parser import VN, parse_chat

NOW = datetime(2026, 10, 21, 10, 0, tzinfo=VN)


def rasa(intent: str, confidence: float = 1.0) -> dict:
    return {"intent": intent, "confidence": confidence, "entities": {}}


def _data() -> dict:
    return json.loads(intent_cues.DATA_PATH.read_text(encoding="utf-8"))


class TestLoader:
    def test_every_pattern_compiles_and_folded_lists_are_folded(self):
        cues = intent_cues.load()
        assert cues.patterns["giving_away"].search("cho me 500k")  # "mẹ" trong danh sách đã được bỏ dấu cho mẫu on=folded
        assert cues.patterns["income_specific"].search("em gái gửi mình 500k")  # mẫu on=accented giữ nguyên dấu

    def test_unknown_reference_fails_loudly(self, tmp_path):
        data = _data()
        data["patterns"]["fee"]["regex"] = "<<khong_ton_tai>>"
        bad = tmp_path / "bad.json"
        bad.write_text(json.dumps(data), encoding="utf-8")
        with pytest.raises(ValueError, match="khong_ton_tai"):
            intent_cues.load(bad)

    def test_circular_reference_fails_loudly(self, tmp_path):
        data = _data()
        data["fragments"]["a"], data["fragments"]["b"] = "x<<b>>", "y<<a>>"
        data["patterns"]["fee"]["regex"] = "<<a>>"
        bad = tmp_path / "bad.json"
        bad.write_text(json.dumps(data), encoding="utf-8")
        with pytest.raises(ValueError, match="vòng"):
            intent_cues.load(bad)

    def test_invalid_matching_mode_fails_loudly(self, tmp_path):
        data = _data()
        data["patterns"]["fee"]["on"] = "whatever"
        bad = tmp_path / "bad.json"
        bad.write_text(json.dumps(data), encoding="utf-8")
        with pytest.raises(ValueError, match="folded hoặc accented"):
            intent_cues.load(bad)


class TestSpecificCuesBeatAConfidentModel:
    """Nguyên tắc 4: động từ/vai trò nói rõ chiều tiền thắng model, vì độ chắc softmax của Rasa chưa hiệu chỉnh."""

    @pytest.mark.parametrize("text,wrong", [
        ("bán chiếc điện thoại cũ 3 triệu", "add_expense"),  # động từ "bán", không liệt kê đồ vật
        ("bán lại cái tủ lạnh 2 triệu", "add_expense"),
        ("em gái gửi mình 500k", "transfer_money"),  # chủ ngữ ghép là người khác
        ("bạn cùng phòng gửi cho 300k", "add_expense"),
        ("nhận được tiền hoàn 200k", "add_expense"),
    ])
    def test_money_coming_in(self, text, wrong):
        assert parse_chat(text, rasa(wrong), now=NOW).intent == "add_income"


class TestContextWordsDoNotFlipAConfidentModel:
    """Nguyên tắc 5 và các ngoại lệ: bằng chứng chủ đề/yếu không được lật model."""

    @pytest.mark.parametrize("text,intent", [
        ("mua cam của người bán rau 50k", "add_expense"),  # "người bán" là bên kia, không phải người dùng bán
        ("em gửi mẹ 500k", "transfer_money"),  # "em/anh" có thể là chính người dùng
        ("trả lương nhân viên 5 triệu", "add_expense"),  # "lương" đi cả hai chiều
        # Chỉ nói tiền đi ra: đủ để bác bỏ "tiền vào" nhưng không đủ để chọn giữa chi tiêu và chuyển tiền. Dùng câu không dính danh mục nào:
        # "cho mẹ/anh/em" bị từ điển xếp vào Gia đình, tức là có thêm bằng chứng chi tiêu từ danh mục (nguyên tắc 5), không còn là phép thử riêng.
        ("cho bạn 200k", "transfer_money"),
    ])
    def test_model_keeps_its_answer(self, text, intent):
        assert parse_chat(text, rasa(intent), now=NOW).intent == intent

    def test_without_diacritics_lexical_evidence_defers_to_the_model(self):
        # "ban" không dấu có thể là bạn/bán/ban: luật không thấy động từ nên không được dùng tên món để lật lời của model
        assert parse_chat("ban laptop cu lay 7 trieu", rasa("add_income"), now=NOW).intent == "add_income"

    def test_with_diacritics_a_purchase_noun_still_beats_an_unsupported_income_claim(self):
        assert parse_chat("cắt kính cận 800k", rasa("add_income"), now=NOW).intent == "add_expense"
