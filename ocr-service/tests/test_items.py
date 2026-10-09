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


def test_unpriced_plus_modifier_does_not_turn_next_item_into_addon():
    boxes = HEADER + row(200, ("Trà sữa", 50, 250), ("30,000", 800, 950)) \
        + row(240, ("+ Ít đá", 70, 250), ("0", 900, 920)) \
        + row(280, ("Trà đào", 50, 250), ("20,000", 800, 950)) + FOOTER
    r = extract_items(boxes, 50000)
    assert [(i.name, i.amount, i.parent, i.is_discount) for i in r.items] == [
        ("Trà sữa", 30000, None, False),
        ("Trà đào", 20000, None, False),
    ]
    assert r.ok is True


TWO_ITEMS = row(200, ("Bánh mì", 50, 250), ("30,000", 800, 950)) + row(300, ("Nước cam", 50, 300), ("20,000", 800, 950))


def test_codes_points_phone_above_header_are_not_prices():
    # điểm tích lũy, mã Nợ/Có, số điện thoại ở mép phải phía trên tiêu đề không làm hỏng cả bill
    top = row(10, ("KH: Khiet", 50, 300), ("2,770ĐIỂM", 800, 950)) + row(40, ("Nợ: 1311", 800, 950)) \
        + row(70, ("Điện thoại: 0251.3831289", 600, 950))
    r = extract_items(top + HEADER + TWO_ITEMS + FOOTER, 50000)
    assert [(i.name, i.amount) for i in r.items] == [("Bánh mì", 30000), ("Nước cam", 20000)]
    assert r.ok


def test_total_line_above_header_is_not_first_item():
    # "Tổng tiền các đơn 50,000" in ở phần đầu phiếu, trước bảng món
    top = row(40, ("Ghi chú", 50, 200), ("Tổng tiền các đơn", 400, 700), ("50,000", 800, 950))
    r = extract_items(top + HEADER + TWO_ITEMS + FOOTER, 50000)
    assert [i.amount for i in r.items] == [30000, 20000] and r.ok


def test_trailing_tax_rate_cell_is_skipped_for_price():
    # cột thuế suất "8%" nằm ngoài cùng bên phải: giá là ô tiền gần nhất bên trái nó
    phone = row(10, ("Điện thoại: 0251.3831289", 600, 990))  # số ở mép phải trên tiêu đề, giá món thì không ở mép
    boxes = phone + HEADER + row(200, ("Bánh mì", 50, 250), ("30,000", 760, 900), ("8%", 940, 990)) \
        + row(300, ("Nước cam", 50, 300), ("20,000", 760, 900), ("8%", 940, 990)) + FOOTER
    r = extract_items(boxes, 50000)
    assert [(i.name, i.amount) for i in r.items] == [("Bánh mì", 30000), ("Nước cam", 20000)]
    assert r.ok


def test_subtotal_line_is_not_an_item():
    for label in ("Cộng tiền hàng", "C0ng tien hang", "Tạm tính", "Tiền hàng"):
        boxes = HEADER + TWO_ITEMS + row(400, (label, 50, 300), ("50,000", 800, 950)) + FOOTER
        r = extract_items(boxes, 50000)
        assert [i.amount for i in r.items] == [30000, 20000], label
        assert r.ok, label


VAT_TAIL = row(400, ("Tiền hàng", 50, 300), ("50,000", 800, 950)) \
    + row(440, ("Thuế GTGT (8%)", 50, 300), ("4,000", 800, 950)) \
    + row(480, ("Tổng thanh toán", 50, 300), ("54,000", 800, 950)) \
    + row(520, ("Còn phải thu", 50, 300), ("54,000", 800, 950))


def test_vat_after_subtotal_is_a_surcharge_item():
    r = extract_items(HEADER + TWO_ITEMS + VAT_TAIL, 54000)
    assert [(i.name, i.amount, i.parent, i.is_discount) for i in r.items] == [
        ("Bánh mì", 30000, None, False),
        ("Nước cam", 20000, None, False),
        ("Thuế GTGT (8%)", 4000, None, False),
    ]
    assert r.ok is True
    assert extract_items(HEADER + TWO_ITEMS + VAT_TAIL, 54001).ok is False  # vẫn đòi khớp đúng từng đồng


def test_service_fee_after_subtotal_is_a_surcharge_item():
    tail = row(400, ("Tạm tính", 50, 300), ("50,000", 800, 950)) \
        + row(440, ("Phí phục vụ 5%", 50, 300), ("2,500", 800, 950)) \
        + row(480, ("Thanh toán", 50, 300), ("52,500", 800, 950))
    r = extract_items(HEADER + TWO_ITEMS + tail, 52500)
    assert [(i.name, i.amount) for i in r.items][-1] == ("Phí phục vụ 5%", 2500)
    assert r.ok


def test_bill_discount_after_subtotal_and_cash_lines_ignored():
    tail = row(400, ("Tổng Tiền Hàng :", 50, 400), ("50,000 VND", 750, 950)) \
        + row(440, ("Giảm giá (0.5%) :", 50, 400), ("5,000 VND", 750, 950)) \
        + row(480, ("Tiền khách đưa :", 50, 400), ("45,000 VND", 750, 950)) \
        + row(520, ("Tổng thanh toán :", 50, 400), ("45,000 VND", 750, 950))
    r = extract_items(HEADER + TWO_ITEMS + tail, 45000)
    assert [(i.name, i.amount, i.parent, i.is_discount) for i in r.items] == [
        ("Bánh mì", 30000, None, False),
        ("Nước cam", 20000, None, False),
        ("Giảm giá (0.5%) :", -5000, None, True),
    ]
    assert r.ok is True


def test_pre_tax_total_after_subtotal_is_not_a_surcharge():
    tail = row(400, ("Tổng cộng", 50, 300), ("2", 900, 920)) \
        + row(440, ("Tổng tiền chưa VAT:", 50, 400), ("50,000", 800, 950)) \
        + row(480, ("Tiền VAT:", 50, 300)) \
        + row(520, ("Tổng phải trả:", 50, 300), ("50,000", 800, 950))
    r = extract_items(HEADER + TWO_ITEMS + tail, 50000)
    assert [i.amount for i in r.items] == [30000, 20000] and r.ok


def test_bare_number_equal_to_running_sum_is_subtotal():
    # "353.364" in một mình ngay trên chữ "Cộng": không phải món
    tail = row(400, ("50,000", 800, 950)) \
        + row(440, ("Cộng tiền hàng", 400, 700), ("50,000", 800, 950)) \
        + row(480, ("Tiền thuế GTGT:", 400, 700), ("4,000", 800, 950)) \
        + row(520, ("Tổng tiền thanh toán", 400, 700), ("54,000", 800, 950))
    r = extract_items(HEADER + TWO_ITEMS + tail, 54000)
    assert [i.amount for i in r.items] == [30000, 20000, 4000] and r.ok
    # số trần khác tổng đang cộng vẫn là món (giá in lệch hàng), không bị nuốt
    odd = row(400, ("7,000", 800, 950)) + FOOTER
    assert [i.amount for i in extract_items(HEADER + TWO_ITEMS + odd, 57000).items] == [30000, 20000, 7000]


def test_surcharge_does_not_count_as_an_item():
    one = row(200, ("Bánh mì", 50, 250), ("50,000", 800, 950))
    r = extract_items(HEADER + one + VAT_TAIL, 54000)
    assert [i.amount for i in r.items] == [50000, 4000]
    assert r.ok is False


def test_no_header_before_first_price_sets_ok_false():
    # "SL: 2" ở chân bill không được coi là tiêu đề cột
    boxes = row(200, ("Trà sữa", 50, 250), ("30,000", 800, 950)) \
        + row(240, ("Trà đào", 50, 250), ("20,000", 800, 950)) \
        + row(280, ("SL: 2", 50, 200)) \
        + row(320, ("Tiền mặt", 50, 250), ("30,000", 800, 950)) \
        + row(360, ("Chuyển khoản", 50, 300), ("20,000", 800, 950))
    assert extract_items(boxes, 50000).ok is False
