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
    lines = ["Ca phe sua", "29,000", "Tong cong", "29,000", "Tien khach dua", "100,000", "Tien thua", "71,000"]
    assert extract_total(lines) == 29_000
    # nhãn dính chữ "Khachphai tra" (ảnh thật 960345d1) vẫn là nhãn phải trả; "Tien khach dua" sau đó không thay thế nó
    glued = ["Cong tien hang", "125,000", "Chiet khau", "0", "Khachphai tra", "130,000", "Tien khach dua", "0", "Tralai", "0"]
    assert extract_total(glued) == 130_000


def test_subtotal_only_still_falls_back_and_single_line_total_unchanged():
    assert extract_total(["Tong (tam tinh)", "42.000d"]) == 42_000
    assert extract_total(["Banh mi", "Tong cong 125.000"]) == 125_000
    assert extract_total(["Tra mat ong", "Tong cong", "35.000"]) == 35_000  # "Tong" là chữ đầu từ vẫn là nhãn
