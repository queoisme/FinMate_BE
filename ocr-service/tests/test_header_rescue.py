import sys, pathlib

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))

from extract import extract_merchant


def test_glued_field_labels_and_codes_are_never_the_merchant():
    # OCR hay viết liền trường thông tin và mất dấu hai chấm: NVGH..., Hóa đơn số..., mã có gạch dưới
    lines = ["NVGHQ9_2-Q92", "Hóa ddn s6IN042620", "Ngay hóa don01/10/2026", "DTNPP:", "NVBHPhanThiMyLinh"]
    assert extract_merchant(lines, [0.9] * 5) is None


def test_generic_receipt_phrases_are_not_the_fallback_merchant():
    assert extract_merchant(["Cam on quy khach", "Tong cong", "Don gia"], [0.9] * 3) is None
    # tên thật vẫn được lấy, kể cả khi chứa chữ "hoa" / "hang"
    assert extract_merchant(["Cua hang Hoa Sen", "Cam on quy khach"], [0.9, 0.9]) == "Cua hang Hoa Sen"
    assert extract_merchant(["Quan Ba Sau", "Ghi chu"], [0.9, 0.9]) == "Quan Ba Sau"


def test_cut_off_line_is_repaired_from_a_retry_read():
    from ocr import repair_cut_off
    first = [("Chi Tram Quan 9 - banh khoai m", 0.93), ("Khoai mi nuong", 0.9)]
    retry = [("Chi Tram Quan 9 - banh khoai mi", 0.9), ("Khoai mi nuong", 0.9)]
    assert repair_cut_off(first, retry)[0][0] == "Chi Tram Quan 9 - banh khoai mi"
    assert repair_cut_off(first, [("Something else m", 0.9)]) == first


def test_vietocr_only_adds_accents_unless_it_reads_a_known_brand():
    from ocr import _prefer
    assert _prefer("Chị Trâm", "Chi Tram") == "Chị Trâm"                      # chỉ thêm dấu
    assert _prefer("Pizzahd", "PizzaHut") == "PizzaHut"                       # đổi chữ, không phải thương hiệu: giữ Paddle
    assert _prefer("coopmart", "co.cpmart") == "coopmart"                     # thương hiệu: nhận VietOCR
    assert _prefer("Winmart", "N/inMart") == "Winmart"
    assert _prefer("Winmert", "WinMart") == "WinMart"                         # Paddle đã đúng thương hiệu: giữ
    assert _prefer("TRONG THI winmart THI THI", "tmcmi") == "tmcmi"           # bịa chữ, không giống Paddle: giữ
    assert _prefer("PHIẾU", "PHIEU") == "PHIẾU" and _prefer("phiếu", "PHIEU") == "PHIẾU"
