import json
import sys, pathlib

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))

import pytest

from parsing.extract import receipt_score, MIN_RECEIPT_SCORE, CONFIDENT_RECEIPT_SCORE

FIX = pathlib.Path(__file__).parent / "fixtures" / "sample_ocr_lines.json"
SAMPLES = json.loads(FIX.read_text(encoding="utf-8"))

NOT_RECEIPTS = {
    "ap phich": ["SALE 50% OFF", "MUA 1 TANG 1", "Chi tu 199.000d", "Con 99.000d", "Hotline 1900 5555", "www.shop.vn"],
    "can cuoc": ["CONG HOA XA HOI CHU NGHIA VIET NAM", "Doc lap - Tu do - Hanh phuc", "CAN CUOC CONG DAN", "So: 079201012345",
                 "Ho va ten: NGUYEN VAN A", "Ngay sinh: 01/01/2000", "Que quan: Ha Noi"],
    "thuc don": ["THUC DON", "Ca phe sua 35.000", "Ca phe den 30.000", "Tra dao 45.000", "Banh mi 25.000",
                 "Sinh to bo 40.000", "Nuoc cam 35.000"],
    "chat": ["Hom nay an gi?", "Pho di", "45k nhe", "Ok toi 12h", "Dc roi"],
    "trang sach": ["Chuong 3. Co so ly thuyet", "Quy tac 50/30/20 la mot phuong phap quan ly", "tai chinh ca nhan do Elizabeth Warren de xuat",
                   "Theo do thu nhap duoc chia thanh ba phan", "can thiet, mong muon va tiet kiem", "Tai lieu tham khao"],
    "bien so": ["29A-123.45"],
    "rong": [],
}


@pytest.mark.parametrize("name", SAMPLES)
def test_every_real_receipt_fixture_scores_as_a_receipt(name):
    lines = [t for t, _ in SAMPLES[name]]
    assert receipt_score(lines) >= MIN_RECEIPT_SCORE, name


@pytest.mark.parametrize("name", NOT_RECEIPTS)
def test_non_receipt_texts_score_below_the_threshold(name):
    assert receipt_score(NOT_RECEIPTS[name]) < MIN_RECEIPT_SCORE, name


def test_a_label_found_total_counts_more_than_a_repeated_amount_guess():
    menu = NOT_RECEIPTS["thuc don"]                     # giá lặp 35.000 nhưng không có nhãn Tổng
    with_label = menu + ["Tong cong 210.000"]
    assert receipt_score(with_label) > receipt_score(menu)


def test_confident_threshold_is_above_the_reject_threshold():
    assert CONFIDENT_RECEIPT_SCORE == MIN_RECEIPT_SCORE + 1
