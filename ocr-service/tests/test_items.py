import sys, pathlib
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))

from ocr import OcrBox
from items import extract_items


def box(text, x0, y0, x1, y1):
    return OcrBox(text, 0.99, x0, y0, x1, y1)


def row(y, *cells):
    """Một hàng cao 30px tại y; cells là (text, x0, x1)."""
    return [box(t, x0, y, x1, y + 30) for t, x0, x1 in cells]


# Bill Ngô Gia BH00037, giấy rộng ~1000px
NGO_GIA = [
    *row(20, ("Phiếu thanh toán", 300, 700)),
    *row(70, ("SphiéuBH00037", 50, 400)),
    *row(120, ("Ngày tạo", 50, 250)),
    *row(170, ("Tại cửa hàng", 50, 300)),
    *row(220, ("T.Ngân: Nhân Viên 1", 50, 450)),
    *row(280, ("1S", 480, 520), ("Tong", 800, 900)),
    *row(330, ("Tra BlDao Ng Gia-Vra", 50, 450)),
    *row(370, ("700cc)", 50, 180), ("19,000", 800, 950)),
    *row(410, ("1", 490, 510)),
    *row(450, ("+ Thach Suong Sao", 70, 420), ("3,000", 830, 950)),
    *row(490, ("+ Thach Dura Nguyen V-", 70, 480), ("3,000", 830, 950)),
    *row(540, ("Khách VIP 3K", 50, 300), ("-3,000d", 800, 950)),
    *row(600, ("T6ng SL món", 50, 300), ("1", 900, 920)),
    *row(640, ("Thanh tien", 50, 250), ("25,000d", 780, 950)),
    *row(680, ("Tong thanh toan", 50, 350), ("22,000d", 780, 950)),
]


def test_ngo_gia_bill_pairs_names_with_prices_and_balances_total():
    r = extract_items(NGO_GIA, 22000)
    assert r.ok is True
    assert [(i.name, i.amount, i.parent, i.is_discount) for i in r.items] == [
        ("Tra BlDao Ng Gia-Vra 700cc)", 19000, None, False),
        ("Thach Suong Sao", 3000, 0, False),
        ("Thach Dura Nguyen V-", 3000, 0, False),
        ("Khách VIP 3K", -3000, None, True),
    ]


HEADER = row(100, ("Tên hàng", 50, 300), ("SL", 500, 540), ("Thành tiền", 780, 950))
FOOTER = row(900, ("Tổng cộng", 50, 300), ("50,000", 800, 950))


def test_wrapped_name_joins_into_priced_row():
    boxes = HEADER + row(200, ("Bánh mì", 50, 250)) + row(240, ("thịt nướng", 50, 300), ("30,000", 800, 950)) \
        + row(300, ("Nước cam", 50, 300), ("20,000", 800, 950)) + FOOTER
    r = extract_items(boxes, 50000)
    assert [i.name for i in r.items] == ["Bánh mì thịt nướng", "Nước cam"]
    assert r.ok


def test_quantity_column_is_not_a_name():
    boxes = HEADER + row(200, ("Bánh mì", 50, 250), ("2", 500, 520), ("30,000", 800, 950)) \
        + row(240, ("1", 500, 520)) + row(300, ("Nước cam", 50, 300), ("20,000", 800, 950)) + FOOTER
    r = extract_items(boxes, 50000)
    assert [i.name for i in r.items] == ["Bánh mì", "Nước cam"]
    assert r.ok


def test_unit_price_times_quantity_uses_rightmost_amount():
    boxes = HEADER + row(200, ("Bánh mì", 50, 250), ("2", 450, 470), ("15,000", 660, 760), ("30,000", 800, 950)) \
        + row(300, ("Nước cam", 50, 300), ("20,000", 800, 950)) + FOOTER
    r = extract_items(boxes, 50000)
    assert [(i.name, i.amount) for i in r.items] == [("Bánh mì", 30000), ("Nước cam", 20000)]
    assert r.ok


def test_sum_mismatch_sets_ok_false():
    assert extract_items(NGO_GIA, 25000).ok is False
    assert extract_items(NGO_GIA, 22001).ok is False


def test_fewer_than_two_items_sets_ok_false():
    boxes = HEADER + row(200, ("Bánh mì", 50, 250), ("50,000", 800, 950)) + FOOTER
    r = extract_items(boxes, 50000)
    assert len(r.items) == 1 and r.ok is False
    # một món cộng một giảm giá vẫn chỉ là một món
    boxes = HEADER + row(200, ("Bánh mì", 50, 250), ("60,000", 800, 950)) \
        + row(240, ("Giảm giá", 50, 250), ("10,000", 800, 950)) + FOOTER
    r = extract_items(boxes, 50000)
    assert [i.amount for i in r.items] == [60000, -10000]
    assert r.ok is False
    assert extract_items([], 50000).ok is False


def test_total_none_sets_ok_false():
    r = extract_items(NGO_GIA, None)
    assert len(r.items) == 4 and r.ok is False


def test_rows_tolerate_slight_tilt():
    # lệch 15px, dưới 0,6 x 30px: cùng hàng
    boxes = HEADER + [box("Bánh mì", 50, 200, 250, 230), box("30,000", 800, 215, 950, 245)] \
        + row(300, ("Nước cam", 50, 300), ("20,000", 800, 950)) + FOOTER
    r = extract_items(boxes, 50000)
    assert [(i.name, i.amount) for i in r.items] == [("Bánh mì", 30000), ("Nước cam", 20000)]
    assert r.ok
