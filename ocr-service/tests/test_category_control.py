import json
import sys, pathlib

import pytest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))

from parsing.extract import analyze, classify_category_detailed, compute_coverage

FIX = pathlib.Path(__file__).parent / "fixtures" / "sample_ocr_lines.json"
SAMPLES = json.loads(FIX.read_text(encoding="utf-8"))
KISNAC = [(t, c) for t, c in SAMPLES["kisnac"]]
HIGHLANDS = [(t, c) for t, c in SAMPLES["highlands"]]


KISNAC_DARK = [(t, c) for t, c in SAMPLES["kisnac_dark"]]
LAMDUONG = [(t, c) for t, c in SAMPLES["lamduong"]]


LONGCHAU5 = [(t, c) for t, c in SAMPLES["longchau5"]]
LONGCHAU6 = [(t, c) for t, c in SAMPLES["longchau6"]]


WINMART = [(t, c) for t, c in SAMPLES["winmart"]]


COOPMART = [(t, c) for t, c in SAMPLES["coopmart"]]


BACHHOAXANH = [(t, c) for t, c in SAMPLES["bachhoaxanh"]]


POS_IMPORT = [(t, c) for t, c in SAMPLES["pos_import"]]


DELIVERY_NOTE = [(t, c) for t, c in SAMPLES["delivery_note"]]


PINK_SLIP = [(t, c) for t, c in SAMPLES["pink_slip"]]


BHX_SCAN = [(t, c) for t, c in SAMPLES["bhx_scan"]]


SATRA_NOTE = [(t, c) for t, c in SAMPLES["satra_note"]]


SATRA_VARIANTS = {k: [(t, c) for t, c in SAMPLES[k]] for k in ("satra_m4", "satra_m5", "satra_m6")}


@pytest.mark.parametrize("key", SATRA_VARIANTS)
def test_total_is_the_repeated_payable_amount_not_a_quantity_row(key):
    # "Tổng số lượng" không phải nhãn tiền; "Tổng tiền phải trả" cạnh các dòng 0 thì dùng số lặp 1,419,282
    assert analyze(SATRA_VARIANTS[key])["total_amount"] == 1419282


def test_quantity_label_is_not_a_total_label():
    from parsing.extract import extract_total

    assert extract_total(["Tong so luong", "23,220"]) is None
    assert extract_total(["Tong so luong", "11,610", "Tong tien phai tra", "0", "Tien chua chiet khau", "1,419,282", "1,419,282"]) == 1419282


@pytest.mark.parametrize("key", SATRA_VARIANTS)
def test_address_fragments_and_cut_off_text_are_never_the_merchant(key):
    merchant = analyze(SATRA_VARIANTS[key])["merchant"]
    assert merchant is None or "HCM" not in merchant.upper()
    assert merchant is None or merchant[0].isupper()          # "uong Mai Sai Gon" là mảnh bị cắt giữa chữ


def test_glued_company_line_is_found_but_an_address_line_is_not():
    from parsing.extract import extract_merchant

    lines = ["S32Binh.DiAnTp.HCM", "ChiNhanhTongCongTyThuongMaiSaiGon", "PHIEU GIAO NHAN VA THANH TOAN"]
    assert extract_merchant(lines, [0.9, 0.9, 0.9]) == "ChiNhanhTongCongTyThuongMaiSaiGon"
    assert extract_merchant(["So 32 An Binh, P. Di An, Tp. HCM", "Cua hang Hoa Sen"], [0.9, 0.9]) == "Cua hang Hoa Sen"


def test_satra_variant_with_readable_header_gets_the_company_name():
    assert analyze(SATRA_VARIANTS["satra_m4"])["merchant"] == "Chi Nhänh Töng Cöng Ty Thuong Mai Sai Gon"


def test_field_and_date_lines_are_never_the_merchant():
    from parsing.extract import extract_merchant

    assert extract_merchant(["Ngay hoa don: 01/10/2026", "NVBH: Phan Thi My Linh", "Cua hang Hoa Sen"], [0.9] * 3) == "Cua hang Hoa Sen"
    assert extract_merchant(["Ngay hoa don: 01/10/2026", "Hoa don so: IN042620", "DT GSNPP:"], [0.9] * 3) is None
    assert extract_merchant(["Ngay in 01/10/2026 11:01", "SDT 0866647689"], [0.9, 0.9]) is None


def test_distributor_note_whose_header_is_unreadable_leaves_the_name_empty_but_keeps_the_total():
    r = analyze(SATRA_NOTE)
    assert r["merchant"] is None            # tên công ty không đọc được: thà để trống còn hơn lấy dòng "Ngày hóa đơn"
    assert r["total_amount"] == 1419282
    assert r["needs_review"] is True


def test_title_line_is_not_a_total_label_and_date_or_staff_code_is_not_money():
    from parsing.extract import extract_total

    # "PHIEU THANH TOAN" chỉ là tiêu đề; dòng ngày giờ + mã nhân viên NV:305162 không phải tiền
    lines = ["PHIEU THANH TOAN BACH HOA XANH", "So CT: OV136193610001671", "03/10/2026 18:34 - NV:305162",
             "Thanh tien", "70.900", "Tong tien:", "70.900"]
    assert extract_total(lines) == 70900
    assert extract_total(["Tong cong", "03/10/2026 18:34"]) is None


def test_numbers_after_code_labels_are_not_amounts():
    from parsing.extract import _amounts

    assert _amounts("NV:305162") == []
    assert _amounts("So HD: 90135  Tong 50.000") == [50000]
    assert _amounts("Ma so thue 0312356259") == []
    assert _amounts("Tien khach dua: 100.000") == [100000]   # nhãn tiền vẫn đọc bình thường


def test_scanned_bach_hoa_xanh_bill_total_is_the_printed_70900():
    r = analyze(BHX_SCAN)
    assert r["total_amount"] == 70900
    assert (r["category"], r["category_source"]) == ("groceries", "brand")


def test_total_value_two_lines_below_its_label_beats_a_truncated_number_right_below():
    from parsing.extract import extract_total

    # nhãn "Tổng tiền hàng", dòng kế tiếp bị rớt chữ số (1.712,00), số đúng nằm sau dòng "Số lượng sản phẩm"
    lines = ["Tong tin hing", "1.712,00", "S6luyngsanpham", "Th1.712.000", "Cam on quy khach"]
    assert extract_total(lines) == 1712000
    assert extract_total(["Tong cong", "Cam on quy khach 99.000 da mua"]) is None


def test_glued_title_is_not_the_merchant_and_a_seller_label_gives_the_name():
    from parsing.extract import extract_merchant

    lines = ["PHIEUGIAONHANTHANHTOAN", "T@n NPP Cong Ty TNHH MinhPhTanh", "TenKH99993554CoCoMart"]
    assert extract_merchant(lines, [0.98, 0.76, 0.9]) == "Cong Ty TNHH MinhPhTanh"


def test_buyer_label_lines_are_not_the_merchant():
    from parsing.extract import extract_merchant

    assert extract_merchant(["Ten khach hang: HO KINH DOANH COCO MART", "Highlands Coffee"], [0.95, 0.95]) == "Highlands Coffee"
    assert extract_merchant(["Nguoi mua: Nguyen Van A", "Cua hang Hoa Sen"], [0.95, 0.95]) == "Cua hang Hoa Sen"


def test_urls_are_not_product_evidence_and_condiments_are_groceries():
    assert classify_category_detailed("ABC", ["https://one.com.vn/dms/sales"]) == ("other", "none", 0.0)
    assert classify_category_detailed("ABC", ["Dau hao CHIN-SU 400gr", "Tuong ot CHIN-SU 1kg"]) == ("groceries", "product", 0.7)


def test_pink_distributor_slip_gets_total_and_name_but_garbled_products_stay_unclassified():
    r = analyze(PINK_SLIP)
    assert r["total_amount"] == 1712000
    assert r["merchant"] == "Cong Ty TNHH MinhPhTanh"
    # tên sản phẩm bị OCR đọc hỏng nặng (Nirocmam, Turom.t...): không đủ căn cứ nên hỏi người dùng thay vì đoán
    assert r["category"] == "other" and r["needs_review"] is True


def test_company_name_split_across_stamp_lines_is_joined():
    from parsing.extract import extract_merchant

    lines = ["KHAU THUC PHAM SACH", "CONG TY TNHH TM DV XNK", "THUC.PHAM SACH", "Chi Minh, Viet Nam.", "MST0312356259"]
    assert extract_merchant(lines, [0.96, 0.91, 0.92, 0.96, 0.96]) == "CONG TY TNHH TM DV XNK THUC.PHAM SACH"
    # không nối dòng địa chỉ/liên hệ vào tên công ty
    assert extract_merchant(["CONG TY TNHH ABC", "46/53/12 DUONG SO 18", "MST 0312356259"], [0.9] * 3) == "CONG TY TNHH ABC"


def test_raw_meat_products_with_glued_numbers_count_as_groceries():
    # OCR hay dính chữ với số: "BAP BO200G"
    assert classify_category_detailed("ABC", ["THIT BO XAO 200G", "BAP BO200G"]) == ("groceries", "product", 0.7)
    assert classify_category_detailed("ABC", ["Bo vien ngon 300gr", "THIT GA 500G"])[:2] == ("groceries", "product")


def test_a4_delivery_note_gets_company_name_total_and_category():
    r = analyze(DELIVERY_NOTE)
    assert r["merchant"] == "CONG TY TNHH TM DV XNK THUC.PHAM SACH"
    assert r["total_amount"] == 372477
    assert r["category"] == "groceries"


def test_document_code_and_status_label_are_not_the_merchant():
    from parsing.extract import extract_merchant

    lines = ["12:12 1", "PN007415 Q", "Da nhap hang", "27/09/2026 11:42", "x", "Phi - Banh mi khong"]
    assert extract_merchant(lines, [0.93, 0.96, 0.87, 0.98, 0.9, 0.94]) == "Phi - Banh mi khong"
    # tên thật vẫn thắng khi đứng đầu như bình thường
    assert extract_merchant(["Kisnac", "PN007415"], [0.9, 0.9]) == "Kisnac"


def test_category_from_the_products_on_the_bill():
    dining = classify_category_detailed("ABC", ["Bun thit nuong (hop)", "Banh mi khong lon"])
    assert dining == ("dining", "product", 0.7)                       # 2 sản phẩm cùng nhóm => đủ tin
    assert classify_category_detailed("ABC", ["Gao ST25 5kg"]) == ("groceries", "product", 0.6)   # 1 sản phẩm
    assert classify_category_detailed("ABC", ["Augmentin 1G GSK", "Voltaren 100MG"])[:2] == ("healthcare", "product")
    assert classify_category_detailed("ABC", ["Xoi gac", "Gao 5kg"]) == ("other", "none", 0.0)   # 1 dining : 1 groceries, hòa => không đoán
    assert classify_category_detailed("ABC", ["Thanh pho Tu Son", "Quan 5"]) == ("other", "none", 0.0)


def test_ambiguous_words_in_names_and_addresses_are_not_products():
    # "Duong" (tên người/đường phố), "Lau 3" (lầu), "chao" (xin chào) không phải sản phẩm; mỗi dòng thử riêng để không dựa vào hòa
    for line in ("Duong Nguyen Hue", "Lau 3", "Xin chao quy khach", "So 10 muoi"):
        assert classify_category_detailed("ABC", [line]) == ("other", "none", 0.0), line


def test_the_merchant_line_itself_is_not_product_evidence():
    assert classify_category_detailed("Banh mi Hoa", ["Banh mi Hoa", "Tong 20.000"])[1] == "keyword"
    assert classify_category_detailed("Gao Lan", ["Gao Lan", "Tong 20.000"]) == ("other", "none", 0.0)


def test_lamduong_cleaning_service_stays_unclassified_for_the_user_to_choose():
    r = analyze(LAMDUONG)
    assert (r["category"], r["category_source"]) == ("other", "none")
    assert r["needs_review"] is True


def test_pos_import_screenshot_gets_supplier_name_and_category_from_products():
    r = analyze(POS_IMPORT)
    assert r["merchant"] == "Phi - Bánh mi khöng"
    assert r["total_amount"] == 190000
    assert r["category"] == "dining"


def test_document_code_is_never_read_as_money():
    from parsing.extract import extract_total

    # "PHIEU THANH TOAN" ở tiêu đề + dòng "So CT: OV106696607150588" ngay dưới
    lines = ["PHIEU THANH TOAN", "So CT: OV106696607150588 -16/07/2026", "Phai thanh toan:", "17.447", "Tien chuyen khoan", "17.000"]
    assert extract_total(lines) == 17447
    assert extract_total(["Tong cong 106696607150588"]) is None   # quá lớn để là tiền hóa đơn


def test_label_only_reads_a_following_line_that_is_just_a_number():
    from parsing.extract import extract_total

    assert extract_total(["Tong cong", "Cam on quy khach 99.000 da mua"]) is None   # dòng sau là câu chữ
    assert extract_total(["Tong cong", "125.000 d"]) == 125000                      # dòng sau là số tiền


def test_title_words_are_stripped_from_a_brand_line():
    from parsing.extract import extract_merchant

    assert extract_merchant(["PHIEU THANH TOAN BACHHóA XANH"], [0.89]) == "BACHHóA XANH"
    assert extract_merchant(["Kisnac"], [0.9]) == "Kisnac"


def test_bach_hoa_xanh_bill_total_and_name():
    r = analyze(BACHHOAXANH)
    assert r["total_amount"] == 17447
    assert r["merchant"] == "BACHHóA XANH"
    assert (r["category"], r["category_source"]) == ("groceries", "brand")


def test_descriptor_line_below_the_receipt_title_is_not_the_merchant():
    from parsing.extract import extract_merchant

    lines = ["co.cpmart", "tmcmoin", "Chi Minh", "PHIEU TINH TIEN", "Don hang sieu thi"]
    assert extract_merchant(lines, [0.86, 0.62, 0.83, 0.94, 0.95]) == "co.cpmart"


def test_brand_tolerates_one_dropped_letter_but_not_lookalike_words():
    assert classify_category_detailed("co.cpmart", []) == ("groceries", "brand", 0.8)
    assert classify_category_detailed("Winter Market", [])[1] != "brand"
    assert classify_category_detailed("Coco Mart", [])[1] != "brand"      # khác 2 chữ so với "coopmart"
    assert classify_category_detailed("Co Mart", [])[1] != "brand"


def test_coopmart_bill_gets_logo_name_and_groceries():
    r = analyze(COOPMART)
    assert r["merchant"] == "co.cpmart"
    assert r["total_amount"] == 171200
    assert (r["category"], r["category_source"]) == ("groceries", "brand")


def test_known_brand_line_beats_background_text_like_a_keyboard():
    from parsing.extract import extract_merchant

    # bàn phím trong ảnh bị OCR đọc thành chữ rõ nét ở đầu ("Pause Break")
    assert extract_merchant(["FA", "Paus", "Braak", "WinMart"], [0.5, 0.97, 0.89, 0.96]) == "WinMart"


def test_winmart_bill_gets_brand_name_total_and_category():
    r = analyze(WINMART)
    assert r["merchant"] == "WinMart"
    assert r["total_amount"] == 187769
    assert (r["category"], r["category_source"]) == ("groceries", "brand")


def test_merchant_skips_low_confidence_handwriting_and_prefers_website_name():
    from parsing.extract import extract_merchant

    lines = ["gh", "Magi", "FPTRetail", "www.nhathuoclongchau.com"]
    assert extract_merchant(lines, [0.67, 0.62, 0.96, 0.99]) == "nhathuoclongchau"
    assert extract_merchant(["gh", "Highlands"], [0.6, 0.9]) == "Highlands"
    assert extract_merchant(["Cua hang:", "KISNAC"], [0.9, 0.9]) == "KISNAC"  # bỏ dòng nhãn kết thúc bằng ':'


def test_business_word_line_beats_logo_line():
    from parsing.extract import extract_merchant

    assert extract_merchant(["FPTRetail", "NHA THUOC LONG CHAU"], [0.9, 0.9]) == "NHA THUOC LONG CHAU"


def test_pharmacy_bill_with_website_is_healthcare():
    r = analyze(LONGCHAU5)
    assert r["merchant"] == "nhathuoclongchau"
    assert r["category"] == "healthcare"
    assert r["total_amount"] == 1920500


def test_pharmacy_bill_without_name_is_guessed_from_one_drug_but_still_flagged():
    r = analyze(LONGCHAU6)  # OCR không đọc được tên lẫn website; chỉ còn một dòng thuốc (nước muối sinh lý)
    assert r["total_amount"] == 52000
    assert (r["category"], r["category_source"], r["category_confidence"]) == ("healthcare", "product", 0.6)
    assert r["needs_review"] is True      # một dòng bằng chứng chưa đủ để bỏ qua bước xác nhận


def test_remembered_alias_fixes_garbled_logo_name():
    r = analyze(LONGCHAU6, lookup=lambda m: "healthcare" if m == "FPTRvtail" else None)
    assert (r["category"], r["category_source"]) == ("healthcare", "memory")


def test_thanh_pho_in_address_does_not_make_a_bill_dining():
    # "Thành phố" bỏ dấu thành "thanh pho", không được khớp từ khóa "phở"
    assert classify_category_detailed("LAM DUONG", ["Thanh pho Tu Son"]) == ("other", "none", 0.0)
    assert classify_category_detailed("Phở Bò Gia Truyền", [])[0:2] == ("dining", "keyword")
    assert classify_category_detailed("Phở 24", [])[0:2] == ("dining", "brand")  # đã có trong bảng thương hiệu


def test_amount_payable_wins_over_subtotal():
    from parsing.extract import extract_total

    assert extract_total(["Tong tien hang 570.500", "Tong can thanh toan 530.100"]) == 530100
    assert extract_total(["Cong tien hang 125.000", "Khach phai tra", "130.000"]) == 130000


def test_lamduong_bill_is_not_dining_and_total_is_130000():
    r = analyze(LAMDUONG)
    assert r["total_amount"] == 130000
    assert r["category"] != "dining"
    assert r["needs_review"] is True  # danh mục chưa rõ nên phải hỏi người dùng


def test_total_fallback_uses_largest_repeated_amount_when_label_is_garbled():
    from parsing.extract import extract_total

    lines = ["ry tienhang", "570,500", "530.100", "570,500", "VISA570,500", "13,800", "13,800"]
    assert extract_total(lines) == 570500
    assert extract_total(["Ly cafe 35.000", "Banh 20.000"]) is None  # không lặp thì không đoán


def test_keyword_total_still_wins_over_repeated_amounts():
    from parsing.extract import extract_total

    assert extract_total(["TONG CONG 125.000", "Item 50.000", "Item 50.000"]) == 125000


def test_dark_kisnac_bill_finds_total_but_is_flagged():
    r = analyze(KISNAC_DARK)
    assert r["total_amount"] == 570500
    assert r["category"] == "groceries"
    assert r["needs_review"] is True


def test_memory_beats_brand():
    assert classify_category_detailed("Kisnac", [], remembered="dining") == ("dining", "memory", 1.0)


def test_brand_table_maps_convenience_store_to_groceries():
    assert classify_category_detailed("KISNAC", []) == ("groceries", "brand", 0.8)
    assert classify_category_detailed("Circle K", [])[0:2] == ("groceries", "brand")


def test_keyword_and_none_sources():
    assert classify_category_detailed("Quan Cafe Vuon Xanh", []) == ("dining", "keyword", 0.75)
    assert classify_category_detailed("HIGHLANDS COFFEE", []) == ("dining", "brand", 0.8)  # chuỗi đã có trong bảng thương hiệu
    assert classify_category_detailed("QWERTY", []) == ("other", "none", 0.0)


def test_coverage_low_for_damaged_bill_and_full_for_clean_bill():
    assert compute_coverage(KISNAC) < 0.7
    assert compute_coverage(HIGHLANDS) == 1.0
    assert compute_coverage([]) == 0.0


def test_kisnac_bill_is_groceries_but_flagged_for_review():
    r = analyze(KISNAC)
    assert r["total_amount"] == 570500
    assert r["category"] == "groceries"
    assert r["category_source"] == "brand"
    assert r["coverage"] < 0.7
    assert r["needs_review"] is True


def test_clean_bill_needs_no_review():
    r = analyze(HIGHLANDS)
    assert r["category"] == "dining"
    assert r["coverage"] == 1.0
    assert r["needs_review"] is False


def test_unknown_category_flags_review_even_with_total():
    r = analyze([("QWERTY", 0.95), ("TONG CONG 125.000", 0.95)])
    assert r["category"] == "other"
    assert r["category_confidence"] == 0.0
    assert r["needs_review"] is True


def test_lookup_overrides_category_with_memory_source():
    r = analyze(
        [("QWERTY", 0.95), ("TONG CONG 125.000", 0.95)],
        lookup=lambda merchant: "shopping" if merchant == "QWERTY" else None,
    )
    assert (r["category"], r["category_source"], r["category_confidence"]) == ("shopping", "memory", 1.0)
    assert r["needs_review"] is False
