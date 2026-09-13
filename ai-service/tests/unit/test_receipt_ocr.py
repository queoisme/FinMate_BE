"""Bóc trường từ text hóa đơn.

Engine OCR bị thay bằng fake: nó là biên I/O phụ thuộc binary hệ điều hành, còn thứ đáng
test là chọn ĐÚNG con số trong một tờ giấy đầy con số.
"""

from datetime import datetime

import pytest

from app.pipeline.stages.receipt_ocr import read_receipt
from app.utils.text_utils import VIETNAM_TZ

NOW = datetime(2026, 9, 13, 12, 0, tzinfo=VIETNAM_TZ)

SUPERMARKET = """WINMART+ NGUYEN TRAI
Dia chi: 123 Nguyen Trai, Q.1
MST: 0312345678
HOA DON BAN HANG
So HD: 000123
Ngay: 12/09/2026 19:45
--------------------------------
Sua tuoi Vinamilk   2 x 32.000   64.000
Banh mi sandwich    1 x 25.000   25.000
Nuoc suoi Lavie     3 x 7.000    21.000
--------------------------------
Tong cong:                      110.000
Tien khach dua:                 200.000
Tien thoi lai:                   90.000
Cam on quy khach!"""

RESTAURANT = """NHA HANG NGON
Bang so: 12
Thanh tien           320.000
TONG THANH TOAN      320.000
Ngay 13/09/2026 20:15"""

NO_LABEL = """CAY XANG PETROLIMEX
13/09/2026
Xang RON95      1.5 L
45.000"""


class FakeEngine:
    def __init__(self, text: str) -> None:
        self._text = text

    def read_text(self, image: bytes) -> str:
        return self._text


def read(text: str):
    return read_receipt(FakeEngine(text), b"anh-gia", NOW)


def test_total_wins_over_the_cash_the_customer_handed_over():
    """ "Tiền khách đưa 200.000" lớn hơn tổng tiền 110.000.

    Đây là cái bẫy chính của hóa đơn siêu thị: chọn con số lớn nhất sẽ ghi nhận sai số tiền,
    và nó sai theo hướng luôn luôn lớn hơn thực tế.
    """
    reading = read(SUPERMARKET)
    assert reading.amount_cents == 110_000
    assert reading.total_marker == "tong cong"


def test_total_beats_the_line_items():
    reading = read(SUPERMARKET)
    assert reading.amount_cents != 64_000


def test_the_last_label_wins():
    """Hóa đơn liệt kê từ trên xuống; "Thành tiền" từng dòng đứng trước "Tổng thanh toán"."""
    reading = read(RESTAURANT)
    assert reading.amount_cents == 320_000
    assert reading.total_marker == "tong thanh toan"


def test_falls_back_to_the_largest_amount_without_a_label():
    reading = read(NO_LABEL)
    assert reading.amount_cents == 45_000
    assert reading.total_marker is None
    # Suy đoán thì phải tự nhận là suy đoán.
    assert reading.confidence < read(SUPERMARKET).confidence


def test_merchant_is_the_shop_name_not_the_paperwork():
    assert read(SUPERMARKET).merchant_name == "WINMART+ NGUYEN TRAI"
    assert read(RESTAURANT).merchant_name == "NHA HANG NGON"


def test_merchant_skips_addresses_tax_codes_and_invoice_headers():
    reading = read(SUPERMARKET)
    assert "Dia chi" not in (reading.merchant_name or "")
    assert "MST" not in (reading.merchant_name or "")
    assert "HOA DON" not in (reading.merchant_name or "")


def test_receipt_date_is_read_not_defaulted_to_upload_time():
    """Người ta chụp lại hóa đơn hôm trước là chuyện thường; lấy thời điểm tải lên sẽ đẩy
    giao dịch sang sai ngày, và có thể sang sai cả chu kỳ ngân sách."""
    reading = read(SUPERMARKET)
    assert reading.transacted_at == datetime(2026, 9, 12, 19, 45, tzinfo=VIETNAM_TZ)
    assert reading.transacted_at.date() != NOW.date()


@pytest.mark.parametrize("text", ["", "   \n  \n"])
def test_blank_ocr_output_yields_nothing(text):
    reading = read(text)
    assert reading.amount_cents is None
    assert reading.confidence == 0.0


def test_text_without_any_amount_yields_no_amount():
    reading = read("CUA HANG ABC\nCam on quy khach")
    assert reading.amount_cents is None


def test_confidence_never_reaches_the_one_tap_threshold():
    """Docx phương thức 3 yêu cầu người dùng rà soát trước khi lưu, còn mốc 0,85 của Flow 1
    lại dành cho xác nhận một chạm — luồng này không được chạm vào vùng đó."""
    for text in (SUPERMARKET, RESTAURANT, NO_LABEL):
        assert read(text).confidence < 0.85


def test_count_of_items_on_the_total_line_is_not_the_total():
    """ "Tổng cộng 3 mon 250.000" — chỉ xét phần SAU nhãn thì vẫn còn cả 3 lẫn 250.000,
    nên phải lấy con số lớn hơn, không phải con số đầu tiên."""
    reading = read("CUA HANG ABC\nTong cong 3 mon        250.000")
    assert reading.amount_cents == 250_000


def test_a_tax_code_is_not_an_amount():
    """Mã số thuế "0312345678" đọc thành 312.345.678 đồng nếu chỉ lấy con số lớn nhất.

    Tesseract thật tìm ra lỗi này, không phải test — text hóa đơn viết tay trong các test
    trên đều có nhãn "Tổng cộng" nên nhánh đoán chưa bao giờ chạy tới. Người Việt viết tiền
    là luôn có dấu ngăn nhóm, nên dãy số trần dài phân biệt được với tiền nhờ điều đó.
    """
    reading = read("CUA HANG ABC\nMST: 0312345678\nDT: 0912345678\n45.000")
    assert reading.amount_cents == 45_000


def test_an_invoice_number_is_not_an_amount():
    reading = read("SIEU THI XYZ\nSo HD: 00012345678\nTong cong 250.000")
    assert reading.amount_cents == 250_000


def test_a_receipt_with_only_bare_digit_runs_yields_no_amount():
    """Thà không có số tiền còn hơn có một số tiền bịa ra từ số điện thoại."""
    reading = read("CUA HANG ABC\nMST: 0312345678\nHotline: 19001234")
    assert reading.amount_cents is None
