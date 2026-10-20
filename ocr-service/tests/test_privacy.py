"""Che dữ liệu cá nhân trước khi lưu dataset hoặc trả về app. Ví dụ lấy từ các dòng thật đã thấy trong dataset."""
import sys, pathlib

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))

from parsing.privacy import sanitize_lines


def test_phone_with_separators_email_and_long_account_numbers_are_masked():
    out = sanitize_lines(["DT093.636.6628", "Hotline 0903 026 419", "lienhe@cuahang.vn", "Techcombank 1903.4567.8901.23", "MST 0312345678"])
    joined = " | ".join(out)
    for secret in ("093.636.6628", "0903 026 419", "lienhe@cuahang.vn", "1903.4567.8901.23", "0312345678"):
        assert secret not in joined


def test_personal_label_lines_and_their_value_on_the_next_line_are_dropped():
    lines = ["CUA HANG ABC", "Khach hangCoco Mart", "Ten KH: Nguyen Van A", "Nguoi nhan:", "Tran Thi B", "Thu ngan: tn7",
             "NV01735-Au Ng6 Ngoc Dieu", "Dia chi Masteri toa B vinhome Grand Park", "STK0421.003.753.163-Vietcombank-Nguyen Thi Ha",
             "Ca phe sua da 2 x 30.000"]
    assert sanitize_lines(lines) == ["CUA HANG ABC", "Ca phe sua da 2 x 30.000"]


def test_amounts_dates_products_and_shop_names_are_kept():
    keep = ["HIGHLANDS COFFEE", "Mon A 1 20.000 20.000", "TONG CONG 1.250.000", "Ngay 04/10/2026 09:46", "Khoai mi nuong (cai le)",
            "SP002993", "Thanh tien: 570,500", "5,000 x 15 75,000"]
    assert sanitize_lines(keep) == keep


def test_short_labels_do_not_eat_ordinary_names():
    keep = ["FPT Telecom", "Telecom Viet Nam", "Dtaphone Store", "NVIDIA Store", "Stkplus Mart"]
    assert sanitize_lines(keep) == keep
    assert sanitize_lines(["Tel: 0281234567", "NV: Ba", "STK: 123", "SDT 090 123 4567", "Email: a@b.vn"]) == []


def test_person_name_lines_are_dropped_but_a_shop_called_sieu_thi_is_not():
    assert sanitize_lines(["HO THIHOANG NGA", "SIEU THI DUC THANH"]) == ["SIEU THI DUC THANH"]


def test_sanitizing_twice_gives_the_same_result():
    lines = ["DT093.636.6628", "Khach hang: A", "Tong 100.000", "a@b.vn"]
    once = sanitize_lines(lines)
    assert sanitize_lines(once) == once


def test_a_name_on_its_own_line_between_contact_and_address_labels_is_dropped_even_when_ocr_garbles_the_labels():
    # bill Ngô Gia: cột giá trị tách khỏi nhãn nên tên khách đứng một mình giữa hai nhãn bị OCR sai chữ
    for phone_label in ("Sg dién thoal", "S6 dlen thoal", "So dien thoai"):
        out = sanitize_lines(["Phiếu thanh toán", phone_label, "Vu Thanh Tung", "Da chi", "T.NganNhan Vien 1", "19,000"])
        assert "Vu Thanh Tung" not in out
        assert "19,000" in out and "Phiếu thanh toán" in out
    assert "Le Minh Chau" not in sanitize_lines(["Khach hang", "Le Minh Chau", "19,000"])  # ngay sau nhãn khách hàng
    assert "Le Minh Chau" not in sanitize_lines(["Ca phe sua", "Le Minh Chau", "Dia chi:", "19,000"])  # ngay trước nhãn địa chỉ


def test_names_of_shops_and_dishes_are_kept_when_no_contact_or_address_label_is_next_to_them():
    keep = ["Highlands Coffee", "Tra Sua Tran Chau", "Thach Dua Nguyen Vi", "Banh Mi Thit Nguoi", "19,000", "Dich vu giao hang", "Pho Bo Tai Nam"]
    assert sanitize_lines(keep) == keep
    assert sanitize_lines(["Dien may Xanh", "Hang chinh hang", "Tong cong 1.250.000"]) == ["Dien may Xanh", "Hang chinh hang", "Tong cong 1.250.000"]


def test_a_label_that_already_carries_its_value_does_not_make_the_next_line_a_name():
    keep = ["ien thoai[so-dai]", "HÒA ĐƠN ĐẠT HÀNG", "Ma don 15464"]  # bill thật: dòng tiêu đề đứng sau dòng điện thoại
    assert "HÒA ĐƠN ĐẠT HÀNG" in sanitize_lines(keep)


def test_street_addresses_are_dropped_even_when_ocr_garbles_them():
    lines = ["ĐC: 12 Nguyễn Trãi, TP.HCM", "DC17 V Van Tan,TP.HCM", "C210 Cach Mang Thang 8TP.HCM", "Dia chl20 Thanh Mau.P7.TP.Da Lal-Thanh",
             "Ton&45A Ly Tu Trong,Quan 1, TP.HCM.", "Phuong Hai Ba Trung,Ha Noi", "Hồ Chí Minh", "TPHCM", "19,000", "Tổng cộng 22,000đ"]
    assert sanitize_lines(lines) == ["19,000", "Tổng cộng 22,000đ"]


def test_house_number_line_next_to_an_address_line_is_dropped_but_a_short_item_is_kept():
    # bill Ngô Gia BH00001: địa chỉ bị OCR xé thành 4 dòng
    out = sanitize_lines(["H472 - 85 Hoàng Hữu Nam", "phung Tang Nhon Phu,TP", "Hồ Chí Minh", "185 Hoang Hru Nam", "Phiếu thanh toán", "1 Chai", "19,000"])
    assert out == ["Phiếu thanh toán", "1 Chai", "19,000"]


def test_places_inside_shop_and_item_names_are_kept():
    keep = ["Bệnh viện quận 7", "Vé máy bay Hà Nội - TP.HCM", "Ve may bay Ha N8i - TP.HCM", "Điện lực Hồ Chí Minh", "CÔNG TY TNHH SX TM GẠO VIỆT NAM", "Quần jean nam ống đứng",
            "Phí cầu đường BOT", "Quán cơm tấm Cô Ba", "2 Ca phe sua", "10 Banh bao nhan thit"]
    assert sanitize_lines(keep) == keep
