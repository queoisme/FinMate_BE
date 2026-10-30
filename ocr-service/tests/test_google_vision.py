"""OCR bằng Google Cloud Vision (DOCUMENT_TEXT_DETECTION) làm đường chính, OCR cục bộ (PaddleOCR + VietOCR) làm dự phòng.
Không gọi mạng thật: _post được thay bằng bản giả. Khóa chỉ nằm trong header, không bao giờ vào URL hay log."""
import io
import json
import logging
import pathlib
import sys

import pytest
from PIL import Image

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))

from reading import google_vision, ocr  # noqa: E402


def _png() -> bytes:
    buf = io.BytesIO()
    Image.new("RGB", (900, 300), "white").save(buf, "PNG")
    return buf.getvalue()


def _word(text, x0, y0, x1, y1, conf=0.9, brk="SPACE"):
    symbols = [{"text": c} for c in text]
    symbols[-1]["property"] = {"detectedBreak": {"type": brk}}
    return {"boundingBox": {"vertices": [{"x": x0, "y": y0}, {"x": x1, "y": y0}, {"x": x1, "y": y1}, {"x": x0, "y": y1}]},
            "confidence": conf, "symbols": symbols}


def _response(*words):
    return {"responses": [{"fullTextAnnotation": {"pages": [{"blocks": [{"paragraphs": [{"words": list(words)}]}]}]}}]}


# "Sữa tươi 1L" rồi "32.000" ở hai dòng; chữ có dấu nguyên vẹn
TWO_LINES = _response(
    _word("Sữa", 10, 20, 60, 40, 0.98), _word("tươi", 70, 20, 120, 40, 0.96), _word("1L", 130, 20, 160, 40, 0.90, "EOL_SURE_SPACE"),
    _word("32.000", 300, 62, 380, 82, 0.99, "LINE_BREAK"),
)


@pytest.fixture(autouse=True)
def clean_state(monkeypatch):
    monkeypatch.setattr(google_vision._COOLDOWN, "until", 0.0)
    monkeypatch.setattr(google_vision._KEY, "fetched", float("-inf"))
    monkeypatch.delenv("AI_SERVICE_URL", raising=False)
    monkeypatch.delenv("AI_SERVICE_API_KEY", raising=False)
    monkeypatch.delenv("FINMATE_OCR_ENGINE", raising=False)
    monkeypatch.setenv("GOOGLE_VISION_API_KEY", "khoa-thu-nghiem")


def test_words_are_joined_into_lines_at_each_line_break_with_their_boxes_and_confidence():
    lines = google_vision.parse(TWO_LINES)
    assert [(t, round(c, 3)) for t, c, _ in lines] == [("Sữa tươi 1L", 0.947), ("32.000", 0.99)]
    assert lines[0][2] == [(10, 20), (160, 20), (160, 40), (10, 40)]  # góc của từ đầu và từ cuối dòng


# Một hàng bảng Google gộp thành MỘT dòng: số lượng + tên món (chữ sát nhau) rồi khoảng trống rộng, rồi số tiền (cột khác)
ONE_ROW = _response(
    _word("3", 10, 20, 24, 40), _word("GÓI", 34, 20, 80, 40), _word("Cut", 90, 20, 130, 40),  # không có EOL: Google không ngắt dòng ở cột
)
ONE_ROW["responses"][0]["fullTextAnnotation"]["pages"][0]["blocks"][0]["paragraphs"][0]["words"] += [
    _word("234,000", 520, 20, 640, 40, 0.99, "LINE_BREAK")]


def test_a_row_google_merged_into_one_line_is_cut_into_cells_at_the_wide_column_gaps():
    # Bộ tách món viết theo ô của PaddleOCR (mỗi ô một hộp); dòng gộp cả hàng khiến "Cut 234,000" bị đọc như một tên món
    assert [t for t, _c, _q in google_vision.parse(ONE_ROW)] == ["3 GÓI Cut", "234,000"]


def test_words_close_together_stay_in_one_cell():
    close = _response(_word("Sữa", 10, 20, 60, 40), _word("tươi", 70, 20, 120, 40), _word("1L", 130, 20, 160, 40, 0.90, "LINE_BREAK"))
    assert [t for t, _c, _q in google_vision.parse(close)] == ["Sữa tươi 1L"]


def test_a_response_without_text_or_with_an_api_error_has_no_lines():
    assert google_vision.parse({"responses": [{}]}) == []
    assert google_vision.parse({"responses": [{"error": {"code": 8, "message": "quota"}}]}) == []


def test_the_request_asks_for_document_text_in_vietnamese_and_keeps_the_key_out_of_the_url():
    req = google_vision.build_request(b"anh")
    assert "key=" not in req.full_url and "khoa-thu-nghiem" not in req.full_url
    assert req.get_header("X-goog-api-key") == "khoa-thu-nghiem"
    body = json.loads(req.data)["requests"][0]
    assert body["features"] == [{"type": "DOCUMENT_TEXT_DETECTION"}] and body["imageContext"]["languageHints"] == ["vi"]


def test_google_is_the_main_reader_and_the_local_ocr_is_not_touched(monkeypatch):
    monkeypatch.setattr(google_vision, "_post", lambda req: TWO_LINES)
    monkeypatch.setattr(ocr, "_run", lambda bgr: pytest.fail("OCR cục bộ không được chạy khi Google đã trả kết quả"))
    boxes = ocr.read_boxes(_png())
    assert [b.text for b in boxes] == ["Sữa tươi 1L", "32.000"]
    assert (boxes[0].x0, boxes[0].y0, boxes[0].x1, boxes[0].y1) == (10, 20, 160, 40)


def test_a_google_failure_falls_back_to_the_local_ocr_and_pauses_google_for_a_while(monkeypatch):
    calls = []

    def failing(req):
        calls.append(1)
        raise OSError("mạng đứt")

    monkeypatch.setattr(google_vision, "_post", failing)
    monkeypatch.setattr(ocr, "_run", lambda bgr: [ocr.OcrBox("Local", 0.9, 0, 0, 10, 10)])
    monkeypatch.setattr(ocr, "_use_vietocr", lambda: False)
    monkeypatch.setattr(ocr, "crop_receipt", lambda a: a)
    assert [b.text for b in ocr.read_boxes(_png())] == ["Local"]
    assert [b.text for b in ocr.read_boxes(_png())] == ["Local"]
    assert len(calls) == 1  # lần hai nằm trong thời gian tạm nghỉ: không chờ Google lỗi lần nữa


def test_without_a_key_or_with_the_engine_forced_local_google_is_never_called(monkeypatch):
    monkeypatch.setattr(google_vision, "_post", lambda req: pytest.fail("không được gọi Google"))
    monkeypatch.setattr(ocr, "_run", lambda bgr: [ocr.OcrBox("Local", 0.9, 0, 0, 10, 10)])
    monkeypatch.setattr(ocr, "_use_vietocr", lambda: False)
    monkeypatch.setattr(ocr, "crop_receipt", lambda a: a)
    monkeypatch.delenv("GOOGLE_VISION_API_KEY")
    assert [b.text for b in ocr.read_boxes(_png())] == ["Local"]
    monkeypatch.setenv("GOOGLE_VISION_API_KEY", "khoa-thu-nghiem")
    monkeypatch.setenv("FINMATE_OCR_ENGINE", "local")
    assert [b.text for b in ocr.read_boxes(_png())] == ["Local"]


def test_a_failure_is_logged_without_the_key_or_any_receipt_text(monkeypatch, caplog):
    monkeypatch.setattr(google_vision, "_post", lambda req: (_ for _ in ()).throw(OSError("lỗi mạng")))
    with caplog.at_level(logging.WARNING):
        assert google_vision.read_lines_or_none(_png()) is None
    dump = " ".join(r.getMessage() for r in caplog.records)
    assert "OSError" in dump and "khoa-thu-nghiem" not in dump


def test_the_admin_key_wins_over_the_environment_and_ai_service_down_falls_back_to_it(monkeypatch):
    monkeypatch.setenv("AI_SERVICE_URL", "http://ai-service:8000")
    monkeypatch.setenv("AI_SERVICE_API_KEY", "internal")

    class Fake:
        answer = "khoa-tu-trang-quan-tri"

        def shared_secret(self, name, consumer):
            assert (name, consumer) == ("google_vision_api_key", "ocr")
            if self.answer is None:
                raise google_vision.ai_api.AiApiError(0, "unreachable", "x")
            return self.answer

    fake = Fake()
    monkeypatch.setattr(google_vision._KEY, "api", fake)
    assert google_vision.api_key() == "khoa-tu-trang-quan-tri"  # kho key thắng biến môi trường
    assert google_vision.build_request(b"anh").get_header("X-goog-api-key") == "khoa-tu-trang-quan-tri"

    fake.answer = None  # AI Service ngừng: không tắt OCR đám mây
    monkeypatch.setattr(google_vision._KEY, "fetched", float("-inf"))
    assert google_vision.api_key() == "khoa-thu-nghiem"

    monkeypatch.setattr(google_vision._KEY, "fetched", float("-inf"))
    monkeypatch.delenv("GOOGLE_VISION_API_KEY")
    assert google_vision.api_key() is None and not google_vision.enabled()  # không có ở đâu cả thì tắt
