"""Ảnh chụp màn hình app: đầu ra OCR thật của 3 ảnh người dùng gửi (đã bỏ mã khách hàng/số thẻ)."""
import sys, pathlib

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))

from parsing.app_screens import parse

RIDE = ["16:55 1", "23", "06 Jun 2026,08:17PM", "Mã chuyến đi", "A-XXXX 6", "Ban khng thé xép hang, gúri tien tip hoác xem thng",
        "tin tài xế sau 7 ngày.", "Car", "0.67 km - 8phut", "353/2Dinh B Linh", "8:26 PM", "FPT Shop-Xo Viet Nghe Tinh", "8:34 PM",
        "4215", "8", "Ca nhan", "Total", "32.000@ >", ", Xuat hóa don", "A", "Báo cáo su c", "C' Datlai"]

SERVICE = ["17:07", "19", "Chi Tiết Giao Dịch", "ĐIỆN LỰC HỒ CHÍ MINH", "-802.278d", "Trạng thái", "Thành công", "Thi gian ",
           "18:29-10/07/2026", "Ma giao dich", "137061556710", "Tai khoan/thé", "Quy Tién topaz", "Tong phi", "Mién phi", "Danh muc",
           "H6a don v", "Nha cung cap", "Dién luc Ho Chi Minh", "Ma khach hang", "PEXXXX", "CNG TY Co PHAN", "Ten khách hang",
           "Dia chi", "Ky thanh toan", "Tién dién 6/2026", "Quan ly chi tieu", "Lién hé CSKH", "Giao dich mói"]

SHOPEE = ["16:53", "23", "Đơn đã mua", " láy hang", "Ch giao háng", "Đã giao", "Trả hàng", "Đã hủy", "203.400", "Mua lai",
          "Gia tung mua 221.400d", "Yéu thich", "FURYCUBE LOCAL", "Hoan thanh", "Ban Phim C6 Day FURYCUBE IP75,75% Lay..",
          "Xam Gradient-3 Mode", "x1", "680.000@549.000d", "Tong so tién (1 san pham): 466.650d", "Xem dánh giá", "Mua lai",
          "Yéu thich+", "VUALOTCHUOT", "Hoan thanh", "Gengar - Miéng Lót Chut, Ban Phim May Ti.", "Gengar 01,80x30cm", "x1",
          "95.000d", "Tong so tién (1 san pham) : 90.200d", "Xem dánh giá", "Mua lai", " Buni_Furniture_Toan Quoc", "Hoan thanh",
          "Ghé Xoay Van Phong, Ghé Cong Thái Hoc,..", "BN06-Nga,Den", "x1", "1.800.000@1.376.600d",
          "Tng s tien (1 san pham): 1.073.748d", "Xem dánh giá", "Mua lai", "Yéu thich+", "Phu ki@n Holly", "Hoan thanh",
          "Keycap Folded Shadow ! Profile Cherry 130."]


def test_ride_screen_gives_total_and_transport_not_the_destination_as_merchant():
    r = parse(RIDE)
    assert r["screen_type"] == "ride" and r["total_amount"] == 32000 and r["category"] == "transport"
    assert "FPT" not in r["merchant"]


def test_service_payment_screen_reads_provider_and_the_big_amount_not_the_year():
    r = parse(SERVICE)
    assert r["screen_type"] == "service_payment"
    assert r["merchant"] == "ĐIỆN LỰC HỒ CHÍ MINH" and r["total_amount"] == 802278 and r["category"] == "housing"


def test_order_list_gives_one_order_per_shop_with_the_payable_total():
    r = parse(SHOPEE)
    assert r["screen_type"] == "order_list" and r["category"] == "shopping"
    assert [(o["merchant"], o["total_amount"]) for o in r["orders"]] == [
        ("FURYCUBE LOCAL", 466650), ("VUALOTCHUOT", 90200), ("Buni_Furniture_Toan Quoc", 1073748)]
    assert r["orders"][0]["product"].startswith("Ban Phim")


def test_order_list_counts_cut_off_orders_instead_of_guessing():
    # đơn đầu mất tên shop (203.400 không có "Tổng số tiền"), đơn cuối không có dòng tổng tiền
    assert parse(SHOPEE)["incomplete_orders"] == 1  # đơn "Phu kien Holly" bị cắt


def test_order_list_with_badge_and_name_on_one_line_and_orphan_total():
    lines = ["Đơn đã mua", "Tong so tién (2 san pham): 50.000d", "Mua lai", "Yêu thích+ SHOP A", "Hoàn thành", "Áo thun",
             "Tổng số tiền (1 sản phẩm): 120.000đ", "Mua lại"]
    r = parse(lines)
    assert [(o["merchant"], o["total_amount"]) for o in r["orders"]] == [("SHOP A", 120000)]
    assert r["incomplete_orders"] == 1


def test_paper_receipts_and_unknown_screens_are_not_treated_as_app_screens():
    assert parse(["HIGHLANDS COFFEE", "Cam on quy khach", "TONG CONG 125.000"]) is None
    assert parse(["Hoàn thành", "Mua lại"]) is None  # thiếu dòng tổng tiền đơn


# Vé xem phim CGV (ảnh thật): nền in lặp chữ TICKET nên OCR chỉ đọc được mảnh vụn
CINEMA = ["TICKE", "TICKCKET", "THE VÀO", "PHỊ", "TICKET", "Ng", "Vincon Nguyen Chi Thanh", "TICKET", "NCT", "9.Quan", "11", "an", "at",
          "Dong DaHa Noi", "POS:BOXO5", "TICKET", "ICKET TICKET", "L", "Ticker Price", "C", "TICKETICKZT", "CUNDO", "Sale", "UATS.", "c", "KET", "TIC"]


def test_cinema_ticket_is_recognised_even_from_the_scraps_ocr_can_read():
    r = parse(CINEMA)
    assert r["screen_type"] == "cinema_ticket" and r["category"] == "entertainment"
    assert r["total_amount"] is None  # không đọc được giá thì không đoán


def test_cinema_ticket_with_price_and_cgv_text_gives_total_and_name():
    lines = ["THE VAO PHONG CHIEU PHIM", "CGV Vincom Nguyen Chi Thanh", "Nha Ba Nu [C16]", "Gold Class", "TICKET", "TICKET", "TICKET", "VND 195.000", "VND 105.000", "VND 300.000"]
    r = parse(lines)
    assert r["screen_type"] == "cinema_ticket" and r["total_amount"] == 300000 and r["merchant"] == "CGV"


def test_an_ordinary_parking_or_event_ticket_is_not_a_cinema_ticket():
    assert parse(["PARKING TICKET", "TICKET", "TICKET", "Gio vao 08:00", "Phi 5.000"]) is None
    assert parse(["Ve xe khach Phuong Trang", "Ticket", "Gia 250.000"]) is None


# Nhãn kiện hàng Shopee Express (ảnh thật); tên người nhận và địa chỉ có trong chữ OCR nên không được lưu
SPX = ["SPX", "Mavaitan:", "SPXVN06241337149A", "Ma d0hang: 261001FJ9VEYYA", "TEX:", ":LanThanh Dinh", "BinGiaDung25", "541/72 Phan ang Luu,kp Tuong",
       "Maeri Centre Point,Block E,D1", "HCB-51-121-Q92-N: Q9-PLB-19-D", "Noi dung hang (Tong SL san pham: 2)", "mui,Size L,5 HOP SL2",
       "QUY KHACH VUI LONG", "Ngay dat hang:", "01-10-2026", "13:24", "Tién thu Ngui nhan:", "<hi luang ti da: 1,800 g", "Chr ky ngui nhan",
       "nhan hang nguyén ven, khng", "252,500 VND", "mp/méo,bev", "Goi 1900 6885", "Buu cuc thu nh@p dén 20.000.000 VN", "Chuyen phat nhanh chi tu 11.000 VND"]


def test_shipping_label_gives_the_cash_on_delivery_amount_and_never_keeps_recipient_text():
    r = parse(SPX)
    assert r["screen_type"] == "shipping_label" and r["category"] == "shopping"
    assert r["total_amount"] == 252500 and r["merchant"] == "Shopee" and r["force_review"]
    assert r["keep_lines"] == []  # nhãn có tên/địa chỉ người nhận: không giữ dòng chữ nào để lưu vào dataset


def test_prepaid_label_has_no_total_and_other_couriers_have_no_guessed_shop():
    prepaid = ["GHN Giao Hang Nhanh", "Ma van don: GHNABC123", "Noi dung hang: Ao thun", "Tien thu nguoi nhan: 0 VND"]
    r = parse(prepaid)
    assert r["screen_type"] == "shipping_label" and r["total_amount"] is None and r["merchant"] is None


def test_a_normal_bill_that_mentions_giao_hang_is_not_a_shipping_label():
    assert parse(["HIGHLANDS COFFEE", "Giao hang tan noi", "Tien thu ngan 35.000", "TONG CONG 35.000"]) is None


# --- ảnh chụp màn hình thật: giao đồ ăn, vé xe khách, ví điện tử (đã bỏ tên, điện thoại, địa chỉ người nhận) ---
XANH_SM = ["23:12 1", "17:40 04/10/2026", "Green Food", "Tóm tắt đơn hàng", "4 món : 67.550d .  Tién mat/QR", "Xem chi tiết đơn hàng",
           "Ma don hang", "01M43805HQ7M3...", "Trang thái", "Hoan thanh", "Chi tiét giao hang", "Wallace Burger & Chicken - Kha Van Can",
           "1098 Duong Kha Van Can", "Dat lai"]

SHOPEEFOOD = ["02:02 1", "Chi tiết đơn hàng", "000", "Đơn hoàn tất", "niO", "Do anlKOKOPOP KOREAN - FOOD X", "o Dén", "Tóm tát don háng",
              "1 x_HOTDOG Xuc xich khoai tay", "28.000d", "1 x [Size M] Ga vién sot chua ngot +", "69.000d", "Tokbokki", "89.000d",
              "64.000d", "1 x Mi Han Trn + Ga vien sÓt chua ngot", "95.000d", "Khong cay", "Tong (3 món)", "161.000d",
              "Phi giao hang (1.3 km)", "15.000d", "Phi áp dung @", "3.000d", "Giam gia", "-38.000d", "141.000d", "Thong tin don hang"]

GRABFOOD = ["02:02 1", "03:54, 25 Thg 7 2026", "Ăn Vặt - kim Hiền - Cá Viên Chiên", "GF-748", "Đã giao", "Ghi chu cho tai xé",
            "Tóm tat don hang", "Dat lai", "70.000", "met cá vién chién mam tói", "(phan nhó)", "1x", "banh trang chién", "30.000",
            "1x", "Tong tam tinh", "132.000d", "Phi áp dung @", "41.000", " Giam 26.000d", "-26.000", "Giam 11.000d", "-11.000",
            "Giam 6.000d", "-6.000", "Tong cong", "130.000d", "Lién hé vói Grab"]

VEXERE = ["02:04", "Thông tin vé xe", "O Vé đã sử dụng", "Việt Tân Phát", "Limousine 34 giung", "Chi tiết", "Ho Tay", "20:23",
          "Ma dat cho (doi vé tai quay)", "29T5Y4 D", "Tong tién", "279.500d ", "Giam 25% khi dat khach san", "Nha xe:", "Viét Tan Phat"]

MOMO_CGV = ["02:01", "Chi Tiết Giao Dịch", "MUA VÉ XEM PHIM", "-261.000d", "D", " Ban dá dat thanh cng vé xem phim tai rap CGV",
            "Giga Mall Thủ Đức", "Trang thai", "Thánh cong", "Thi gian", "17:55-13/09/2026", "Ma giao dich", "146685842802",
            "Vi Tra Sau", "Tai khoan/the", "Tong phi", "Miên phi", "Thuong xu", "+20 Xu >", "Danh muc", "Giai tri v", "THONG TIN DAT VE"]

MOMO_DATA = ["02:04", "Chi Tiết Giao Dịch", "A", "NAP DATA VIETTEL", "-110.000d", "Trạng thái", "Thành công", "Thi gian",
             "13:03-03/09/2026", "Ma giao dich", "145051631399D", "Tong phi", "Miên phi", "Thuong xu", "+121 Xu", "Danh muc",
             "Hóa don -", "Nha mang", "Viettel", "Gia g6i", "110.000d"]


def test_xanh_sm_food_order_reads_the_restaurant_not_the_app_name_and_the_total():
    r = parse(XANH_SM)
    assert r["screen_type"] == "food_delivery" and r["category"] == "dining"
    assert r["merchant"].startswith("Wallace Burger") and r["total_amount"] == 67550
    assert r["keep_lines"] == [r["merchant"]]  # chỉ giữ tên quán: ảnh có địa chỉ, tên và điện thoại người nhận


def test_shopeefood_order_total_is_the_amount_paid_after_fees_and_discounts():
    r = parse(SHOPEEFOOD)
    assert r["merchant"] == "KOKOPOP KOREAN - FOOD" and r["total_amount"] == 141000  # không phải 161.000 (tổng món)
    assert any("Tokbokki" in l for l in r["keep_lines"])


def test_grabfood_order_total_is_tong_cong_not_the_subtotal():
    r = parse(GRABFOOD)
    assert r["merchant"] == "Ăn Vặt - kim Hiền - Cá Viên Chiên" and r["total_amount"] == 130000  # không phải 132.000 (tạm tính)


def test_bus_ticket_merchant_is_the_operator_not_the_screen_title_or_status():
    r = parse(VEXERE)
    assert r["screen_type"] == "bus_ticket" and r["merchant"] == "Việt Tân Phát"
    assert r["total_amount"] == 279500 and r["category"] == "transport"


def test_wallet_transaction_uses_known_brand_and_the_apps_own_category():
    cgv = parse(MOMO_CGV)
    assert cgv["merchant"] == "CGV" and cgv["total_amount"] == 261000 and cgv["category"] == "entertainment"
    data = parse(MOMO_DATA)
    assert data["merchant"] == "Viettel" and data["total_amount"] == 110000 and data["category"] == "utilities"


def test_wallet_transaction_without_a_known_category_is_flagged_for_review():
    r = parse(["Chi Tiết Giao Dịch", "CHUYEN TIEN", "-50.000d", "Ma giao dich", "123456789012", "Thuong xu", "+1 Xu"])
    assert r["screen_type"] == "wallet_transaction" and r["total_amount"] == 50000 and r["force_review"]


def test_ordinary_food_receipt_is_not_mistaken_for_a_delivery_screen():
    assert parse(["PHO 24", "Tom tat don hang", "Pho bo 55.000", "TONG CONG 55.000"]) is None


def test_service_payment_splits_housing_bills_from_telecom():
    telecom = [l.replace("Dién luc Ho Chi Minh", "FPT Telecom").replace("ĐIỆN LỰC HỒ CHÍ MINH", "FPT TELECOM") for l in SERVICE]
    assert parse(SERVICE)["category"] == "housing"
    assert parse(telecom)["category"] == "utilities"


def test_wallet_generic_bill_category_is_split_by_provider():
    water = ["Chi Tiết Giao Dịch", "TIEN NUOC THANG 9", "-120.000d", "Ma giao dich", "123456789012", "Danh muc", "Hóa don -"]
    assert parse(water)["category"] == "housing"
    assert parse(MOMO_DATA)["category"] == "utilities"  # nạp data Viettel


MOMO_ELECTRIC = ["11:52", "Kết quả giao dịch", "Giao dich thánh cng", "Chia sé >", "1.062.774d", "Dịch vụ/ Cửa hàng", "Điện lực Hồ Chí Minh",
                 "Thi gian giao dich", "11:52 - 10/02/2026", "Ma khách hang", "PE12000307008", "Tén khach hang", "CNG TY Co PHAN"]
FPT_NOTICE = ["10:05", "FPT Telecom", "Tài khoản OA", "Thong báo thanh toán thánh cng", "Quy khách dá thanh toán thánh cng dón hang",
              "Ma d0n hang HCMKH929NL2X", "Khach hang", "LE MINH TUAN", "Hp dong so SGAEM1455", "So tién", "739,000 VND"]


def test_momo_result_screen_reads_provider_and_amount_without_the_customers_details():
    s = parse(MOMO_ELECTRIC)
    assert (s["screen_type"], s["merchant"], s["total_amount"], s["category"]) == ("momo_result", "Điện lực Hồ Chí Minh", 1_062_774, "housing")
    assert s["keep_lines"] == ["Điện lực Hồ Chí Minh"]


def test_provider_payment_notice_reads_brand_and_amount():
    s = parse(FPT_NOTICE)
    assert (s["screen_type"], s["merchant"], s["total_amount"], s["category"]) == ("provider_notice", "FPT Telecom", 739_000, "utilities")
