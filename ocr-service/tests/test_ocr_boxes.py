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


def test_read_boxes_keeps_x_extent_and_deskews_y(monkeypatch):
    _fake(monkeypatch)
    first = ocr.read_boxes(_png())[0]
    assert (first.text, first.conf) == ("Sua tuoi", 0.9)
    # chỉ hai quad rộng (< 3): không đủ tin độ dốc, giữ hộp bao thẳng trục như cũ
    assert (first.x0, first.y0, first.x1, first.y1) == (10, 20, 110, 40)


def _quad(x0, x1, top, slope, h=30):
    """Quad PaddleOCR (trên-trái, trên-phải, dưới-phải, dưới-trái) của chữ in thẳng trên trang nghiêng."""
    return [[x0, top + slope * x0], [x1, top + slope * x1], [x1, top + slope * x1 + h], [x0, top + slope * x0 + h]]


def _bbox(q):
    xs, ys = [p[0] for p in q], [p[1] for p in q]
    return min(xs), min(ys), max(xs), max(ys)


def test_flat_page_is_unchanged_by_deskew():
    quads = [_quad(50, 400, 100 + 40 * i, 0.0) for i in range(5)] + [_quad(800, 950, 100 + 40 * i, 0.0) for i in range(5)]
    assert ocr._deskew(quads) == [_bbox(q) for q in quads]


def test_deskew_needs_three_wide_quads():
    quads = [_quad(50, 400, 100, 0.05), _quad(800, 950, 100, 0.05), _quad(50, 100, 140, 0.05)]  # quad thứ ba hẹp
    assert ocr._deskew(quads) == [_bbox(q) for q in quads]


def test_deskew_ignores_a_wild_slope():
    quads = [_quad(50, 400, 100, 0.9), _quad(50, 400, 600, 0.9), _quad(800, 950, 100, 0.01)]  # trung vị 0,9 > 0,2
    assert ocr._deskew(quads) == [_bbox(q) for q in quads]


def test_deskew_fixes_pairing_on_tilted_page():
    from items import extract_items

    slope = 0.05  # 50px trên 1000px = 1,25 hàng (8904 trôi 1,56 hàng): tâm giá gần tên hàng dưới hơn tên cùng hàng
    names = ["Bánh mì", "Nước cam", "Trà đào", "Cà phê", "Sữa chua"]
    prices = [30000, 20000, 25000, 15000, 10000]
    rows = [("Tên hàng", 50, 400, 60), ("Thành tiền", 800, 950, 60)]
    for i, (n, p) in enumerate(zip(names, prices)):
        top = 100 + 40 * i
        rows += [(n, 50, 400, top), (f"{p:,}", 800, 950, top)]
    rows += [("Tổng cộng", 50, 400, 320), (f"{sum(prices):,}", 800, 950, 320)]
    quads = [_quad(x0, x1, top, slope) for _, x0, x1, top in rows]

    def boxes(coords):
        return [ocr.OcrBox(t, 0.99, *c) for (t, *_), c in zip(rows, coords)]

    tilted = extract_items(boxes([_bbox(q) for q in quads]), sum(prices))
    assert [(i.name, i.amount) for i in tilted.items] != list(zip(names, prices))  # hộp thẳng trục: ghép lệch hàng
    fixed = extract_items(boxes(ocr._deskew(quads)), sum(prices))
    assert [(i.name, i.amount) for i in fixed.items] == list(zip(names, prices))
    assert fixed.ok is True


def test_read_lines_equals_boxes_text_and_confidence(monkeypatch):
    _fake(monkeypatch)
    data = _png()
    assert ocr.read_lines(data) == [(b.text, b.conf) for b in ocr.read_boxes(data)]
