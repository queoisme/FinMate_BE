"""Ngày giao dịch từ chữ OCR, gồm các dạng thật đã gặp trên bill của người dùng."""
from datetime import datetime

import pytest

from dates import VN, find_date

NOW = datetime(2026, 10, 8, 20, 0, tzinfo=VN)


@pytest.mark.parametrize(
    ("lines", "expected"),
    [
        (["HIGHLANDS COFFEE", "Ngay11-04-202613:16", "Tong tien: 16.000"], datetime(2026, 4, 11, 13, 16, tzinfo=VN)),  # dính liền, thật
        (["Ngay 06 thang 10 nam 2026", "Tong 1,305,000"], datetime(2026, 10, 6, 12, 0, tzinfo=VN)),  # Coco Mart
        (["Ngày 06 tháng 10 năm 2026"], datetime(2026, 10, 6, 12, 0, tzinfo=VN)),
        (["Ngay: 11/04/2022 10:04"], datetime(2022, 4, 11, 10, 4, tzinfo=VN)),
        (["Ngày vào viện: 05:39 11/05/2026"], datetime(2026, 5, 11, 12, 0, tzinfo=VN)),
        (["14:34 Ngay21 thang 05nam2026"], datetime(2026, 5, 21, 12, 0, tzinfo=VN)),
        (["Ngay 3.9.26"], datetime(2026, 9, 3, 12, 0, tzinfo=VN)),
    ],
)
def test_real_date_formats(lines, expected):
    assert find_date(lines, NOW) == expected


def test_no_date_gives_none():
    assert find_date(["HIGHLANDS COFFEE", "Tong 16.000"], NOW) is None


def test_the_latest_plausible_date_wins_over_an_earlier_one():
    assert find_date(["Ngay vao: 11/05/2026", "Ngay ra: 15/05/2026"], NOW) == datetime(2026, 5, 15, 12, 0, tzinfo=VN)


def test_ranges_and_due_dates_are_not_the_transaction_date():
    lines = ["Phi quan ly T05/2026 (Tu 01/05/2026 den 31/05/2026)", "Thanh toan truoc ngay 20/06/2026", "Ngay 21 thang 05 nam 2026"]
    assert find_date(lines, NOW) == datetime(2026, 5, 21, 12, 0, tzinfo=VN)


def test_future_dates_are_ocr_mistakes():
    assert find_date(["Ngay 11/04/2062"], NOW) is None
    assert find_date(["Ngay 09/10/2026"], NOW) is not None  # ngày mai vẫn được phép (lệch múi giờ)


@pytest.mark.parametrize("line", ["Ngay 31/02/2026", "Ngay 11/13/2026", "Ngay 11/04/1998", "SDT 0901.234.567"])
def test_impossible_or_irrelevant_numbers_are_not_dates(line):
    assert find_date([line], NOW) is None


@pytest.mark.parametrize(
    ("line", "expected"),
    [
        ("2026-10-09 13:38", datetime(2026, 10, 9, 13, 38, tzinfo=VN)),
        ("2026-10-0913:38", datetime(2026, 10, 9, 13, 38, tzinfo=VN)),  # OCR dính ngày với giờ (bill Ngô Gia)
        ("2026/10/07", datetime(2026, 10, 7, 12, 0, tzinfo=VN)),
    ],
)
def test_year_first_dates_from_pos_machines(line, expected):
    assert find_date([line], datetime(2026, 10, 9, 20, 0, tzinfo=VN)) == expected


def test_time_on_the_adjacent_line_is_attached_to_the_date():
    # bill Ministop: OCR đọc "12:31:13" và "11/10/2026" thành hai dòng liền nhau
    now = datetime(2026, 10, 11, 20, 0, tzinfo=VN)
    expected = datetime(2026, 10, 11, 12, 31, tzinfo=VN)
    assert find_date(["Hoa don Ban hang", "12:31:13", "11/10/2026", "19,000"], now) == expected
    assert find_date(["11/10/2026", "12:31", "19,000"], now) == expected


def test_adjacent_time_is_ignored_when_it_is_not_a_bare_time_or_the_date_has_its_own_time():
    now = datetime(2026, 10, 11, 20, 0, tzinfo=VN)
    assert find_date(["Gio vao 12:31 den 13:00", "11/10/2026"], now) == datetime(2026, 10, 11, 12, 0, tzinfo=VN)  # dòng giờ có chữ: không phải giờ trần
    assert find_date(["09:05", "11/10/2026 13:16"], now) == datetime(2026, 10, 11, 13, 16, tzinfo=VN)  # ngày đã có giờ riêng
    assert find_date(["25:99", "11/10/2026"], now) == datetime(2026, 10, 11, 12, 0, tzinfo=VN)  # giờ không hợp lệ
