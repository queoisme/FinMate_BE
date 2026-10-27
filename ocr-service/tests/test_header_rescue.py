import sys, pathlib

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))

from parsing.extract import extract_merchant


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
    from reading.ocr import repair_cut_off
    first = [("Chi Tram Quan 9 - banh khoai m", 0.93), ("Khoai mi nuong", 0.9)]
    retry = [("Chi Tram Quan 9 - banh khoai mi", 0.9), ("Khoai mi nuong", 0.9)]
    assert repair_cut_off(first, retry)[0][0] == "Chi Tram Quan 9 - banh khoai mi"
    assert repair_cut_off(first, [("Something else m", 0.9)]) == first


def test_vietocr_only_adds_accents_unless_it_reads_a_known_brand():
    from reading.ocr import _prefer
    assert _prefer("Chị Trâm", "Chi Tram") == "Chị Trâm"                      # chỉ thêm dấu
    assert _prefer("Pizzahd", "PizzaHut") == "PizzaHut"                       # đổi chữ, không phải thương hiệu: giữ Paddle
    assert _prefer("coopmart", "co.cpmart") == "coopmart"                     # thương hiệu: nhận VietOCR
    assert _prefer("Winmart", "N/inMart") == "Winmart"
    assert _prefer("Winmert", "WinMart") == "WinMart"                         # Paddle đã đúng thương hiệu: giữ
    assert _prefer("TRONG THI winmart THI THI", "tmcmi") == "tmcmi"           # bịa chữ, không giống Paddle: giữ
    assert _prefer("PHIẾU", "PHIEU") == "PHIẾU" and _prefer("phiếu", "PHIEU") == "PHIẾU"


def test_body_lines_accept_vietocr_letter_fixes_but_not_invented_text():
    # Thân phiếu (tên món): VietOCR được sửa cả chữ PaddleOCR đọc sai, miễn là còn giống; bịa chữ thì giữ Paddle.
    # Đo trên 58 ảnh thật: tên món chứa nguyên đoạn nhãn 123/161 (Paddle) -> 149/157.
    from reading.ocr import _prefer_body
    assert _prefer_body("Pizza bốn loại", "Pizza bon loai") == "Pizza bốn loại"                      # thêm dấu
    assert _prefer_body("Nước ngọt", "Nudc ngot") == "Nước ngọt"                                      # sửa chữ đọc sai
    assert _prefer_body("Thông tin chuyển khoản thu thu thu", "Cong Jy TNHH Minh Pha") == "Cong Jy TNHH Minh Pha"  # bịa chữ
    assert _prefer_body("sữa tươi", "SUA TUOI") == "SỮA TƯƠI"                                         # Paddle IN HOA: giữ IN HOA


def test_header_lines_keep_the_strict_gate_while_body_lines_use_the_body_gate(monkeypatch):
    import io
    from PIL import Image
    from reading import ocr

    class Engine:
        def ocr(self, bgr, cls=True):
            return [[([[10, 20], [110, 22], [108, 40], [12, 38]], ("PizzaHut", 0.9)),
                     ([[10, 60], [110, 62], [108, 80], [12, 78]], ("Nudc ngot", 0.9))]]

    answers = iter(["Pizzahd", "Nước ngọt"])
    monkeypatch.setattr(ocr, "_engine", lambda: Engine())
    monkeypatch.setattr(ocr, "_use_vietocr", lambda: True)
    monkeypatch.setattr(ocr, "_VIET_HEAD_LINES", 1)
    monkeypatch.setattr(ocr, "crop_receipt", lambda a: a)
    monkeypatch.setattr(ocr.vietocr_onnx, "recognize", lambda image: next(answers))
    buf = io.BytesIO()
    Image.new("RGB", (900, 300), "white").save(buf, "PNG")
    assert [b.text for b in ocr.read_boxes(buf.getvalue())] == ["PizzaHut", "Nước ngọt"]  # đầu phiếu: "Pizzahd" bị chặn; thân: nhận
