import sys, pathlib
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))

from ocr import OcrBox
from totals import choose_total


def box(text, x0, y0, x1, y1):
    return OcrBox(text, 0.99, x0, y0, x1, y1)


def row(y, label, amount=None):
    """Một hàng cao 30px: nhãn bên trái, số tiền (nếu có) bên phải."""
    cells = [box(label, 50, y, 450, y + 30)]
    if amount is not None:
        cells.append(box(amount, 780, y, 950, y + 30))
    return cells


def rows(*specs):
    out = []
    for i, spec in enumerate(specs):
        out += row(100 + i * 45, *spec)
    return out


# ---- tổng được chọn vì NÓ THỎA CÁC RÀNG BUỘC CỘNG TRỪ, không vì nhãn đứng cạnh

def test_total_equals_subtotal_plus_tax_and_cash_minus_change():
    boxes = rows(("Tạm tính:", "146.000"), ("Thuế GTGT (8%):", "11.680"), ("Tổng cộng:", "157.680"), ("Tiền khách đưa:", "200.000"), ("Tiền thối lại:", "42.320"))
    assert choose_total(boxes) == (157680, True)


def test_no_tax_bill_is_verified_by_the_cash_identity_and_the_subtotal():
    boxes = rows(("Tổng tiền hàng (3 món):", "186.000"), ("Chiết khấu:", None), ("Tổng thanh toán:", "186.000"), ("Tiền khách đưa:", "200.000"), ("Tiền thừa:", "14.000"))
    assert choose_total(boxes) == (186000, True)


def test_service_fee_and_tax_are_added_to_the_subtotal():
    boxes = rows(("Tổng tiền hàng (4 món):", "128.000"), ("Phí phục vụ (5%):", "6.400"), ("Thuế GTGT (8%):", "10.752"), ("Tổng thanh toán:", "145.152"),
                 ("Tiền khách đưa:", "200.000"), ("Tiền thối lại:", "54.848"))
    assert choose_total(boxes) == (145152, True)


def test_a_discount_row_is_subtracted_even_when_printed_without_a_minus_sign():
    boxes = rows(("Tổng tiền hàng (5 món):", "95.000"), ("Giảm giá:", "5.000"), ("Tổng thanh toán:", "90.000"), ("Tiền khách đưa:", "100.000"), ("Tiền thối lại:", "10.000"))
    assert choose_total(boxes) == (90000, True)


def test_discount_with_a_minus_sign_and_tax_together():
    boxes = rows(("Tổng tiền hàng (5 món):", "251.000"), ("Giảm giá (Khuyến mãi):", "-12.000"), ("Thuế GTGT (8%):", "19.120"), ("Tổng thanh toán:", "258.120"),
                 ("Tiền khách đưa:", "300.000"), ("Tiền thối lại:", "41.880"))
    assert choose_total(boxes) == (258120, True)


def test_of_two_total_labels_the_one_that_satisfies_the_cash_identity_wins():
    # hai dòng "Tổng cộng": số đầu là cộng tạm, số sau sau thuế; chỉ số sau cộng với tiền thối ra đúng tiền khách đưa
    boxes = rows(("Tổng cộng", "100.000"), ("Thuế GTGT", "8.000"), ("Tổng cộng", "108.000"), ("Tiền khách đưa:", "200.000"), ("Tiền thối lại:", "92.000"))
    assert choose_total(boxes) == (108000, True)


def test_the_cash_identity_alone_picks_the_total_among_the_amounts_on_the_bill():
    # nhãn tổng bị OCR đọc hỏng ("Tong c0ng") nên không khớp nhãn nào; 157.680 = 200.000 - 42.320 và có in trên bill
    boxes = rows(("Thuế GTGT (8%):", "11.680"), ("Tong c0ng:", "157.680"), ("Tiền khách đưa:", "200.000"), ("Tiền thối lại:", "42.320"))
    assert choose_total(boxes) == (157680, True)


def test_a_wrong_labelled_total_is_replaced_by_the_one_the_arithmetic_supports():
    # nhãn "Tổng thanh toán" đứng cạnh 42.000 (số của món cuối) nhưng tạm tính + thuế = 157.680 và có in trên bill
    boxes = rows(("Tạm tính:", "146.000"), ("Thuế GTGT (8%):", "11.680"), ("Tổng thanh toán:", "42.000"), ("Cộng:", "157.680"))
    assert choose_total(boxes)[0] == 157680


# ---- không kiểm chứng được thì trả đúng giá trị dự phòng và nói rõ là chưa kiểm chứng

def test_unverifiable_bill_returns_the_fallback_unverified():
    boxes = rows(("Tổng tiền hàng:", "125.000"), ("Tổng thanh toán:", "150.000"))  # lệch 25.000, không thuế/giảm giá nào giải thích
    assert choose_total(boxes, fallback=150000) == (150000, False)


def test_no_fallback_and_nothing_verifiable_is_none():
    assert choose_total(rows(("Tổng cộng", "78.589"), ("Tổng cộng", "982.364"))) == (None, False)


def test_no_boxes():
    assert choose_total([]) == (None, False)
    assert choose_total([], fallback=5000) == (5000, False)


def test_amounts_below_the_minimum_never_become_the_total():
    boxes = rows(("Thuế GTGT (0%):", "0"), ("Tổng thanh toán:", "60.000"), ("Tiền khách đưa:", "100.000"), ("Tiền thối lại:", "40.000"))
    assert choose_total(boxes) == (60000, True)


def test_cash_and_change_rows_are_never_candidates_themselves():
    boxes = rows(("Tiền khách đưa:", "200.000"), ("Tiền thối lại:", "42.320"))
    assert choose_total(boxes, fallback=None)[0] is None


# ---- ảnh thật c55ef611 (nhà hàng, nhãn viết dính "ThueGTGT8%)", "Tién hang" không có chữ "tổng"): từng chọn nhầm "Tiền hàng" 435.000 làm tổng
def test_glued_tax_label_and_tien_hang_subtotal_still_give_the_right_total():
    boxes = rows(("Quattro Formaggi", "120.000"), ("Pizza bon loai", "110.000"), ("y thitxong", "175.000"), ("4Cocacola", "30.000"),
                 ("Tién hang", "435.000d"), ("ThueGTGT8%)", "34.800d"), ("Tong thanh toan", "469.800d"), ("Con phai thu", "469.800d"))
    assert choose_total(boxes) == (469800, True)


def test_a_goods_subtotal_is_not_the_total_even_when_it_equals_the_sum_of_the_items():
    boxes = rows(("Mon A", "60.000"), ("Mon B", "40.000"), ("Tiền hàng", "100.000"), ("Tổng thanh toán", "150.000"))
    assert choose_total(boxes, fallback=150000) == (150000, False)


def test_an_item_row_whose_amount_equals_a_target_is_not_a_total():
    # tạm tính 100.000 - giảm giá 40.000 = 60.000 trùng đúng giá món A, nhưng "Món A" không phải nhãn tổng
    boxes = rows(("Mon A", "60.000"), ("Mon B", "40.000"), ("Tổng tiền hàng", "100.000"), ("Giảm giá", "40.000"), ("Tổng thanh toán", "150.000"))
    assert choose_total(boxes, fallback=150000)[0] == 150000
