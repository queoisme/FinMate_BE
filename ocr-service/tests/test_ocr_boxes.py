import io

from PIL import Image

import ocr


class _FakeEngine:
    def ocr(self, bgr, cls=True):
        return [[
            ([[10, 20], [110, 22], [108, 40], [12, 38]], ("Sua tuoi", 0.9)),
            ([[300, 21], [400, 20], [401, 41], [299, 40]], ("25.000", 0.8)),
        ]]


def _png() -> bytes:
    buf = io.BytesIO()
    Image.new("RGB", (900, 300), "white").save(buf, "PNG")
    return buf.getvalue()


def _fake(monkeypatch):
    monkeypatch.setattr(ocr, "_engine", lambda: _FakeEngine())
    monkeypatch.setattr(ocr, "_use_vietocr", lambda: False)
    monkeypatch.setattr(ocr, "crop_receipt", lambda a: a)


def test_read_boxes_returns_bounding_rectangle(monkeypatch):
    _fake(monkeypatch)
    first = ocr.read_boxes(_png())[0]
    assert (first.text, first.conf) == ("Sua tuoi", 0.9)
    assert (first.x0, first.y0, first.x1, first.y1) == (10, 20, 110, 40)


def test_read_lines_equals_boxes_text_and_confidence(monkeypatch):
    _fake(monkeypatch)
    data = _png()
    assert ocr.read_lines(data) == [(b.text, b.conf) for b in ocr.read_boxes(data)]
