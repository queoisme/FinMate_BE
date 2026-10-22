"""Hóa đơn thật (KidsPlaza, COCO MART) tách món hỏng vì OCR đọc sai một ký tự: nhãn "Tổng tiền hàng" thành "Téng tién hang", giá 20.000 thành 20.009."""
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))

from parsing.extract import extract_total
from parsing.items import _rows, extract_items, payable_total
from reading.ocr import OcrBox
from parsing.totals import choose_total


def box(text, x0, y0, x1, y1):
    return OcrBox(text, 0.99, x0, y0, x1, y1)


def row(y, *cells):
    """Một hàng cao 30px tại y; cells là (text, x0, x1)."""
    return [box(t, x0, y, x1, y + 30) for t, x0, x1 in cells]


HEADER = row(100, ("Tên hàng", 50, 300), ("SL", 500, 540), ("Thành tiền", 780, 950))


class TestGarbledSubtotalLabel:
    # KidsPlaza: giá từng dòng ĐÃ trừ khuyến mại (50.000 - 7.500 = 42.500), "Tổng tiền hàng" là giá gốc 929.000, rồi "giảm giá" 22.500, cần thanh toán 906.500.
    KIDS = HEADER + row(200, ("Sua PediaSure 850g Uc", 50, 420), ("779,000", 800, 950)) \
        + row(250, ("Sua chua Hoff vi xoai", 50, 420), ("42,500", 800, 950)) \
        + row(300, ("Sua chua Hoff vi tao", 50, 420), ("42,500", 800, 950)) \
        + row(350, ("Sua chua Hoff vi chuoi", 50, 420), ("42,500", 800, 950)) \
        + row(420, ("Téng tién hang", 50, 350), ("929,000", 800, 950)) \
        + row(470, ("Tổng giá trị giảm giá", 50, 400), ("22,500", 800, 950)) \
        + row(520, ("Tổng tiền cần thanh toán", 50, 450), ("906,500", 800, 950))

    def test_garbled_label_closes_the_table_instead_of_becoming_an_item(self):
        r = extract_items(self.KIDS, 906_500)
        assert [i.name for i in r.items] == ["Sua PediaSure 850g Uc", "Sua chua Hoff vi xoai", "Sua chua Hoff vi tao", "Sua chua Hoff vi chuoi"]
        assert r.ok is True

    def test_discount_already_in_the_line_prices_is_not_subtracted_twice(self):
        r = extract_items(self.KIDS, 906_500)
        assert [i.amount for i in r.items] == [779_000, 42_500, 42_500, 42_500]
        assert not any(i.is_discount for i in r.items)

    def test_a_separate_discount_is_still_subtracted_when_lines_are_gross(self):
        # Giá dòng là giá gốc: tổng bill = 929.000 - 22.500 = 906.500, nên giảm giá cuối phải được trừ.
        gross = HEADER + row(200, ("Sua PediaSure 850g Uc", 50, 420), ("779,000", 800, 950)) \
            + row(250, ("Sua chua Hoff vi xoai", 50, 420), ("50,000", 800, 950)) \
            + row(300, ("Sua chua Hoff vi tao", 50, 420), ("50,000", 800, 950)) \
            + row(350, ("Sua chua Hoff vi chuoi", 50, 420), ("50,000", 800, 950)) \
            + row(420, ("Téng tién hang", 50, 350), ("929,000", 800, 950)) \
            + row(470, ("Tổng giá trị giảm giá", 50, 400), ("22,500", 800, 950)) \
            + row(520, ("Tổng tiền cần thanh toán", 50, 450), ("906,500", 800, 950))
        r = extract_items(gross, 906_500)
        assert r.ok is True
        assert [i.amount for i in r.items] == [779_000, 50_000, 50_000, 50_000, -22_500]


class TestOneDigitMisread:
    # COCO MART: tên một hàng, hàng sau là "đơn giá | số lượng | thành tiền".
    @staticmethod
    def bill(amount_text, second="30,000"):
        return HEADER + row(200, ("Sting vang chai", 50, 400)) \
            + row(240, ("10,000", 50, 150), ("2", 450, 470), (amount_text, 800, 950)) \
            + row(300, ("Nuoc suoi", 50, 300), (second, 800, 950)) \
            + row(900, ("Tổng cộng", 50, 300), ("50,000", 800, 950))

    def test_unit_price_times_quantity_repairs_a_single_misread_digit(self):
        r = extract_items(self.bill("20.009"), 50_000)
        assert [i.amount for i in r.items] == [20_000, 30_000]
        assert r.ok is True

    def test_not_repaired_when_it_would_not_balance_the_total(self):
        r = extract_items(self.bill("20.009"), 99_999)
        assert [i.amount for i in r.items] == [20_009, 30_000]
        assert r.ok is False

    def test_corrected_sum_is_offered_for_choosing_the_total(self):
        assert extract_items(self.bill("20.009"), None).fixed_total == 50_000

    def test_a_real_promotion_is_left_alone_when_the_sum_already_balances(self):
        # 15.000 x 2 = 30.000 nhưng in 25.000 (khuyến mại): lệch đúng một chữ số, nhưng tổng đã khớp nên không được "sửa".
        promo = HEADER + row(200, ("Banh quy", 50, 400)) \
            + row(240, ("15,000", 50, 150), ("2", 450, 470), ("25,000", 800, 950)) \
            + row(300, ("Nuoc suoi", 50, 300), ("25,000", 800, 950)) \
            + row(900, ("Tổng cộng", 50, 300), ("50,000", 800, 950))
        r = extract_items(promo, 50_000)
        assert [i.amount for i in r.items] == [25_000, 25_000]
        assert r.ok is True

    def test_a_row_with_a_promotion_column_is_never_rewritten(self):
        # Đơn giá | SL | KM | thành tiền: hai ô tiền bên trái nên không đoán được ô nào là đơn giá.
        km = HEADER + row(200, ("Sua chua Hoff", 50, 400)) \
            + row(240, ("50,000", 300, 380), ("1", 450, 470), ("7,500", 600, 700), ("42,509", 800, 950)) \
            + row(300, ("Nuoc suoi", 50, 300), ("20,000", 800, 950)) \
            + row(900, ("Tổng cộng", 50, 300), ("62,500", 800, 950))
        r = extract_items(km, 62_500)
        assert [i.amount for i in r.items] == [42_509, 20_000]
        assert r.ok is False


class TestMissingQuantityIsInferred:
    # Ảnh thật: OCR bỏ mất ô số lượng "2" ở một số hàng, chỉ còn "đơn giá | thành tiền".
    @staticmethod
    def bill(unit, amount, total):
        return HEADER + row(200, ("Sting vang chai", 50, 400))             + row(240, (unit, 50, 150), (amount, 800, 950))             + row(300, ("Nuoc suoi", 50, 300), ("30,000", 800, 950))             + row(900, ("Tổng cộng", 50, 300), (total, 800, 950))

    def test_the_only_multiple_one_digit_away_repairs_the_amount(self):
        r = extract_items(self.bill("10,000", "20.009", "50,000"), 50_000)
        assert [i.amount for i in r.items] == [20_000, 30_000]
        assert r.ok is True

    def test_two_multiples_equally_close_means_no_guess(self):
        # 2.000 x 7 = 14.000 và 2.000 x 8 = 16.000 đều lệch 15.000 một chữ số. Dù 14.000 sẽ làm tổng khớp 44.000, có hai khả năng thì không đoán.
        r = extract_items(self.bill("2,000", "15,000", "44,000"), 44_000)
        assert [i.amount for i in r.items] == [15_000, 30_000]
        assert r.ok is False

    def test_an_amount_that_is_already_an_exact_multiple_is_left_alone(self):
        r = extract_items(self.bill("10,000", "30,000", "60,000"), 60_000)
        assert [i.amount for i in r.items] == [30_000, 30_000]
        assert r.fixed_total is None

    def test_still_needs_the_total_to_balance(self):
        r = extract_items(self.bill("10,000", "20.009", "50,000"), 99_999)
        assert [i.amount for i in r.items] == [20_009, 30_000]
        assert r.ok is False


class TestChooseTotalUsesTheRepairedSum:
    def test_printed_total_is_verified_by_the_sum_with_the_misread_digit_repaired(self):
        # Các món cộng ra 50.009 (đọc sai), đã sửa thì 50.000 = số in ở dòng Tổng cộng: kiểm chứng được nhờ phép sửa.
        assert choose_total(TestOneDigitMisread.bill("20.009")) == (50_000, True)

    def test_a_total_that_matches_nothing_stays_unverified(self):
        boxes = TestOneDigitMisread.bill("20.009")
        boxes = [b if b.text != "50,000" else box("77,777", b.x0, b.y0, b.x1, b.y1) for b in boxes]
        assert choose_total(boxes, fallback=12_345) == (12_345, False)


class TestWrappedTotal:
    # COCO MART: cột tổng hẹp nên "452,432" bị in cắt thành "452,43" ở hàng nhãn và "2" ở hàng ngay dưới.
    WRAPPED = row(800, ("Tổng tiền hàng:", 50, 400), ("452,43", 850, 913)) + row(836, ("2", 903, 913))         + row(880, ("Chiết khấu :", 50, 400), ("0", 900, 913))         + row(920, ("Tổng thanh toán:", 50, 400), ("452,43", 850, 913)) + row(956, ("2", 903, 913))

    def test_the_cut_digit_is_joined_back_for_the_payable_total(self):
        assert payable_total(self.WRAPPED) == 452_432

    def test_the_joined_total_is_verified_by_the_subtotal(self):
        assert choose_total(self.WRAPPED) == (452_432, True)

    def test_thousands_groups_before_the_cut_work_too(self):
        boxes = row(800, ("Tổng cộng", 50, 400), ("1,234,56", 820, 913)) + row(836, ("7", 903, 913))
        assert payable_total(boxes) == 1_234_567

    def test_a_quantity_under_a_price_is_not_merged_into_it(self):
        # Nhãn "Banh mi" không phải nhãn tổng: "2" dưới giá "12,50" là số lượng của hàng khác.
        boxes = row(800, ("Banh mi", 50, 400), ("12,50", 850, 913)) + row(836, ("2", 903, 913))
        assert len(_rows(boxes)) == 2

    def test_a_digit_that_is_not_under_the_number_is_not_merged(self):
        boxes = row(800, ("Tổng thanh toán:", 50, 400), ("452,43", 850, 913)) + row(836, ("2", 100, 110))
        assert len(_rows(boxes)) == 2

    def test_a_digit_row_with_other_text_is_not_merged(self):
        boxes = row(800, ("Tổng thanh toán:", 50, 400), ("452,43", 850, 913)) + row(836, ("Cam on", 50, 300), ("2", 903, 913))
        assert len(_rows(boxes)) == 2


class TestHandwrittenBillWithMergedCellsAndClippedTotalLabel:
    """Hóa đơn viết tay KINGFOOD (22/10/2026): 7 món OCR tách ô, 3 món cuối bị gộp tên + "SL x đơn giá = thành tiền" vào MỘT ô, và nhãn
    "TỔNG CỘNG" rơi chữ đầu thành "ONG CONG". Trước đây chỉ tách được 7/10 món (cộng 256.000), tổng bị đoán là 68.000 (số lặp nhiều nhất)."""
    ROWS = [("1. Sua tuoi 1L", "1x32.000=32.000"), ("2. Banh mi goi", "1x28.000=28.000"), ("3. Nuoc cam 1L", "1x45.000=45.000"),
            ("4. Yen mach 500g", "1x68.000=68.000"), ("5. Vo o ly", "2X12.000=24.000"), ("6. But bi xanh", "3X8.000=24.000"),
            ("7. Sach tap to", "1x35.000=35.000")]
    MERGED = ["8.Khau trang 10 chiec1x 25.000=25.000", "9.Nuoc muoi sinh ly2x12.000=24.000", "10. Ban chai danh rang 2x 18.000= 36.000"]
    BOXES = [box("KINGFOOD", 360, 60, 680, 110), box("HOA DON BAN HANG", 280, 130, 760, 170), box("Ngay: 22/10/2026", 380, 190, 690, 230)]         + [b for k, (name, calc) in enumerate(ROWS) for b in row(280 + 70 * k, (name, 170, 440), (calc, 540, 880))]         + [b for k, text in enumerate(MERGED) for b in row(280 + 70 * (7 + k), (text, 170, 880))]         + row(1010, ("ONG CONG: 341.000", 165, 660))

    def test_a_name_and_its_amount_in_one_box_are_still_an_item(self):
        res = extract_items(self.BOXES, 341_000)
        assert [i.amount for i in res.items] == [32_000, 28_000, 45_000, 68_000, 24_000, 24_000, 35_000, 25_000, 24_000, 36_000]
        assert res.ok
        assert "Khau trang" in res.items[7].name and "25.000" not in res.items[7].name  # phần tiền không dính vào tên món

    def test_the_total_is_verified_by_the_items_even_when_its_label_lost_a_letter(self):
        assert choose_total(self.BOXES) == (341_000, True)

    def test_a_total_label_that_lost_its_first_letter_is_still_a_label(self):
        lines = ["KINGFOOD", "1. Sua tuoi 1x68.000=68.000", "2. Banh mi 1x68.000=68.000", "ONG CONG: 341.000"]
        assert extract_total(lines) == 341_000  # không phải 68.000 (số lặp nhiều nhất)
        assert extract_total(["1. Sua 1x68.000=68.000", "2. Banh 1x68.000=68.000", "TONG CONG: 341.000"]) == 341_000
        assert extract_total(["1. Sua 1x68.000=68.000", "2. Banh 1x68.000=68.000", "Mong cong bien: 341.000"]) != 341_000  # chữ khác thì không phải nhãn
