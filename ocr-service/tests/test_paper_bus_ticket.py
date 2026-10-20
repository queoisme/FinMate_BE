"""Phiếu lên xe khách bằng giấy (Vexere): trước đây bị từ chối "không giống hóa đơn" vì không có từ vựng hóa đơn và nhãn "TT:" chưa được nhận là số phải trả."""
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))

import app_screens

# Chữ OCR thật của ảnh phiếu (giữ nguyên lỗi OCR); tên hành khách và số điện thoại đã thay giá trị giả.
TICKET = [
    "CINIiNECONG.IAHHMTVDICHVL", "VILTIANPHIIMDAAMOMG", "Fngdi190.888.606", "PHIẾU LÊN XE", "Quetma QR nadd lenxe",
    "Bn diBXMien DngBeneMien ong", "Benden RKranyNllTy", "so giumgB09", "MVVFF22", "G300.000", "MaMDKN181026/1",
    "Khoi hamh.08:00", "18102026", "Bien6xe50H-807.80", "Ten HKNguyen Van A", "S0900000000/", "Nam", "NThu len", "TT:260.000",
    "Vexere", "17:442-18/10/2026", "\"Luuy:", "-Qukhachv iy. dtaat3phm", "-Hanh ty gu hopgnhhqui 20K",
    "De tra cuu thong tin dicu iui long quet QR",
]


def test_paper_bus_ticket_is_recognised_and_reads_the_amount_paid():
    screen = app_screens.parse(TICKET)
    assert screen is not None
    assert screen["screen_type"] == "bus_ticket"
    assert screen["total_amount"] == 260_000  # "TT:" là tiền thanh toán, không phải giá vé 300.000
    assert screen["merchant"] == "Vexere"
    assert screen["category"] == "transport"


def test_nothing_personal_is_kept_for_learning():
    assert app_screens.parse(TICKET)["keep_lines"] == []  # có tên hành khách và số điện thoại


def test_without_the_paid_label_the_ticket_price_is_used_and_flagged_for_review():
    lines = [l for l in TICKET if not l.startswith("TT:")]
    screen = app_screens.parse(lines)
    assert screen["total_amount"] == 300_000
    assert screen["force_review"] is True


def test_a_hotline_is_never_taken_as_the_amount():
    lines = [l for l in TICKET if not l.startswith(("TT:", "G300"))]
    assert app_screens.parse(lines)["total_amount"] is None


def test_the_label_may_be_missing_one_signal_and_still_be_recognised():
    # OCR mất chữ "Vexere" và dòng nhãn: còn tiêu đề + "Tên HK" + "TT:" là đủ.
    lines = [l for l in TICKET if l not in ("Vexere", "Quetma QR nadd lenxe")]
    assert app_screens.parse(lines)["total_amount"] == 260_000


def test_an_ordinary_receipt_is_not_a_bus_ticket():
    receipt = ["COCOMART", "HÓA ĐƠN TẠM TÍNH", "Xuc xich lac heo 12,000", "Nuoc Pocari 32,000", "Tổng thanh toán", "452,432", "Cam on quy khach"]
    assert app_screens.parse(receipt) is None


def test_one_word_alone_is_not_enough():
    assert app_screens.parse(["Vexere", "Cam on quy khach", "Tong cong 50,000"]) is None
    assert app_screens.parse(["PHIẾU LÊN XE"]) is None
