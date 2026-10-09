"""Lỗi thấy trên bộ ảnh thật data-bill (đầu phiếu rút gọn từ kết quả OCR)."""
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))

from extract import _alnum, _norm, analyze, classify_category_detailed, extract_merchant  # noqa: E402


def _m(*lines):
    return extract_merchant(list(lines))


def test_name_after_a_buyer_label_line_is_not_the_merchant():
    assert _m("Khach:", "CH CoCo mart", "Dia chi Masteri toa B vinhome Grand Park") is None


def test_city_fragment_is_not_the_merchant():
    assert _m("Ninh KieuTP.Can Tho", "Hotline:18006928", "PHIEU THU") is None


def test_garbled_delivery_title_and_garbled_address_are_not_the_merchant():
    assert _m("PHXEU GIAO NHANVA THANM TOAN", "Dja ChiE1.0007Masteri Centro Polnt.VinhomesLong Binh") is None


def test_person_name_line_is_not_the_merchant():
    assert _m("HO THIHOANG NGA", "InNGUYEN THI THUONG") is None
    assert _m("SIEU THI DUC THANH") == "SIEU THI DUC THANH"


def _cat(*body, merchant=None):
    return analyze([(t, 0.95) for t in body])["category"]


def test_pizza_restaurant_slip_is_dining_even_with_a_beef_topping_line():
    assert _cat("Ten mon", "Quattro Formaggi", "Pizza1/2", "Pizza bon loai", "Pizza thit bo,", "Carbonara(My y", "Tong thanh toan") == "dining"


def test_vietnamese_snack_shop_slip_is_dining():
    assert _cat("Tra da(ca)", "Cha ca (Suat)", "Long ca (Suat)", "Nem ca(dia)", "Tong cong:") == "dining"


def test_seasoning_products_are_groceries():
    assert _cat("Bot Chien Gion Aji-Quick 150g", "Hat nem Heo Aji-ngon 400g", "Xot Mayonnaise Vi Nguyen Ban",
                "Bot ngot AJI-NO-MOTO 230g") == "groceries"


from extract import words_to_amount  # noqa: E402


def test_amount_in_words():
    assert words_to_amount("Tam tram bay moi ba nghin dong chan") == 873000
    assert words_to_amount("Mot trieu khong tram sau muoi nghin chin tram nam muoi ba dong") == 1060953
    assert words_to_amount("Hai muoi lam nghin dong") == 25000
    assert words_to_amount("Mot ti hay tranmuthai nghin dng") is None  # chữ hỏng thì không đoán


def test_words_total_from_lines_and_mismatch_is_flagged():
    base = ["Cong ty ABC", "Tong cong thanh toan", "873,000", "Bang chu: Tam tram bay muoi ba nghin dong"]
    ok = analyze([(t, 0.95) for t in base])
    assert ok["total_amount"] == 873000 and ok["words_check"] == "match"
    bad = analyze([(t, 0.95) for t in base[:2] + ["578,000"] + base[3:]])
    assert bad["words_check"] == "mismatch" and bad["needs_review"]


def test_added_chains_give_category_and_display_name():
    from brands import BRAND_CATEGORIES, BRAND_DISPLAY
    from merchant_name import format_merchant_name
    assert set(BRAND_CATEGORIES) <= set(BRAND_DISPLAY)
    for merchant, category, display in [("PIZZA HUT VN", "dining", "Pizza Hut"), ("Phuc Long Coffee", "dining", "Phúc Long"),
                                         ("THE GIOI DI DONG", "shopping", "Thế Giới Di Động"), ("PETROLIMEX CH 12", "transport", "Petrolimex"),
                                         ("CGV Cinemas", "entertainment", None)]:
        assert classify_category_detailed(merchant, [])[0] == category
        if display:
            assert format_merchant_name(merchant) == display


def test_brand_names_are_not_matched_inside_ordinary_words():
    for name in ("Quan Com Binh Dan", "Tap hoa Minh Phat", "Cua hang Hoa Tuoi Lan", "Tiem Banh Ngot Mai", "Banh Bao Nong",
                 "Bun Bo Hue Co Ba", "Com Tam Suon Bi Cha", "Che Thai Lan", "Nuoc Mia Sieu Sach", "Passion Cafe", "Bananas Fruit",
                 "Quan Ba Mien", "Dien thoai Phat Dat", "Giat Ui Hoa Mai", "Cat Toc Nam Phuong", "Nha Hang Hai San Bien Dong"):
        assert classify_category_detailed(name, [])[1] != "brand"


def test_base_brand_table_gives_category_and_name_for_common_vn_chains():
    from brands import BRAND_CATEGORIES, BRAND_DISPLAY
    from merchant_name import format_merchant_name
    assert set(BRAND_CATEGORIES) <= set(BRAND_DISPLAY)
    for merchant, category, display in [("POPEYES TTTM", "dining", "Popeyes"), ("Canifa Nguyen Trai", "shopping", "Canifa"),
                                         ("BITI'S HUNTER STORE", "shopping", "Biti's"), ("PNJ Quan 1", "shopping", "PNJ"),
                                         ("Bao Tin Minh Chau", "shopping", "Bảo Tín Minh Châu"), ("GRABFOOD", "dining", "GrabFood"),
                                         ("MM MEGA MARKET AN PHU", "groceries", "MM Mega Market"), ("Watsons Vincom", "shopping", "Watsons"),
                                         ("Phuong Trang FUTA Bus Lines", "transport", "Phương Trang (FUTA)"), ("CINESTAR QUOC THANH", "entertainment", "Cinestar")]:
        assert classify_category_detailed(merchant, [])[0] == category, merchant
        assert format_merchant_name(merchant) == display, merchant


def test_researched_dining_chains_are_recognised_and_exited_ones_are_gone():
    from brands import BRAND_CATEGORIES
    from merchant_name import format_merchant_name
    for merchant, display in [("KFC Nguyen Hue", "KFC"), ("DOOKKI VINCOM", "Dookki"), ("Marugame Udon Saigon Centre", "Marugame Udon"),
                              ("PIZZA 4PS LE THANH TON", "Pizza 4P's"), ("Gyu-Kaku Japanese BBQ", "Gyu-Kaku"), ("foody.vn don 123", "ShopeeFood")]:
        assert classify_category_detailed(merchant, [])[:2] == ("dining", "brand"), merchant
        assert format_merchant_name(merchant) == display, merchant
    assert "baemin" not in BRAND_CATEGORIES  # rút khỏi Việt Nam 8/12/2023
    assert "cafe amazon" not in BRAND_CATEGORIES  # đóng toàn bộ cửa hàng 18/11/2025
    for merchant, display in [("NIKE", "Nike"), ("Nike Store Vincom", "Nike"), ("JUNO SHOES", "Juno"), ("Puma Store", "Puma"), ("Converse", "Converse"),
                              ("SUPERSPORTS Aeon", "Supersports"), ("Anta Sports", "Anta"), ("ASICS Vietnam", "Asics"), ("Samsonite Vincom", "Samsonite")]:
        assert classify_category_detailed(merchant, [])[:2] == ("shopping", "brand"), merchant
        assert format_merchant_name(merchant) == display, merchant
    for name in ("Nikel Coated Pipe", "Junoon Cafe", "Havana Vans Garage", "Pumaster Gym", "Filatelist Club", "Conversely Books", "Dr. Martens Outlet", "Bata Shoes Store"):
        assert classify_category_detailed(name, [])[1] != "brand", name
    for merchant, display in [("NGOC THAM JEWELRY Q5", "Ngọc Thẩm Jewelry"), ("The Gioi Kim Cuong", "Thế Giới Kim Cương"), ("Skymond Luxury Vincom", "Skymond Luxury"),
                              ("PNJ Vincom", "PNJ"), ("DOJI Tower", "DOJI")]:
        assert classify_category_detailed(merchant, [])[:2] == ("shopping", "brand"), merchant
        assert format_merchant_name(merchant) == display, merchant
    assert classify_category_detailed("TIEM VANG BAC DA QUY HUNG THINH", [])[:2] == ("shopping", "keyword")
    gold_slip = ["PHIEU MUA VANG", "Nhan tron tron 9999 1 chi", "Vang 9999 5 chi", "Tong cong 38.500.000"]
    assert classify_category_detailed(None, gold_slip)[:2] == ("shopping", "product")  # phiếu không có logo vẫn nhận ra
    for name in ("Phu Quy Restaurant", "Mi Hong Spa", "Kim Tin Que Han", "Loc Phuc Quan Com", "Ngoc Tham Hair Salon", "Bao Tin Gia Dinh"):
        assert classify_category_detailed(name, [])[1] != "brand", name
    for merchant, display in [("THE BODY SHOP Mac Thi Buoi", "The Body Shop"), ("Laneige Counter", "Laneige"), ("Kiehl's Vincom", "Kiehl's"), ("LUSH Vietnam", "Lush"),
                              ("Matsukiyo Q1", "Matsumoto Kiyoshi"), ("Shiseido", "Shiseido"), ("Clinique Counter", "Clinique")]:
        assert classify_category_detailed(merchant, [])[:2] == ("shopping", "brand"), merchant
        assert format_merchant_name(merchant) == display, merchant
    for name in ("Blush Cafe", "Plush Toys Shop", "Bodyshop Auto Repair Garage", "Clinical Lab Center", "Mac Computers"):
        assert classify_category_detailed(name, [])[1] != "brand", name
    assert classify_category_detailed(None, ["Sua rua mat CeraVe", "Kem chong nang 50ml", "Son moi 3CE", "Tong cong 850.000"])[:2] == ("shopping", "product")
    for merchant, category, display in [("SIEU THI CON CUNG", "shopping", "Con Cưng"), ("Kids Plaza Cau Giay", "shopping", "Kids Plaza"), ("MYKINGDOM Aeon", "shopping", "MyKingdom"),
                                         ("30SHINE Q3", "other", "30Shine"), ("Cheng Salon", "other", "Cheng Salon"), ("NHA KHOA PARIS", "healthcare", "Nha Khoa Paris"),
                                         ("Parkway Dental Clinic", "healthcare", "Nha Khoa Parkway"), ("Tham My Kangnam", "healthcare", "Thẩm Mỹ Kangnam")]:
        assert classify_category_detailed(merchant, [])[:2] == (category, "brand"), merchant
        assert format_merchant_name(merchant) == display, merchant
    assert classify_category_detailed("Nha Khoa An Tam", [])[:2] == ("healthcare", "keyword")        # tên lạ: nhận qua từ khóa
    assert classify_category_detailed("TIEM TOC HOA MAI", ["Cat toc nam", "Goi dau thu gian", "Tong 150.000"])[:2] == ("other", "product")
    assert classify_category_detailed(None, ["Nieng rang mat cai", "Cao voi rang", "Tong 8.500.000"])[:2] == ("healthcare", "product")
    for name in ("Con Cung Quan Com", "Kim Loai Co Khi", "Shine Auto Wash", "Kangnam Tower Office", "Be Con Cung Spa"):
        assert classify_category_detailed(name, [])[1] != "brand", name
    assert "bershka" not in BRAND_CATEGORIES  # không có cửa hàng ở VN (Inditex tại VN chỉ có Zara, Pull&Bear, Stradivarius, Massimo Dutti)
    for merchant, display in [("H&M Vincom", "H&M"), ("MUJI Aeon", "Muji"), ("PULL&BEAR", "Pull&Bear"), ("Massimo Dutti", "Massimo Dutti"),
                              ("Tommy Hilfiger Store", "Tommy Hilfiger"), ("CALVIN KLEIN", "Calvin Klein"), ("Cotton On Kids", "Cotton On"),
                              ("Mango Fashion Vincom", "Mango"), ("GAP STORE", "Gap"), ("Guess Jeans", "Guess"), ("Coach New York", "Coach"),
                              ("Superdry", "Superdry"), ("Giordano", "Giordano"), ("Takashimaya", "Takashimaya")]:
        assert classify_category_detailed(merchant, [])[:2] == ("shopping", "brand"), merchant
        assert format_merchant_name(merchant) == display, merchant
    for name in ("Mango Smoothie Bar", "Guess Who Board Game Cafe", "Coach Station Bus", "Diesel Generator Shop", "Gap Year Education", "Boss Karaoke",
                 "Triumph Motorcycles", "Next Level Gym", "R&M Auto Parts", "Mujino Restaurant", "Gaps Furniture"):
        assert classify_category_detailed(name, [])[1] != "brand", name
    assert "routine" not in BRAND_CATEGORIES and "routine vietnam" in BRAND_CATEGORIES  # "routine" riêng trùng "skincare routine"
    for merchant, display in [("YODY Cau Giay", "Yody"), ("Biluxury Store", "Biluxury"), ("The Blue Exchange Q1", "The Blue Exchange"),
                              ("DIRTY COINS Studio", "Dirty Coins"), ("MC FASHION", "MC Fashion"), ("Routine Vietnam", "Routine")]:
        assert classify_category_detailed(merchant, [])[:2] == ("shopping", "brand"), merchant
        assert format_merchant_name(merchant) == display, merchant
    for name in ("Skincare Routine Serum", "Morning Routine Cafe", "Blue Exchange Rate Bank", "Gumball Machine", "Level Events Hall", "Grey Matter Cafe"):
        assert classify_category_detailed(name, [])[1] != "brand", name
    assert "fivimart" not in BRAND_CATEGORIES  # VinCommerce mua lại, 23 siêu thị đổi thành VinMart năm 2018
    for merchant, display in [("SIEU THI FINELIFE", "Finelife"), ("Co.op Smile Quan 7", "Co.op Smile"), ("TH TRUE MART Q1", "TH true mart"),
                              ("Kingfoodmart Binh Thanh", "Kingfoodmart"), ("GIVRAL BAKERY", "Givral Bakery")]:
        assert classify_category_detailed(merchant, [])[1] == "brand", merchant
        assert format_merchant_name(merchant) == display, merchant
    for name in ("Cua hang Cheers Mung", "Win Win Store", "Go Go Karaoke", "Home Farm Stay", "Organic Rau Cu", "Fruit Tea Shop"):
        assert classify_category_detailed(name, [])[1] != "brand", name
    for merchant, display in [("CA PHE ONG BAU", "Cà Phê Ông Bầu"), ("Milano Coffee Vo Van Tan", "Milano Coffee"),
                              ("PHUC TEA FRANCHISE", "Phúc Tea"), ("CHAGEE Vincom", "Chagee"), ("Swensen's Ice Cream", "Swensen's")]:
        assert classify_category_detailed(merchant, [])[:2] == ("dining", "brand"), merchant
        assert format_merchant_name(merchant) == display, merchant
    for name in ("Passion Fruit Tea", "Milano Fashion Store", "King Kong Karaoke", "Gemini Cinema", "Tra Sua Dau Nanh", "Dau Ma Hoa Qua"):
        assert classify_category_detailed(name, [])[1] != "brand", name


def test_tobacco_is_not_medicine():
    lines = [("CONG TY TNHH THUONG MAI- DICH VU BAC HIEN", 0.9), ("Thuoc la Vinataba 20 bao", 0.9),
             ("Thuoc la Marlboro", 0.9), ("Tong cong 2,654,660", 0.9)]
    assert analyze(lines)["category"] != "healthcare"


def test_branch_label_is_not_a_business_name_and_supplier_line_wins():
    lines = ["PN007561", "Da nhap hang", "04/10/202609:46", "Chi Tram Quan 9 - banh khoai mi",
             "NCC000117·0362500031", "Khoai mi nuong (cai le)", "Nguoi nhap hang", "Thu ngan", "Chi nhanh"]
    assert _m(*lines) == "Chi Tram Quan 9 - banh khoai mi"
    assert _cat(*lines) == "dining"


HONG_TRA = [("H472 - 85 Hoàng Hữu Nam", 0.93), ("phung Tang Nhon PhTP", 0.91), ("Hồ Chí Minh", 0.85), ("185Hoang Hiru Nam", 0.9), ("0346827472", 1.0),
            ("Phiếu thanh toán", 0.91), ("SphluBH00001", 0.95), ("Ngày tạo", 0.89), ("2026-10-0508:10", 0.99), ("Ten khach hang", 0.9),
            ("Vu Thanh Tung", 0.9), ("T.Ngan: Nhan Vién 1", 0.83), ("Tai cura hang", 0.94), ("SL", 0.99), ("Tong", 0.84),
            ("Hong Tra Dai Loan -#(Lon 1000cc)", 0.91), ("1", 1.0), ("19,000", 1.0), ("+ Thach Dua", 0.91), ("3,000", 0.99), ("Nguyen V-", 0.86),
            ("x1", 0.97), ("+ Thach Suong Sao", 0.94), ("3,000", 1.0), ("-3,000d", 0.95), ("+ Khach VIP 3K", 0.91), ("Tong SL món", 0.91),
            ("1", 0.94), ("25,000d", 0.98), ("Thanh tien", 0.88), ("22,000d", 0.97), ("Tong thanh toan", 0.94), ("22,000d", 0.94), ("+ TIan mat", 0.75)]


def test_tea_shop_bill_without_a_shop_name_is_dining_from_its_drink_items():
    r = analyze(HONG_TRA)
    assert r["category"] == "dining" and r["category_source"] == "product"
    assert r["total_amount"] == 22000


def test_an_address_line_used_as_the_name_is_always_flagged_for_review():
    r = analyze(HONG_TRA)
    assert r["merchant"] == "H472 - 85 Hoàng Hữu Nam" and r["needs_review"]  # tên cửa hàng không in trên phiếu: người dùng điền rồi Lưu


def test_drink_vocabulary_and_tea_brand():
    for item in ("Hong tra dai loan", "Tra xanh matcha latte", "Olong sua", "Tran chau duong den", "Thach dua", "Thach suong sao", "Soda chanh"):
        assert classify_category_detailed(None, [item, "Tra vai", "Tong 50.000"])[:2] == ("dining", "product"), item
    assert classify_category_detailed("HONG TRA NGO GIA", [])[:2] == ("dining", "brand")
    assert classify_category_detailed(None, ["Thach rau cau", "Gao ST25 5kg", "Sua tuoi 1L", "Tong 200.000"])[0] == "groceries"  # siêu thị có thạch rau câu vẫn là groceries


VINARICE = [("CÔNG TY TNHH SX TM GẠO VIỆT NAM", 0.97), ("MST0313486701", 1.0), ("VINARICE", 0.98), ("PHIẾU GIAO HÀNG", 0.99), ("Ngay 05-10-2026", 0.99),
            ("Khach hang: Coco Mart_Toa Masteri Khu Vinhome Quan 9", 0.94), ("SDT0866647689", 1.0), ("MSKHCUZN04599", 1.0), ("Ten hang hoa", 0.91),
            ("Don vi tien teVND", 0.91), ("Gao thom ST25 5kg - Mé Gao", 0.91), ("Thanh tien", 0.93), ("Kg", 0.91), ("30", 1.0), ("23,700", 0.99), ("711,000", 1.0),
            ("Gao ST25 (HCK) 2Kg -Me Gao", 0.93), ("Kg", 0.99), ("20", 1.0), ("32,700", 0.99), ("654,000", 1.0), ("Gao ST21 5Kg - Mé Gao", 0.95), ("Kg", 1.0),
            ("30", 1.0), ("21,500", 1.0), ("645,000", 1.0), ("Tong cong", 0.96), ("80", 1.0), ("2,010,000", 1.0),
            ("S6 tai khoan nhan thanh toan: 909915990 - CONG TY TNHH SX TM GAO VIET NAM - MBBANK", 0.94), ("Luuy:", 0.97)]


def test_bank_account_number_is_not_the_total_even_on_a_line_that_says_thanh_toan():
    r = analyze(VINARICE)
    assert r["total_amount"] == 2010000


def test_account_lines_and_bare_nine_digit_ids_are_never_money():
    from extract import extract_total
    assert extract_total(["So tai khoan nhan thanh toan: 909915990 - MBBANK"]) is None
    assert extract_total(["Ma don 123456789", "Tong cong 250.000"]) == 250000
    assert extract_total(["STK: 1903456789 Techcombank", "Thanh toan 99.000"]) == 99000
    assert extract_total(["Tong cong 125000000"]) is None or extract_total(["Tong cong 125.000.000"]) == 125000000  # có dấu phân cách thì vẫn là tiền


def _check(lines):
    from extract import items_cross_check
    return items_cross_check(lines)


def test_items_sum_uses_quantity_times_price_equals_amount_triples():
    texts = [t for t, _ in VINARICE]
    assert _check(texts)["items_sum"] == 2010000          # 30x23.700 + 20x32.700 + 30x21.500
    assert _check(texts)["items_count"] == 3


def test_same_line_pos_rows_are_counted_and_other_numbers_are_not():
    rows = ["Mon A 1 20.000 20.000", "Mon B 2 15.000 30.000", "Ma don 4021 5555", "Tong cong 50.000"]
    assert _check(rows)["items_sum"] == 50000 and _check(rows)["items_count"] == 2
    assert _check(["Ngay 04/10/2026", "SDT 0903 123 456", "Tong 25.000"])["items_sum"] is None  # không có dòng hàng nào kiểm chứng được


def test_cross_check_levels_match_plausible_mismatch():
    from extract import items_check_level
    assert items_check_level(2010000, 2010000) == "match"
    assert items_check_level(2171000, 2010000) == "plausible"    # có VAT 8%
    assert items_check_level(1800000, 2010000) == "plausible"    # có giảm giá
    assert items_check_level(909915990, 2010000) == "mismatch"   # đúng lỗi lấy nhầm số tài khoản
    assert items_check_level(50000, 2010000) == "mismatch"
    assert items_check_level(None, 2010000) is None
    assert items_check_level(2523000, 629000) == "plausible"      # tổng một phần: OCR chỉ đọc đủ vài dòng hàng (ca thật, lệch 4 lần)
    assert items_check_level(146000, 21000) == "plausible"        # ca thật lệch 7 lần


def test_analyze_flags_a_total_far_from_the_items_and_fills_in_a_missing_total():
    base = [("CUA HANG GAO", 0.95), ("PHIEU GIAO HANG", 0.95), ("Gao ST25 5kg", 0.95), ("Kg", 0.95), ("30", 1.0), ("23,700", 1.0), ("711,000", 1.0),
            ("Gao ST21 5Kg", 0.95), ("20", 1.0), ("32,700", 1.0), ("654,000", 1.0), ("Cam on quy khach", 0.95)]
    ok = analyze(base + [("Tong cong 1,365,000", 0.95)])
    assert ok["total_amount"] == 1365000 and ok["items_check"] == "match"
    wrong = analyze(base + [("Tong cong 99,000,000", 0.95)])
    assert wrong["items_check"] == "mismatch" and wrong["needs_review"]
    missing = analyze(base)                                           # không có dòng tổng nào
    assert missing["total_amount"] == 1365000 and missing["total_source"] == "items" and missing["needs_review"]


MM_HEADER = [("Kyhae3eriest", 0.66), ("Don vian hanoTRN TAM  EGA AET AN PH", 0.7), ("Ten don vi", 0.76), ("Dia chi:", 0.88),
             ("Ma so thue0302249586", 0.82), ("Kenh H Gia Tot", 0.9), ("So Hoa DonVAT0159393", 0.91), ("HTTPS://EINOICE.MMIETNAM.COM", 0.83),
             ("Ngaya02/10/20261616", 0.96), ("Thu nganCDD", 0.84), ("Tong cong so luong10", 0.94), ("Tong cong truoc thue", 0.93), ("2.041.728", 0.98),
             ("2.234.654", 0.97), ("Tong cong so tien da co thue GGT", 0.89), ("So tien bang chu", 0.91),
             ("Hai trieu hai tram ba uoi bon nghin sau tra nam uoi bon dong", 0.89), ("2.234.654D", 0.94)]


def test_garbled_header_still_names_the_shop_from_its_website_domain():
    r = analyze(MM_HEADER)
    from merchant_name import format_merchant_name
    assert format_merchant_name(r["merchant"]) == "MM Mega Market"
    assert r["category"] == "groceries"


def test_amount_in_words_survives_ocr_dropping_the_accent_letters():
    from extract import words_to_amount
    assert words_to_amount("Hai trieu hai tram ba uoi bon nghin sau tra nam uoi bon dong") == 2234654
    assert analyze(MM_HEADER)["words_check"] == "match"


NHA_TRO = [("NHÀ TRO", 0.93), ("SẠCH SẾ", 0.95), ("HÓA ĐỐN DỊCH VỤ", 0.94), ("Nha Tro Sach Sé thng báo HOA DON DICH VU", 0.93), ("10/2026 c6 ma 2610C9FNAM cua khach hang", 0.94),
           ("Le Minh Tuan c6 ma khach hang\"KH19570", 0.93), ("phong B3,khu D An - 36 Dudng QL 1A\", vui", 0.93),
           ("lng thanh toán tru6c ngay 05/10/2026 de", 0.9), ("khng bi dóng tién tré han", 0.98), ("Tién phng", 0.97), ("2.400.000 dong (31 ngay)", 0.95),
           ("Tién dién", 0.93), ("364.000 dong (2600*140 KWh,", 0.96), ("1003/1143)", 0.99), ("Tién dien tien 196.000 dong (1400*140 KWh,", 0.93),
           ("ich chung", 0.99), ("1003/1143)", 1.0), ("Tién nu6c", 0.91), ("150.000 dong (6 m3, 39/45)", 0.98), ("Tién dich vu", 0.96),
           ("550.000 dong (Ó vu0t 0 dong +", 0.92), ("Dich vu khác 550.000 dng)", 0.96), ("Phi phat", 0.99), ("0 dong (0 ngay)", 0.96), ("thanh toan", 0.95),
           ("tré", 0.85), ("TONG", 0.93), ("3.660.000 dong", 0.95), ("THANH", 1.0), ("TOAN", 1.0), ("Xem chi tiét h6a don", 0.89)]


def test_date_on_a_payment_deadline_line_is_not_the_total_and_split_total_label_is_found():
    r = analyze(NHA_TRO)
    assert r["total_amount"] == 3660000


def test_years_and_dates_are_never_money_but_real_amounts_are():
    from extract import extract_total
    assert extract_total(["Han thanh toan truoc ngay 05/10/2026", "TONG 125.000"]) == 125000
    assert extract_total(["Thanh toan truoc 10/2026", "Tong cong 2026"]) is None            # "2026" trần là năm, không phải tiền
    assert extract_total(["TONG THANH TOAN", "3.660.000 dong"]) == 3660000                   # số kèm "đồng" vẫn là dòng số tiền
    assert extract_total(["Tong cong", "250,000 VND"]) == 250000


def test_two_line_logo_name_is_joined_into_one_name():
    from merchant_name import format_merchant_name
    r = analyze(NHA_TRO)
    from extract import _norm
    assert _norm(format_merchant_name(r["merchant"])) == "nha tro sach se"  # dấu cuối do OCR đọc, ở đây chỉ kiểm tra việc ghép hai dòng


def test_boarding_house_invoice_counts_as_housing_per_the_user_choice():
    r = analyze(NHA_TRO)
    assert r["category"] == "housing"
    assert classify_category_detailed("NHA TRO SACH SE", [])[:2] == ("housing", "keyword")
    assert classify_category_detailed(None, ["Tien nha thang 10", "Tien dien 364.000", "Tong 3.000.000"])[:2] == ("housing", "product")
    assert classify_category_detailed("Cua hang Gao Viet", ["Giao den nha tro Ba Muoi", "Gao ST25 5kg"])[0] != "housing"  # "nhà trọ" trong địa chỉ giao hàng không làm nên nhà ở


def test_housing_covers_rent_electricity_water_gas_and_apartment_fees_but_not_telecom():
    for merchant in ("Cong ty Dien luc TP", "Cap nuoc khu vuc 3", "Dai ly gas Nam Viet", "Ban quan ly chung cu Sunrise", "Phong tro Hoa Binh"):
        assert classify_category_detailed(merchant, [])[0] == "housing", merchant
    for lines in (["Tien gas binh 12kg", "Tong 420.000"], ["Phi chung cu thang 10", "Tong 600.000"], ["Tien rac sinh hoat", "Tong 30.000"]):
        assert classify_category_detailed(None, lines)[0] == "housing", lines
    for merchant in ("Viettel", "VNPT", "FPT Telecom"):
        assert classify_category_detailed(merchant, [])[0] == "utilities", merchant
    assert classify_category_detailed(None, ["Cuoc internet cap quang", "Tong 220.000"])[0] == "utilities"
    assert classify_category_detailed(None, ["Nap dien thoai 100.000", "Tong 100.000"])[0] == "utilities"  # "điện thoại" không phải tiền điện


# Phiếu bán hàng giấy in nhiệt dài chụp xa (đã bỏ tên, điện thoại, người bán): tiêu đề "Phiếu Bán Hàng" và "Khách hàng" (người mua) không phải tên cửa hàng.
PHIEU_BAN_HANG = ["68123123", "Ngay:01/10/2026 07:47", "Phiếu Bán Hàng", "HANGDOITRATRONGVONG3NGAY", "XINCAM ON", "KhachhangB1839 COCO MART",
                  "E1MASTERI", "AQUAFINA PET24X35SML-THng24", "92.000", "276.000", "MUOI TRANG1KG-10KG", "65,000", "x1", "65.000",
                  "NGUA TRANG-Cay", "260,000", "x2", "520,000", "Tng Tin Hang5,654.000 VND", "Glam gla0.5%28.270 VND",
                  "Tin khach dua:5.625,730 VND", "Tóng thanh toan5,625,730-VND", "Cam on quyvi da danhthoi gian cho"]


def test_sales_slip_title_and_buyer_are_not_taken_as_the_shop_and_payable_total_wins():
    r = analyze([(t, 0.9) for t in PHIEU_BAN_HANG])
    assert r["total_amount"] == 5625730  # tổng thanh toán, không phải tổng tiền hàng 5.654.000
    assert "phieu" not in _alnum(_norm(r["merchant"] or "")) and "coco" not in _alnum(_norm(r["merchant"] or ""))         and "doitra" not in _alnum(_norm(r["merchant"] or ""))  # dòng "Hàng đổi trả trong vòng 3 ngày" là chính sách
    assert r["needs_review"]  # không đọc được tên người bán thì phải nhắc kiểm tra


# --- mua sắm và giải trí: từ vựng mặt hàng/loại hình (chữ OCR của ảnh bill giả đã làm hỏng như ảnh chụp thật) ---
def test_shopping_items_without_a_known_brand_are_recognised():
    for lines in (["Quần Jean nam ống đứng", "616.000", "Tui xach tay", "276.000", "TONG CONG 892.000"],
                  ["Tai nghe bluetooth", "697.500", "Kem chng nang SPF50", "170.500"],
                  ["Balo laptop", "896.000", "TNG CNG 896.000 VND"]):
        assert classify_category_detailed("Cua hang Linh", lines)[0] == "shopping", lines
    assert classify_category_detailed("Tiệm vàng Kim Ngọc", [])[:2] == ("shopping", "keyword")
    assert classify_category_detailed("Siêu thị điện máy Nam Anh", ["Kem chống nắng SPF50", "205.000"])[0] == "shopping"  # không phải thực phẩm


def test_entertainment_tickets_and_venues_are_recognised():
    for lines in (["Combo bắp nước", "120.500", "TONG CONG 120.500 VND"], ["Véxem phim 2D ngui ln", "99.500"],
                  ["Vé vào cổng khu vui chơi", "300.500"], ["Máy chơi game trẻ em", "154.000", "Vé xem phim 2D", "99.500"]):
        assert classify_category_detailed("Cong ty ABC", lines)[0] == "entertainment", lines
    for name in ("Nhà hát lớn", "Khu vui chơi Kids Zone", "Công viên nước Đầm Sen"):
        assert classify_category_detailed(name, [])[:2] == ("entertainment", "keyword"), name


def test_new_shopping_and_entertainment_words_do_not_steal_ordinary_bills():
    assert classify_category_detailed("Cua hang Gao Viet", ["Gao ST25 5kg", "Dau an 1L", "Sua tuoi 1L"])[0] == "groceries"
    assert classify_category_detailed("Quan com Ba Tu", ["Com suon nuong", "Tra da", "Bap cai xao"])[0] == "dining"
    assert classify_category_detailed(None, ["Cam on quy vi da danh thoi gian cho", "Giay da in 2 mat"])[0] != "shopping"  # "quý vị đã" bỏ dấu thành "vi da"
    assert classify_category_detailed("Mắt kính Sài Gòn", [])[0] == "healthcare"  # khớp mô tả trong app: kính mắt thuộc Sức khỏe


# --- giao thông và tạp hóa ---
def test_transport_places_and_services_are_recognised():
    for name in ("Nhà xe Hoàng Long", "Bãi giữ xe Nguyễn Trãi", "Tiệm rửa xe Hùng", "Garage ô tô Minh Phát"):
        assert classify_category_detailed(name, [])[:2] == ("transport", "keyword"), name
    for lines in (["Sửa xe vá lốp", "3X 139.500"], ["Phi gui xe  to theo gi", "51.500", "Phí cầu đường BOT", "117.000"], ["Thay nhót động co", "300.000", "Phí gùi xe máy", "3.000"]):
        assert classify_category_detailed("Cong ty ABC", lines)[0] == "transport", lines


def test_grocery_shop_names_and_household_goods_are_recognised():
    for name in ("Tạp hóa Bà Tư", "Tiệm tạp hóa Cô Lan", "Cửa hàng thực phẩm sạch", "Chợ đầu mối Bình Điền"):
        assert classify_category_detailed(name, [])[:2] == ("groceries", "keyword"), name
    assert classify_category_detailed("Cong ty ABC", ["Bột giặt túi 3kg", "307.500", "Bánh mì sandwich", "21.500"])[0] == "groceries"  # "bánh mì sandwich" là bánh đóng gói, không phải quán ăn
    assert classify_category_detailed("Quan an Ba Hai", ["Banh mi thit nguoi", "Ca phe sua da"])[0] == "dining"


def test_new_words_do_not_confuse_repair_services_with_yogurt():
    assert classify_category_detailed("Tiệm sửa điện thoại Phát", ["Dich vu sua chua dieu hoa", "213.000"])[0] != "groceries"  # "sửa chữa" bỏ dấu = "sua chua" (sữa chua)


# --- viễn thông và ăn uống ---
def test_telecom_places_and_services_are_recognised_and_phone_top_up_is_not_electricity():
    for name in ("Đại lý viễn thông Mai Linh", "Viễn thông tỉnh", "Trung tâm giao dịch internet"):
        assert classify_category_detailed(name, [])[:2] == ("utilities", "keyword"), name
    for lines in (["Dịch vụ wifi gia đình", "695.000"], ["Gia han goi cuoc di dong", "172.000"], ["Cuoc internet cap quang", "281.500"]):
        assert classify_category_detailed("Cong ty ABC", lines)[0] == "utilities", lines
    assert classify_category_detailed("Cong ty ABC", ["Nap tien dien thoal", "100.000"])[0] != "housing"  # "tiền điện thoại" (OCR đọc sai chữ cuối) không phải tiền điện
    assert classify_category_detailed("Cong ty ABC", ["Nap tien dien thoai", "100.000"])[0] == "utilities"
    assert classify_category_detailed("Cong ty ABC", ["Tien dien thang 10", "450.000"])[0] == "housing"  # tiền điện thật vẫn là nhà ở


def test_dining_places_and_common_dishes_are_recognised():
    for name in ("Quán bún bò Huế O Hiền", "Quán nhậu Tư Béo", "Tiệm bánh Ngọt Ngào"):
        assert classify_category_detailed(name, [])[:2] == ("dining", "keyword"), name
    for lines in (["Combo burger khoai chiên", "438.000", "Ga ran 2 mieng", "122.000"], ["Pho bo tai", "68.000", "Com suon nuong", "55.000"]):
        assert classify_category_detailed("Cong ty ABC", lines)[0] == "dining", lines
    assert classify_category_detailed("Cua hang Gao Viet", ["Gao ST25 5kg", "Xuc xich tiet trung", "Ga nguyen con"])[0] == "groceries"


# --- tên cửa hàng trên phiếu giao nhận/bảng kê (chữ OCR thật, đã bỏ điện thoại và tên người) ---
def test_header_with_shop_code_and_print_time_gives_the_shop_code_not_a_table_heading():
    lines = ["WAMIHS-21/09/202615:41", "Khách:", "CH CoCo mart", "Địa chi: Masteri tòa B", "T.Tiển", "Đơn giá", "Sản phẩm", "SL", "187,000"]
    assert extract_merchant(lines, [0.98, 0.9, 0.96, 0.93, 0.95, 0.78, 0.84, 0.99, 0.99]) == "WAMIHS"
    assert extract_merchant(["T.Tiển", "Đơn giá", "SL", "187,000"]) is None  # tiêu đề cột không bao giờ là tên cửa hàng


def test_seller_label_survives_ocr_mistakes_in_the_word_ten():
    lines = ["PHIẾU GIAO NHÂN THANH TOÁN", "T@a NPP Cong Jy TNHH MinhPhaThanh", "Ten KH99993554CoCoMar", "03NM00684 Nuoc mam"]
    assert "MinhPhaThanh" in extract_merchant(lines, [0.95, 0.79, 0.82, 0.81])
    assert extract_merchant(["To go ve", "Com suon 45.000", "Tong 45.000"]) != "go ve"  # "To ..." bình thường không phải nhãn người bán


def test_ordinary_all_caps_header_with_a_date_is_not_mistaken_for_a_shop_code():
    assert extract_merchant(["NGAY-21/09/2026", "HIGHLANDS COFFEE", "Tong 50.000"]) != "NGAY"
