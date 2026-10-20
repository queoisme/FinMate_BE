import sys, pathlib
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))

from reading.ocr import OcrBox
from parsing.items import extract_items


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


def test_names_one_row_above_prices_still_balance_sum_check_cannot_catch_it():
    # Ghi nhận giới hạn: giá lệch xuống một hàng so với tên (ảnh nghiêng) vẫn cộng đúng tổng, nên ok=True với tên sai.
    # Kiểm tra tổng không bắt được lỗi này; chặn nó ở ocr._deskew (chỉnh nghiêng trước khi gom hàng).
    boxes = HEADER + row(200, ("Creamer", 50, 300)) + row(240, ("Sữa hạt", 50, 300), ("30,000", 800, 950)) \
        + row(280, ("Loc 4x", 50, 300), ("20,000", 800, 950)) + row(320, ("5,000", 800, 950)) \
        + row(400, ("Tổng cộng", 50, 300), ("55,000", 800, 950))
    r = extract_items(boxes, 55000)
    assert r.ok is True
    assert [(i.name, i.amount) for i in r.items] == [("Creamer Sữa hạt", 30000), ("Loc 4x", 20000), ("", 5000)]


def test_no_header_before_first_price_sets_ok_false():
    # "SL: 2" ở chân bill không được coi là tiêu đề cột
    boxes = row(200, ("Trà sữa", 50, 250), ("30,000", 800, 950)) \
        + row(240, ("Trà đào", 50, 250), ("20,000", 800, 950)) \
        + row(280, ("SL: 2", 50, 200)) \
        + row(320, ("Tiền mặt", 50, 250), ("30,000", 800, 950)) \
        + row(360, ("Chuyển khoản", 50, 300), ("20,000", 800, 950))
    assert extract_items(boxes, 50000).ok is False


# Bill cửa hàng tiện lợi không in tiêu đề cột: tên | SL | đơn giá | thành tiền (dấu * dính sau), cộng tạm rồi khuyến mãi
NO_HEADER_QTY_UNIT = [
    *row(20, ("CÔNG TY TNHH CỬA HÀNG MẪU", 300, 900)),
    *row(70, ("12 Đường Mẫu, TP.HCM", 300, 900)),
    *row(120, ("Hoa don Ban hang", 500, 750)),
    *row(170, ("11/10/2026", 300, 450), ("12:31:13", 860, 990)),
    *row(220, ("Khan uot MAU 20", 310, 570), ("1", 650, 670), ("19,000", 700, 800), ("19,000", 880, 985)),
    *row(260, ("TraXanhMatOng450", 310, 560), ("1", 650, 670), ("14,000", 700, 800), ("14,000*", 880, 1000)),
    *row(300, ("Nuoc Viet Quat 45", 310, 570), ("1", 650, 670), ("14,000", 700, 800), ("14,000*", 880, 1000)),
    *row(340, ("TraChanhMatOng", 310, 575), ("1", 650, 670), ("14,000", 700, 800), ("14,000*", 880, 1000)),
    *row(380, ("Tam tinh:", 300, 440), ("61,000", 880, 990)),
    *row(420, ("Khuyen mai:", 300, 460), ("-14,000", 880, 990)),
    *row(460, ("Tong cong (gom thue):", 300, 600), ("47,000", 880, 1000)),
    *row(500, ("Tien mat", 300, 420), ("50,000", 880, 1000)),
    *row(540, ("(Tien mat)Tien thua:", 300, 580), ("-3,000", 880, 1000)),
]


def test_no_header_qty_unit_total_columns_with_subtotal_and_promotion():
    r = extract_items(NO_HEADER_QTY_UNIT, 47000)
    assert r.ok is True
    assert [(i.name, i.amount, i.is_discount) for i in r.items] == [
        ("Khan uot MAU 20", 19000, False), ("TraXanhMatOng450", 14000, False),
        ("Nuoc Viet Quat 45", 14000, False), ("TraChanhMatOng", 14000, False),
        ("Khuyen mai:", -14000, True)]


def test_no_header_wrong_total_sets_ok_false():
    assert extract_items(NO_HEADER_QTY_UNIT, 61000).ok is False


def test_no_header_without_subtotal_or_total_label_sets_ok_false():
    # không tiêu đề thì bảng món phải khép bằng dòng cộng/tổng; tổng trùng hợp không đủ
    boxes = row(200, ("Trà sữa", 50, 250), ("30,000", 800, 950)) + row(240, ("Trà đào", 50, 250), ("20,000", 800, 950))
    assert extract_items(boxes, 50000).ok is False


def test_headerless_payment_lines_are_not_items():
    # tên món không có giá đọc được; hai dòng trả tiền cộng đúng tổng không được thành danh sách món
    boxes = row(200, ("Trà sữa", 50, 250)) + row(240, ("Trà đào", 50, 250)) \
        + row(280, ("Tiền mặt", 50, 250), ("30,000", 800, 950)) \
        + row(320, ("Chuyển khoản", 50, 300), ("20,000", 800, 950)) \
        + row(360, ("Tổng thanh toán", 50, 300), ("50,000", 800, 950))
    assert extract_items(boxes, 50000).ok is False


def test_headerless_payment_lines_in_right_column_with_prices_in_middle():
    boxes = row(200, ("Trà sữa", 50, 250), ("30,000", 450, 550)) + row(240, ("Trà đào", 50, 250), ("20,000", 450, 550)) \
        + row(280, ("Tiền mặt", 50, 250), ("30,000", 800, 950)) \
        + row(320, ("Chuyển khoản", 50, 300), ("20,000", 800, 950)) \
        + row(360, ("Tổng thanh toán", 50, 300), ("50,000", 800, 950))
    assert extract_items(boxes, 50000).ok is False


def test_priced_total_row_is_not_a_column_header():
    boxes = row(200, ("Trà sữa", 50, 250), ("30000", 800, 950)) + row(240, ("Trà đào", 50, 250), ("20000", 800, 950)) \
        + row(280, ("Tổng cộng", 50, 250), ("50,000", 800, 950)) \
        + row(320, ("Tiền mặt", 50, 250), ("30,000", 800, 950)) \
        + row(360, ("Chuyển khoản", 50, 300), ("20,000", 800, 950)) \
        + row(400, ("Đã thanh toán", 50, 300), ("50,000", 800, 950))
    r = extract_items(boxes, 50000)
    assert not (r.ok and any("Tiền mặt" in i.name or "Chuyển khoản" in i.name for i in r.items))


def test_card_payment_after_bare_total_header_is_not_an_item():
    boxes = row(160, ("Tổng", 800, 900)) + row(200, ("Trà sữa", 50, 250)) \
        + row(240, ("Thẻ", 50, 150), ("20,000", 800, 950)) + row(280, ("Tiền mặt", 50, 250), ("30,000", 800, 950)) \
        + row(320, ("Tổng cộng", 50, 250), ("50,000", 800, 950))
    assert extract_items(boxes, 50000).ok is False


def test_headerless_sl_footer_then_payments_closed_by_total_sets_ok_false():
    boxes = row(200, ("Trà sữa", 50, 250), ("30,000", 800, 950)) \
        + row(240, ("Trà đào", 50, 250), ("20,000", 800, 950)) \
        + row(280, ("SL: 2", 50, 200)) \
        + row(320, ("Tiền mặt", 50, 250), ("30,000", 800, 950)) \
        + row(360, ("Chuyển khoản", 50, 300), ("20,000", 800, 950)) \
        + row(400, ("Tổng tiền", 50, 250), ("50,000", 800, 950))
    assert extract_items(boxes, 50000).ok is False


def test_fee_including_vat_before_subtotal_is_an_item_not_a_surcharge():
    boxes = HEADER + row(200, ("Cà phê", 50, 300), ("30,000", 800, 950)) \
        + row(240, ("Bánh", 50, 300), ("20,000", 800, 950)) + row(280, ("Tạm tính", 50, 300), ("50,000", 800, 950)) \
        + row(320, ("Gồm VAT 8%", 50, 300), ("4,000", 800, 950)) \
        + row(360, ("Tổng cộng", 50, 300), ("50,000", 800, 950))
    r = extract_items(boxes, 50000)
    assert r.ok is True and [i.name for i in r.items] == ["Cà phê", "Bánh"]


# ---- tổng khách phải trả đọc theo HÀNG: nhãn và số cùng hàng, không phụ thuộc thứ tự OCR đọc các dòng
from parsing.items import payable_total  # noqa: E402

BILL_ROWS = [
    *row(500, ("Tổng tiền hàng (5 món):", 50, 450), ("160.000", 800, 950)),
    *row(540, ("Thuế GTGT (8%):", 50, 400), ("12.800", 820, 950)),
    *row(600, ("Tổng thanh toán:", 50, 450), ("172.800", 780, 950)),
    *row(650, ("Tiền khách đưa:", 50, 400), ("200.000", 780, 950)),
    *row(690, ("Tiền thối lại:", 50, 400), ("27.200", 800, 950)),
    *row(740, ("Hình thức thanh toán: Tiền mặt", 50, 600)),
]


def test_payable_total_is_the_amount_on_the_same_row_as_the_payable_label():
    assert payable_total(BILL_ROWS) == 172800  # không phải thuế phía trên (12.800) hay tiền khách đưa phía dưới (200.000)


def test_payable_total_ignores_the_payment_method_row_and_cash_rows():
    only_cash = [*row(100, ("Tiền khách đưa:", 50, 400), ("200.000", 780, 950)), *row(140, ("Hình thức thanh toán: Tiền mặt", 50, 600))]
    assert payable_total(only_cash) is None


def test_payable_total_takes_the_last_matching_row():
    rows = [*row(100, ("Tổng thanh toán (tạm)", 50, 400), ("100.000", 780, 950)), *row(200, ("Phải trả:", 50, 400), ("90.000", 780, 950))]
    assert payable_total(rows) == 90000


def test_payable_total_accepts_the_amount_inside_the_label_box():
    assert payable_total(row(100, ("Tong thanh toan: 59.400", 50, 900))) == 59400


def test_payable_total_none_without_a_payable_label_or_without_boxes():
    assert payable_total([]) is None
    assert payable_total(row(100, ("Thành tiền", 50, 400), ("50.000", 780, 950))) is None  # nhãn khác: giữ luật cũ ở extract.py


# ---- bảng có cột STT (bill siêu thị/tiện lợi in sẵn): hàng mô tả không giá nằm DƯỚI món, không phải phần đầu tên của món sau
STT_TABLE = [
    *row(20, ("STT", 50, 100), ("Tên hàng", 150, 400), ("SL", 560, 600), ("Đơn giá", 650, 750), ("Thành tiền", 800, 950)),
    *row(70, ("1", 50, 70), ("Mì ly Hảo Hảo", 150, 400), ("1", 570, 590), ("10.000", 650, 750), ("10.000", 820, 950)),
    *row(105, ("hương vị tôm chua cay 67g", 150, 500)),
    *row(150, ("2", 50, 70), ("Trà xanh C2", 150, 400), ("1", 570, 590), ("15.000", 650, 750), ("15.000", 820, 950)),
    *row(185, ("chai 500ml", 150, 400)),
    *row(230, ("Tổng tiền hàng (2 món):", 50, 450), ("25.000", 800, 950)),
    *row(270, ("Thu€ GTGT (8%):", 50, 400), ("2.000", 820, 950)),
    *row(320, ("Tổng thanh toán:", 50, 450), ("27.000", 800, 950)),
]


def test_description_row_below_an_stt_item_is_not_glued_to_the_next_item_name():
    r = extract_items(STT_TABLE, 27000)
    assert [i.name for i in r.items if not i.is_discount][:2] == ["Mì ly Hảo Hảo", "Trà xanh C2"]


def test_vat_row_read_as_thu_euro_gtgt_is_still_a_surcharge_so_the_sum_balances():
    r = extract_items(STT_TABLE, 27000)
    assert r.ok is True
    assert [(i.amount, i.parent) for i in r.items] == [(10000, None), (15000, None), (2000, None)]


def test_gtgt_alone_marks_a_surcharge():
    rows = [*STT_TABLE[:-2], *row(270, ("Thuế GTGT (8%):", 50, 400), ("2.000", 820, 950)), *row(320, ("Tổng thanh toán:", 50, 450), ("27.000", 800, 950))]
    assert extract_items(rows, 27000).ok is True


# ---- bill "Tổng cộng" (Tạp hóa Đức Cường, ảnh thật 2026-10-14): nhãn không phải "Tổng thanh toán" nhưng vẫn là tổng cuối
TONG_CONG_ROWS = [
    *row(500, ("Tạm tính:", 500, 700), ("146.000", 800, 950)),
    *row(540, ("Thuế GTGT (8%):", 500, 750), ("11.680", 820, 950)),
    *row(590, ("Tổng cộng:", 500, 700), ("157.680", 800, 950)),
    *row(650, ("Tiền khách đưa:", 500, 750), ("200.000", 800, 950)),
    *row(690, ("Tiền thối lại:", 500, 750), ("42.320", 820, 950)),
]


def test_payable_total_falls_back_to_the_tong_cong_row():
    assert payable_total(TONG_CONG_ROWS) == 157680


def test_payable_label_beats_a_tong_cong_subtotal_row():
    rows = [*row(100, ("Tổng cộng", 50, 400), ("100.000", 780, 950)), *row(140, ("Thuế GTGT", 50, 400), ("8.000", 800, 950)),
            *row(180, ("Tổng thanh toán", 50, 400), ("108.000", 780, 950))]
    assert payable_total(rows) == 108000


def test_tong_cong_of_goods_or_tam_tinh_is_a_subtotal_not_the_total():
    assert payable_total(row(100, ("Tổng cộng tiền hàng", 50, 400), ("100.000", 780, 950))) is None
    assert payable_total(row(100, ("Tạm tính", 50, 400), ("100.000", 780, 950))) is None


def test_tong_cong_row_without_an_amount_on_the_row_is_ignored():
    assert payable_total(row(100, ("Tổng cộng:", 50, 400))) is None


def test_several_tong_cong_rows_are_block_subtotals_not_a_total():
    # hóa đơn A4 nhiều khối bảng, mỗi khối có "Tổng cộng" riêng (ảnh thật d2b8bddd): không chọn dòng nào làm tổng bill
    rows = [*row(100, ("Tổng cộng", 50, 400), ("78.589", 780, 950)), *row(300, ("Tổng cộng", 50, 400), ("982.364", 780, 950))]
    assert payable_total(rows) is None


# ---- hồi quy 2026-10-14 sau khi sửa \b: "Thẻ game nạp" (món) bị coi là dòng thanh toán bằng thẻ nên bảng món dừng ở món 2
THE_GAME_TABLE = [
    *row(20, ("STT", 50, 100), ("Tên hàng", 150, 400), ("SL", 560, 600), ("Đơn giá", 650, 750), ("Thành tiền", 800, 950)),
    *row(70, ("1", 50, 70), ("Vé vào cổng người lớn", 150, 450), ("2", 570, 590), ("80.000", 650, 750), ("160.000", 820, 950)),
    *row(120, ("2", 50, 70), ("Thẻ game nạp 100.000đ", 150, 450), ("1", 570, 590), ("100.000", 650, 750), ("100.000", 820, 950)),
    *row(170, ("3", 50, 70), ("Bắp rang mini", 150, 450), ("1", 570, 590), ("30.000", 650, 750), ("30.000", 820, 950)),
    *row(230, ("Tổng tiền hàng (3 món):", 50, 450), ("290.000", 800, 950)),
    *row(280, ("Tổng thanh toán:", 50, 450), ("290.000", 800, 950)),
]


def test_a_game_card_item_is_not_a_card_payment_line():
    r = extract_items(THE_GAME_TABLE, 290000)
    assert [i.amount for i in r.items] == [160000, 100000, 30000] and r.ok is True


def test_a_real_card_payment_row_still_ends_the_item_table():
    rows = [*THE_GAME_TABLE[:20], *row(210, ("Thẻ ATM", 50, 300), ("290.000", 800, 950))]
    assert [i.amount for i in extract_items(rows, 290000).items] == [160000, 100000, 30000]


def test_glued_ocr_tax_label_is_a_surcharge():
    rows = [*THE_GAME_TABLE[:20], *row(230, ("Tiền hàng", 50, 450), ("290.000", 800, 950)), *row(270, ("ThueGTGT8%)", 50, 400), ("23.200", 820, 950)),
            *row(320, ("Tong thanh toan", 50, 450), ("313.200", 800, 950))]
    assert extract_items(rows, 313200).ok is True


# ---- bill "Bách Hóa Mini An Khang": OCR dính số STT vào tên ("1Soa tuoi", "5 Giay ve sinh"); dòng "1L", "3,2kg", "10 cuon" là phần tên xuống dòng của món TRÊN nó
# Hộp lấy từ ảnh thật (x0, y0, x1, y1 theo pixel).
def _boxes(*cells):
    return [box(t, x0, y0, x1, y1) for t, x0, y0, x1, y1 in cells]


AN_KHANG = _boxes(
    ("STT", 6, 194, 30, 211), ("SLongia Thanhtien", 95, 194, 198, 211),
    ("1Soa tuoi Vinamilk", 8, 224, 93, 237), ("2", 98, 226, 112, 235), ("32.000", 119, 225, 153, 239), ("64.000", 166, 227, 196, 238),
    ("1L", 24, 238, 38, 251),
    ("2Trung gavi 10qua1", 7, 261, 112, 275), ("35.000", 121, 262, 153, 276), ("35.000", 165, 261, 198, 277),
    ("3Rau muong 500g", 7, 287, 93, 300), ("1", 99, 289, 109, 298), ("12.000", 120, 287, 153, 301), ("12.000", 164, 288, 198, 302),
    ("4Nudc giat OMO", 7, 311, 84, 325), ("1189.000", 100, 313, 153, 327), ("189.000", 159, 313, 197, 327),
    ("3,2kg", 23, 326, 53, 342),
    ("5 Giay ve sinh", 7, 349, 77, 363), ("1", 101, 352, 110, 362), ("78.000", 120, 351, 153, 365), ("78.000", 165, 352, 197, 366),
    ("10 cuon", 24, 364, 63, 378),
    ("6Dau goi Clear", 6, 385, 81, 400), ("1165.000", 99, 387, 154, 401), ("165.000", 148, 389, 196, 400),
    ("630g", 24, 400, 49, 416),
    ("Tong cong:", 6, 429, 83, 451), ("543.000", 141, 434, 195, 450),
)


def test_stt_glued_to_the_name_is_stripped_and_wrapped_size_lines_do_not_leak_into_the_next_item():
    r = extract_items(AN_KHANG, 543000)
    assert r.ok is True
    assert [i.name for i in r.items] == ["Soa tuoi Vinamilk", "Trung gavi 10qua1", "Rau muong 500g", "Nudc giat OMO", "Giay ve sinh", "Dau goi Clear"]
    assert [i.amount for i in r.items] == [64000, 35000, 12000, 189000, 78000, 165000]


def test_a_leading_digit_is_kept_when_it_is_not_the_next_stt_number():
    # "7Up" là tên hàng, không phải STT 7 (món đầu tiên phải là STT 1)
    boxes = _boxes(
        ("STT", 6, 194, 30, 211), ("Tên hàng", 40, 194, 90, 211), ("Thành tiền", 150, 194, 198, 211),
        ("7Up lon", 8, 224, 93, 237), ("12.000", 166, 227, 196, 238),
        ("Mi Hao Hao", 8, 250, 93, 263), ("4.000", 166, 252, 196, 264),
        ("Tong cong:", 6, 290, 83, 305), ("16.000", 141, 290, 195, 305),
    )
    r = extract_items(boxes, 16000)
    assert [i.name for i in r.items][0] == "7Up lon"
