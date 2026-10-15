import sys, pathlib
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))

from merchant_name import format_merchant_name
from extract import _amounts, extract_total, extract_merchant, classify_category_detailed, analyze, items_cross_check


def classify_category(merchant, lines):
    return classify_category_detailed(merchant, lines)[0]


def test_parse_amount_vietnamese_formats():
    first = lambda text: (_amounts(text) or [None])[0]
    assert first("1.250.000") == 1250000
    assert first("125,000") == 125000
    assert first("125000đ") == 125000
    assert first("1.250.000,00") == 1250000
    assert first("khong co so") is None


def test_extract_total_same_line_no_diacritics():
    assert extract_total(["A", "TONG CONG 125.000"]) == 125000


def test_extract_total_with_diacritics_next_line_picks_largest():
    assert extract_total(["Tạm tính 100.000", "TỔNG CỘNG", "125.000"]) == 125000


def test_extract_total_none_without_keyword():
    assert extract_total(["Highlands", "Ly cafe 35.000"]) is None


def test_extract_merchant_skips_noise_lines():
    assert extract_merchant(["***", "HIGHLANDS COFFEE", "Ngày 29/09"]) == "HIGHLANDS COFFEE"
    assert extract_merchant(["123", "456"]) is None


def test_classify_category():
    assert classify_category("HIGHLANDS COFFEE", []) == "dining"
    assert classify_category("PETROLIMEX", []) == "transport"
    assert classify_category("CO.OPMART", []) == "groceries"
    assert classify_category("ABC", []) == "other"


def test_analyze_confident_bill():
    r = analyze([("HIGHLANDS COFFEE", 0.9), ("TONG CONG 125.000", 0.9)])
    assert r["merchant"] == "HIGHLANDS COFFEE"
    assert r["total_amount"] == 125000
    assert r["category"] == "dining"
    assert r["needs_review"] is False
    assert r["raw_lines"] == ["HIGHLANDS COFFEE", "TONG CONG 125.000"]


def test_analyze_needs_review_when_unclear():
    assert analyze([("xyz", 0.5)])["needs_review"] is True


def test_total_survives_ocr_cutting_the_last_digit_when_a_number_wraps_to_the_next_line():
    # Coco Mart: "1,305,000" bị cắt thành "1,305,00" + "0"; tổng không được đọc thành 1.305đ
    lines = ["Bia Tiger Crystal 330ml", "3", "1,305,000", "435,000", "1,305,00", "Tong tién hang:", "0", "Chiét khau :", "1.305.00", "Tong thanh toan:"]
    assert extract_total(lines) == 1_305_000
    assert extract_total(["Tong cong: 1,305.00"]) == 1305  # số lẻ khác dấu vẫn là số lẻ
    assert items_cross_check(lines) == {"items_sum": 1_305_000, "items_count": 1}  # SL, thành tiền, đơn giá


def test_amount_in_words_survives_ocr_gluing_words_together():
    from extract import extract_words_total
    lines = ["Mot trieu ba tram linhnamnghin", "dong chan)", "Cam on va hen gap lai!"]
    assert extract_words_total(lines) == 1_305_000
    assert extract_words_total(["Cam on quy khach", "Hen gap lai"]) is None


def test_eyeglasses_go_to_healthcare_and_sunglasses_to_shopping():
    assert classify_category_detailed("Lb eyewear", [])[0] == "healthcare"
    assert classify_category_detailed(None, ["Gong kinh can", "Trong kinh", "Tong 3,040,000"])[0] == "healthcare"
    assert classify_category_detailed(None, ["Kinh ram Rayban", "Tong 1,500,000"])[0] == "shopping"


HOSPITAL = ["Bệnh nhân bảo hiểm)", "Mabonhnhanc26074057", "BENHVIEN QUANY103", "Madahto2466519", "Khoa điều trị:", "ĐẶNG VĂN MIỀN", "Khoa Ong Tieu H6a",
            "1. S6 tiln da tam thu:", "(VP:7.050.600)", "7.050,600", "2. S tien phai thanh toan:", "-CMn dodn hinh anh",
            "- S6 tien BHYT bnh nhan cung chi tra:", "4.995.993", "3.104.945", "4. Bnh nhan con thira (1 - 2):(Tra lai)", "2.054.607"]


def test_hospital_bill_uses_the_amount_to_pay_not_the_advance_and_names_the_hospital():
    assert extract_total(HOSPITAL) == 4_995_993  # "2. Số tiền phải thanh toán", không phải tiền tạm thu 7.050.600
    r = analyze([(l, 1.0) for l in HOSPITAL])
    assert r["category"] == "healthcare" and "QUANY103" in (r["merchant"] or "").replace(" ", "")


GRAB = ["GrabFood", "GF-637", "1x Bun Bo Tai", "60.000", "[Tieu diem] ME", "-30.000", "1x Tra Lai Tac Mat Ong", "24.000", "-12.000",
        "42.000d", "Tong (tam tinh)", "Giam gia", "-4.800", "BOSSCHFL1X0E1SV", "37.200d", "Tong cdng", "GF-637"]


def test_final_total_is_after_the_discount_code_not_the_subtotal_or_the_discount():
    assert extract_total(GRAB) == 37_200  # không phải 42.000 (tạm tính) hay 4.800 (giảm giá)
    assert extract_total(["Tong (tam tinh)", "42.000d"]) == 42_000  # chỉ có tạm tính thì vẫn dùng nó
    assert _amounts("Giam gia -4.800", True) == [] and _amounts("-4.800") == [4800]  # ví điện tử in giao dịch "-125.000đ": chỉ bỏ số âm khi tìm dòng Tổng


def test_a_lone_venue_word_is_not_a_merchant_name():
    assert extract_merchant(["COFFEE", "HÓA ĐƠN BÁN HÀNG", "Tong: 1,286,000"]) is None
    assert extract_merchant(["HIGHLANDS COFFEE", "Hoa Don Thanh Toan"]) == "HIGHLANDS COFFEE"


def test_wifi_password_line_does_not_make_a_cafe_receipt_an_internet_bill():
    lines = ["COFFEE", "Cafe PHUONG HOANG LUA", "Da xay tra xanh", "Tong: 1,286,000", "Pass Wifi: 12341234"]
    assert classify_category_detailed(None, lines)[0] == "dining"


def test_service_mode_label_is_not_a_merchant_name():
    lines = ["In Store", "Pager: 07", "Check:200497", "Hoa Don Thanh Toan", "Phin Sua Da L", "45.000", "Tong tien:", "16.000"]
    assert extract_merchant(lines) is None
    assert extract_merchant(["In Store", "customerservice@highlandscoffee.com.vn", "16.000"]) == "highlands coffee"


def test_greeting_after_the_name_is_not_part_of_the_merchant():
    # bill Ngô Gia: "♥ NGÔ GIA XIN CHÀO ♥" bị đọc thành một từ dính liền kèm chữ V thay trái tim
    lines = ["0777587568", "NGOGIAXINCHAOV", "Phiếu thanh toán", "SphiéuBH00037", "19,000", "Tổng thanh toán", "22,000đ"]
    assert extract_merchant(lines) == "NGOGIA"
    assert extract_merchant(["NGÔ GIA XIN CHÀO", "Phiếu thanh toán", "22,000đ"]) == "NGÔ GIA"
    assert extract_merchant(["XIN CHÀO QUÝ KHÁCH", "Hóa đơn", "22,000đ"]) is None  # chỉ có lời chào thì để app hỏi
    assert format_merchant_name("NGOGIA") == "Hồng Trà Ngô Gia"


def test_household_consumables_are_shopping():
    # đồ dùng tiêu hao là shopping (quy tắc cũ xếp vào groceries), đi qua đường từ khóa sản phẩm
    for item in ("Bot giat Omo 3kg", "Nuoc rua chen Sunlight", "Giay ve sinh Pulppy", "Nuoc xa vai Comfort"):
        assert classify_category_detailed("Cong ty ABC", [item, "50.000"])[:2] == ("shopping", "product"), item


def test_food_staples_stay_groceries():
    for item in ("Gao ST25 5kg", "Sua tuoi Vinamilk 1L"):
        assert classify_category_detailed("Cong ty ABC", [item, "50.000"])[:2] == ("groceries", "product"), item


# Hóa đơn cửa hàng tiện lợi (đã bỏ dòng địa chỉ/MST): OCR đọc số tiền bên phải TRƯỚC nhãn bên trái trên cả khối tổng tiền.
# Tên hàng "TraYuzumatongboncha" / "...MatOng..." (mật ong) chứa chuỗi "tong".
CONVENIENCE = ["CÔNG TY TNHH MINISTOP VIỆT NAM", "Hoa don Ban hang", "12:31:13", "11/10/2026",
               "19,000", "19,000", "14,000*", "Khan uot ALLCARE 20", "14,000", "BonchaTacMatOng450", "14,000*", "14,000",
               "Boncha Viet Quat 45", "14,000*", "14,000", "TraYuzumatongboncha",
               "61,000", "Tam tinh:", "-14,000", "Khuyen mai:", "47,000", "Tong cong (gom thue):", "50,000", "Tien mat",
               "-3,000", "(Tien mat)Tien thua:", "Ministop xin cam on"]


def test_subtotal_is_not_the_total_when_a_payable_label_follows():
    # "tong" nằm giữa tên hàng ("matong" = mật ong) không phải nhãn Tổng: không được lấy 61.000 (tạm tính) ở dòng kế
    assert extract_total(CONVENIENCE) == 47_000
    r = analyze([(l, 0.95) for l in CONVENIENCE])
    assert r["total_amount"] == 47_000 and r["merchant"] == "CÔNG TY TNHH MINISTOP VIỆT NAM" and r["category"] == "groceries"


def test_amount_emitted_before_its_label_belongs_to_that_label():
    # "50,000" đứng sau "Tong cong" nhưng ngay trước nhãn "Tien mat": đó là tiền khách đưa, không phải tổng
    block = ["61,000", "Tam tinh:", "-14,000", "Khuyen mai:", "47,000", "Tong cong (gom thue):", "50,000", "Tien mat",
             "-3,000", "(Tien mat)Tien thua:", "Cam on quy khach"]
    assert extract_total(block) == 47_000


def test_column_split_labels_first_then_amounts_still_resolves():
    # OCR tách cột: cả khối nhãn rồi mới tới khối số tiền
    assert extract_total(["Tam tinh:", "Tong cong:", "100,000", "125,000"]) == 125_000
    # nhãn không có số ngay trước: số sau nhãn vẫn là của nhãn dù kế tiếp là dòng chữ cuối phiếu
    assert extract_total(["Ca phe", "Tong cong:", "125,000", "Cam on quy khach"]) == 125_000
    assert extract_total(["Tong cong:", "So luong 2", "125,000", "Cam on quy khach"]) == 125_000


def test_cash_and_change_are_never_the_total():
    # hai món, OCR đọc số bên phải trước nhãn: 100.000 (khách đưa) và 46.000 (tiền thừa) nằm sau "Tong cong" nhưng là của nhãn kế
    lines = ["Banh mi", "25,000", "Ca phe", "29,000", "54,000", "Tong cong", "100,000", "Tien khach dua", "46,000", "Tien thua"]
    assert extract_total(lines) == 54_000
    assert extract_total(["Banh mi", "25,000", "Ca phe", "29,000", "54,000", "Tong cong", "100,000", "Tien mat", "-46,000", "Tien thoi"]) == 54_000
    # nhãn dính chữ "Khachphai tra" (ảnh thật 960345d1) vẫn là nhãn phải trả; "Tien khach dua" sau đó không thay thế nó
    glued = ["Cong tien hang", "125,000", "Chiet khau", "0", "Khachphai tra", "130,000", "Tien khach dua", "0", "Tralai", "0"]
    assert extract_total(glued) == 130_000


def test_subtotal_only_still_falls_back_and_single_line_total_unchanged():
    assert extract_total(["Tong (tam tinh)", "42.000d"]) == 42_000
    assert extract_total(["Banh mi", "Tong cong 125.000"]) == 125_000
    assert extract_total(["Tra mat ong", "Tong cong", "35.000"]) == 35_000  # "Tong" là chữ đầu từ vẫn là nhãn


def test_amount_after_the_total_label_is_kept_when_followed_by_ordinary_text():
    # bố cục nhãn-trước-số phổ biến: số sau nhãn Tổng là tổng dù dòng kế là chữ cuối phiếu, ngày, ghi chú VAT hay nhãn phí
    for tail in ("Cam on quy khach", "Ngay 11/10/2026", "Da bao gom VAT"):
        assert extract_total(["Banh mi", "25,000", "Tong cong", "54,000", tail]) == 54_000, tail
    assert extract_total(["Tong tien hang", "60,000", "Phi dich vu", "6,000", "Khach phai tra", "66,000", "Cam on quy khach"]) == 66_000
    assert extract_total(["Tam tinh", "54,000", "Phi giao hang", "15,000", "Can thanh toan", "69,000", "Hotline 1900"]) == 69_000


def test_total_label_with_one_glued_noise_character_still_matches():
    for label in ("ITong tien:", "lTONG TIEN", "ITổng:", "Ttong tien", "BTong tien hang", "|Tong", "_Tong", "1.Tong", "(Tong)",
                  "*Tong", "TổngTiền", "Tongcong", "TÔNG CỘNG"):
        assert extract_total(["Banh mi", label, "35,000"]) == 35_000, label
    # tên hàng chứa "tong" giữa chữ không phải nhãn
    for name in ("TraYuzumatongboncha", "BonchaTacMatOng450", "Banh thanhtong"):
        assert extract_total([name, "35,000"]) is None, name


def test_payable_total_not_taken_from_the_cash_tendered_line_below_it():
    # ảnh thật Bách Hóa Xanh: số tiền nằm dòng dưới nhãn; tìm số theo nhãn không được đi qua nhãn kế ("Tiền khách đưa") lấy luôn 200.000
    lines = ["Tong tien hang (3 mon):", "186.000", "Chiet khau:", "0", "Tong thanh toan:", "186.000", "Tien khach dua:", "200.000", "Tien thua:", "14.000"]
    assert extract_total(lines) == 186000


def test_label_value_still_found_across_a_quantity_line_before_the_next_label():
    # nhãn tách khỏi số bởi một dòng chữ KHÔNG phải nhãn khác: giữ hành vi cũ (lấy số lớn nhất trước khi gặp nhãn mới)
    assert extract_total(["Tong cong", "So luong", "125.000"]) == 125000


# ---- ảnh thật 2026-10-14 (Nhà sách Hướng Dương): thương hiệu trong TÊN HÀNG ("Bút bi Thiên Long") không được thành tên cửa hàng
NHA_SACH = ["NHÀ SÁCH", "HƯỞNG DƯƠNG", "TRI THỨC ĐỒNG HÀNH CÙNG BẠN", "NHÀ SÁCH HƯỚNG DƯƠNG", "S6 123 Nguyén Van Cu, P. An Binh", "MST: [so-dai]",
            "Ngay: 26/04/2025", "Gi: 16:42", "SÓ HD: HD[so-dai]", "POS: 02", "Ca: 1", "STT", "Tén hang", "SL", "Don giá", "Thanh tién", "1",
            "V ké ngang 200 trang", "3", "12.000", "36.000", "Vd Hong Ha 200 trang", "2", "But bi xanh", "2", "5.000", "10.000",
            "But bi Thién Long TL-027", "3", "Bia nut A4"]


def test_brand_inside_an_item_line_is_not_the_merchant():
    name = extract_merchant(NHA_SACH)
    assert name and "nhasach" in _alnum_for_test(name)
    assert "thienlong" not in _alnum_for_test(name)


def test_brand_in_the_header_zone_still_wins_over_later_lines():
    lines = ["Highlands Coffee", "Số 12 Lê Lợi", "STT", "Tên hàng", "Cà phê sữa 35.000", "Tổng cộng 35.000"]
    assert "highlands" in _alnum_for_test(extract_merchant(lines))


def _alnum_for_test(text):
    import unicodedata
    folded = "".join(c for c in unicodedata.normalize("NFD", text) if unicodedata.category(c) != "Mn").lower().replace("đ", "d")
    return "".join(ch for ch in folded if ch.isalnum())


def test_two_line_logo_name_is_joined_even_when_the_second_line_contains_duong():
    # "HƯỞNG DƯƠNG" là tên riêng; "duong" chỉ là địa chỉ khi là chữ đầu dòng ("Đường Lê Lợi")
    assert _alnum_for_test(extract_merchant(NHA_SACH)) == "nhasachhuongduong"


def test_descriptor_line_and_brand_line_are_joined_for_an_agent_name():
    lines = ["ĐẠI LÝ NƯỚC TINH KHIẾT", "HÁI NAM", "CHUYEN CUNG CAP NUóC UoNG DóNG BINH, DóNG CHAI", "GIAO HÀNG TẬN NƠI", "Ngay: 26/04/2025", "Gi: 16:18", "STT"]
    assert _alnum_for_test(extract_merchant(lines)) == "dailynuoctinhkhiethainam"


def test_a_street_line_is_still_not_joined_to_the_name():
    lines = ["SIEU THI MINH ANH", "DUONG LE LOI", "Ngay: 26/04/2025", "STT"]
    assert _alnum_for_test(extract_merchant(lines)) == "sieuthiminhanh"


# ---- ảnh thật 2026-10-14: danh mục
def test_amusement_park_header_giai_tri_is_entertainment_even_when_the_name_line_is_the_company():
    lines = ["KHU GIẢI TRÍ", "HAPPY", "LZONE", "CÔNG TY TNHH HAPPY ZONE VIỆT NAM", "KHU GIẢI TRÍ HAPPY ZONE", "Thé game nap 100.000d", "Bap rang mini", "Nuóc ngot lon"]
    category, source, _ = classify_category_detailed("CÔNG TY TNHH HAPPY ZONE VIỆT NAM", lines)
    assert category == "entertainment"


def test_drinking_water_agent_is_groceries_not_a_water_utility_bill():
    lines = ["ĐẠI LÝ NƯỚC TINH KHIẾT", "HÁI NAM", "CHUYEN CUNG CAP NUóC UoNG DóNG BINH, DóNG CHAI", "Nudc tinh khiét 20L", "Nuóc sui chai 500ml"]
    category, source, _ = classify_category_detailed("ĐẠI LÝ NƯỚC TINH KHIẾT HÁI NAM", lines)
    assert category == "groceries"


def test_a_real_water_utility_bill_is_still_housing():
    assert classify_category_detailed("CÔNG TY CẤP NƯỚC SÀI GÒN", ["Tien nuoc thang 9", "Tong cong 120.000"])[0] == "housing"


# ---- tự phát hiện lỗi của chính nó: tên cửa hàng đọc nát phải nhắc người dùng kiểm tra
GIA_DINH_XANH = ["SIEU TH!", "GIA ĐÌNH XANH", "TUOI NGON MI NGAY - Vi GIA iNH BAN", "CONG TY TNHH THUONG MAI GIA iNH XANH", "MST: [sdt]",
                 "Ngay: 24/04/2025", "STT Tén hang", "SL", "Don gia", "Thanh tién", "Gao thom 5kg", "1", "125.000", "125.000",
                 "Tong tién hang (1 món):", "125.000", "Tong thanh toan:", "125.000", "Tién khach dua:", "200.000", "CAM ON QUY KHACH"]


def test_merchant_name_with_a_stray_lowercase_letter_inside_caps_asks_for_review():
    # "GIA iNH XANH": chữ "Đ" bị đọc mất, còn "iNH" lẫn hoa thường giữa dòng IN HOA: tên chắc chắn sai, không được im lặng
    result = analyze([(t, 0.95) for t in GIA_DINH_XANH])
    assert result["needs_review"] is True


def test_a_clean_all_caps_name_is_not_flagged_by_the_garbled_name_rule():
    from extract import looks_garbled_name
    assert looks_garbled_name("CONG TY TNHH THUONG MAI GIA iNH XANH") is True
    for ok in ("QUÁN CƠM NHA BEP", "Bách Hóa Xanh", "MiniStop", "Highlands Coffee", "GS25", "iPhone Store", "McDonald's"):
        assert looks_garbled_name(ok) is False, ok


# ---- tên logo đọc nát: dòng logo ĐÚNG ("GIA ĐÌNH XANH") có sẵn nhưng bị dòng công ty ("... GIA iNH XANH") chiếm chỗ; website in cuối bill xác nhận dòng nào là tên
GIA_DINH_XANH_FULL = ["SIEU TH!", "GIA ĐÌNH XANH", "TUOI NGON MI NGAY - Vi GIA iNH BAN", "CONG TY TNHH THUONG MAI GIA iNH XANH", "MST: [sdt]", "TP. H Chi Minh",
                      "Ngay: 24/04/2025", "STT Tén hang", "SL", "Don gia", "Thanh tién", "Gao thom 5kg", "1", "125.000", "125.000",
                      "Tong thanh toan:", "125.000", "CAM ON QUY KHACH DA MUA SAM!", "Website: www.giadinhxanh.vn"]


def test_header_line_that_matches_the_website_domain_is_the_merchant_name():
    name = extract_merchant(GIA_DINH_XANH_FULL)
    assert _alnum_for_test(name) == "giadinhxanh"


def test_company_line_still_wins_when_no_header_line_matches_the_website():
    lines = ["CÔNG TY TNHH ABC VIỆT NAM", "Số 1 Lê Lợi", "STT", "Tên hàng", "Website: www.xyzxyzxyz.vn"]
    assert "abc" in _alnum_for_test(extract_merchant(lines))


def test_logo_word_read_with_o_instead_of_q_is_repaired_at_the_start_of_a_word():
    lines = ["QUÁN CƠM", "NHA BEP OUE", "COM NGON NHU d NHA", "TP. H6 Chi Minh", "Ngay: 26/04/2025", "STT"]
    assert _alnum_for_test(extract_merchant(lines)) == "quancomnhabepque"


def test_real_words_starting_with_ou_are_not_touched_outside_the_ou_vowel_pattern():
    assert _alnum_for_test(extract_merchant(["OUTLET SHOP 88", "Số 1 Lê Lợi", "STT"])) == "outletshop88"


# Bill "BÁCH HÓA MINI" / "AN KHANG": logo hai dòng, dòng thứ hai trùng tên chuỗi nhà thuốc An Khang. Các dòng là chữ OCR thật của ảnh.
_BACH_HOA_AN_KHANG = ["BÁCH HÓA MINI", "AN KHANG", "T[sdt]", "HÓA ĐƠN BÁN HÀNG", "S6HD[so-dai]", "Ngay26/04/202517:05", "STT", "Ten hang",
                      "SLongia Thanhtien", "1Soa tuoi Vinamilk", "2", "32.000", "64.000", "3Rau muong 500g", "1", "12.000", "12.000",
                      "Tong cong:", "543.000", "Thanh toan the", "543.000", "Cam on quy khach"]


def test_two_line_logo_keeps_the_store_type_line_above_a_known_brand_name():
    assert extract_merchant(_BACH_HOA_AN_KHANG) == "BÁCH HÓA MINI AN KHANG"


def test_a_bach_hoa_store_is_not_a_pharmacy_just_because_its_name_matches_a_pharmacy_chain():
    assert classify_category_detailed("BÁCH HÓA MINI AN KHANG", _BACH_HOA_AN_KHANG)[0] == "groceries"


def test_the_pharmacy_chain_alone_is_still_a_pharmacy():
    assert classify_category_detailed("NHÀ THUỐC AN KHANG", ["NHÀ THUỐC AN KHANG", "Paracetamol 2 24.000"])[:2] == ("healthcare", "brand")
    assert classify_category_detailed("AN KHANG", ["AN KHANG", "Tong cong 24.000"])[:2] == ("healthcare", "brand")


def test_display_name_of_a_bach_hoa_store_keeps_its_own_name_not_the_pharmacy_chain_name():
    assert format_merchant_name("BÁCH HÓA MINI AN KHANG") == "Bách Hóa Mini An Khang"
    assert format_merchant_name("NHÀ THUỐC AN KHANG") == "Nhà thuốc An Khang"


# Bill quán lẩu: logo "LẦU THIÊN ĐƯỜNG" bị coi là tên người ("Thiên" bắt đầu bằng "thi") nên tên cửa hàng thành dòng địa chỉ.
_QUAN_LAU = ["633", "LẦU THIÊN ĐƯỜNG", "99 Hoang Van ThuOuan Ph NhunTP.HCH", "0T[sdt]", "HOA DON THANH TOAN", "S6HD[so-dai]",
             "Ten mon", "SL", "Don gia", "Thanh tien", "Láu thai hai san", "1", "380.000", "380.000", "Tong cong:", "791.000"]


def test_a_store_name_with_thien_or_thinh_is_not_taken_for_a_person():
    from extract import _looks_like_person
    for name in ("LẦU THIÊN ĐƯỜNG", "LẨU THIÊN HƯƠNG", "QUÁN THỊNH VƯỢNG", "BÁNH THIỆN PHÚC"):
        assert _looks_like_person(name) is False, name


def test_vietnamese_personal_names_with_thi_are_still_recognised():
    from extract import _looks_like_person
    for name in ("NGUYEN THI HOA", "Hồ Thị Hoàng Nga", "LE THI THU HA", "TRAN VAN BINH"):
        assert _looks_like_person(name) is True, name


def test_the_logo_beats_the_address_as_merchant_name():
    confs = [0.61, 0.93] + [0.9] * (len(_QUAN_LAU) - 2)
    assert extract_merchant(_QUAN_LAU, confs) == "LẦU THIÊN ĐƯỜNG"


# Bộ 15 ảnh thử nghiệm: biên lai chỉ in tiêu đề (không in tên cửa hàng) và hóa đơn điện ghi EVN ngay trong tiêu đề.
def test_a_receipt_title_is_not_a_merchant_name():
    lines = ["BIÊN LAI THU TIỀN PHÒNG TRỌ", "Tiền phòng:", "2.500.000", "Tiền điện 98 số x 3.500:", "343.000", "TỔNG CỘNG:", "3.003.000"]
    assert extract_merchant(lines) is None  # không có tên để lấy: app phải hỏi, không được lấy tiêu đề làm tên cửa hàng


def test_evn_in_the_bill_title_is_the_merchant_and_an_electricity_bill_is_housing():
    from merchant_name import format_merchant_name

    lines = ["HÓA ĐƠN TIỀN ĐIỆN EVN", "Kỳ hóa đơn:", "tháng 09/2026", "Điện năng tiêu thụ:", "285 kWh", "Tiền điện:", "585.000", "TỔNG CỘNG:", "631.800"]
    merchant = extract_merchant(lines)
    assert merchant == "EVN"
    assert format_merchant_name(merchant) == "EVN"
    assert classify_category_detailed(merchant, lines)[0] == "housing"


def test_a_boarding_house_receipt_with_a_garbled_water_line_is_still_housing():
    # OCR thật: "Tién nu6c" không khớp "tien nuoc"; "Phí rác" (luật 8: rác là nhà ở) phải đủ để thắng "wifi" (hóa đơn dịch vụ)
    lines = ["BIÊN LAI THU TIỀN PHÒNG TRỌ", "Tién phng:", "2.500.000", "Tién dién 98 s0 x 3.500", "343.000", "Tién nu6c 4 kh0i x 15.000:", "60.000",
             "Phí rác và wifi:", "100.000", "TONG CONG:", "3.003.000"]
    assert classify_category_detailed(None, lines)[0] == "housing"
