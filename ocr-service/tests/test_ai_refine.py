import json
import sys, pathlib

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))

import pytest

from ai_refine import AiFailed, AiUnavailable, parse_reply, pick_items, refine


def _gemini_reply(payload: dict | str, status: int = 200):
    text = payload if isinstance(payload, str) else json.dumps(payload, ensure_ascii=False)

    class R:
        status_code = status

        def json(self):
            return {"candidates": [{"content": {"parts": [{"text": text}]}}]}

    return R()


def test_pick_items_keeps_product_lines_and_drops_totals_dates_and_the_shop_name():
    lines = ["HIGHLANDS COFFEE", "HOA DON BAN LE", "Ngay 04/10/2026 09:46", "Ca phe sua da 2 x 30.000", "Banh mi thit nuong", "5,000 x 15 75,000",
             "TONG CONG 90.000", "Tien khach tra 100.000", "Cam on quy khach", "www.highlands.com.vn", "SP002993"]
    assert pick_items(lines, "Highlands Coffee") == ["Ca phe sua da 2 x 30.000", "Banh mi thit nuong"]


def test_pick_items_is_capped():
    assert len(pick_items([f"San pham so {i} abc" for i in range(40)], None)) <= 12


def test_parse_reply_validates_category_and_cleans_text():
    r = parse_reply('{"category": "dining", "merchant": "  Quán  Bụi \\n", "items": ["Cà phê sữa đá", "", 5]}')
    assert r == {"category": "dining", "merchant": "Quán Bụi", "items": ["Cà phê sữa đá"]}


def test_parse_reply_rejects_a_category_outside_the_8_names():
    with pytest.raises(AiFailed):
        parse_reply('{"category": "an uong", "merchant": "A", "items": []}')
    with pytest.raises(AiFailed):
        parse_reply("không phải json")


def test_refine_needs_a_key(monkeypatch):
    monkeypatch.delenv("GEMINI_API_KEY", raising=False)
    with pytest.raises(AiUnavailable):
        refine("A", ["x"], 1000, post=lambda *a, **k: _gemini_reply({}))


def test_refine_sends_only_text_fields_and_forces_an_enum_schema(monkeypatch):
    monkeypatch.setenv("GEMINI_API_KEY", "test-key")
    seen = {}

    def fake_post(url, headers, json, timeout):
        seen.update(url=url, headers=headers, body=json)
        return _gemini_reply({"category": "dining", "merchant": "Highlands Coffee", "items": ["Cà phê sữa đá"]})

    out = refine("HIGHLANDS COFFE", ["Ca phe sua da"], 90000, post=fake_post)
    assert out == {"category": "dining", "merchant": "Highlands Coffee", "items": ["Cà phê sữa đá"]}
    assert seen["headers"]["x-goog-api-key"] == "test-key"
    sent = json.dumps(seen["body"], ensure_ascii=False)
    assert "HIGHLANDS COFFE" in sent and "90000" in sent and "inline_data" not in sent and "inlineData" not in sent  # không gửi ảnh
    enum = seen["body"]["generationConfig"]["responseSchema"]["properties"]["category"]["enum"]
    assert sorted(enum) == sorted(["dining", "transport", "shopping", "groceries", "housing", "utilities", "healthcare", "entertainment", "other"])


def test_refine_reports_http_errors(monkeypatch):
    import ai_refine
    monkeypatch.setattr(ai_refine, "_RETRY_WAIT", 0)
    monkeypatch.setenv("GEMINI_API_KEY", "k")
    with pytest.raises(AiFailed):
        refine("A", ["x"], 1, post=lambda *a, **k: _gemini_reply({}, status=400))


def test_refine_retries_once_when_the_service_is_overloaded(monkeypatch):
    import ai_refine
    monkeypatch.setattr(ai_refine, "_RETRY_WAIT", 0)
    monkeypatch.setenv("GEMINI_API_KEY", "k")
    replies = [_gemini_reply({}, status=503), _gemini_reply({"category": "other", "merchant": "A", "items": []})]
    calls = []
    out = refine("A", ["x"], 1, post=lambda *a, **k: calls.append(1) or replies.pop(0))
    assert out["category"] == "other" and len(calls) == 2
    with pytest.raises(AiFailed):  # quá tải hai lần liên tiếp thì thôi, dùng kết quả cục bộ
        refine("A", ["x"], 1, post=lambda *a, **k: _gemini_reply({}, status=503))


def test_openai_compatible_provider_uses_env_and_validates_the_category(monkeypatch):
    monkeypatch.setenv("FINMATE_AI_PROVIDER", "openai")
    for k in ("FINMATE_AI_BASE_URL", "FINMATE_AI_API_KEY", "FINMATE_AI_MODEL"):
        monkeypatch.delenv(k, raising=False)
    with pytest.raises(AiUnavailable):  # thiếu cấu hình thì không gọi
        refine("A", ["x"], 1, post=lambda *a, **k: None)
    monkeypatch.setenv("FINMATE_AI_BASE_URL", "https://router.example/v1/")
    monkeypatch.setenv("FINMATE_AI_API_KEY", "test-key")
    monkeypatch.setenv("FINMATE_AI_MODEL", "some-model")
    seen = {}

    def fake_post(url, headers, json, timeout):
        seen.update(url=url, headers=headers, body=json)

        class R:
            status_code = 200

            def json(self):
                return {"choices": [{"message": {"content": '{"category": "groceries", "merchant": "Bách Hóa Xanh", "items": ["Sữa tươi"]}'}}]}

        return R()

    out = refine("BACH HOA XANH", ["Sua tuoi"], 25000, post=fake_post)
    assert out["category"] == "groceries" and out["merchant"] == "Bách Hóa Xanh"
    assert seen["url"] == "https://router.example/v1/chat/completions" and seen["headers"]["Authorization"] == "Bearer test-key"
    assert "BACH HOA XANH" in str(seen["body"]) and "image" not in str(seen["body"]).lower()

    def bad_post(*a, **k):
        class R:
            status_code = 200

            def json(self):
                return {"choices": [{"message": {"content": '{"category": "an uong", "merchant": "A", "items": []}'}}]}

        return R()

    with pytest.raises(AiFailed):  # tên danh mục lạ bị chặn dù nhà cung cấp không có schema
        refine("A", ["x"], 1, post=bad_post)
