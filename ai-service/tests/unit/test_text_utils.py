"""Parser tiền và thời gian — phần sai thì hỏng nặng nhất và im lặng nhất."""

from datetime import datetime, timedelta

import pytest

from app.utils.text_utils import (
    VIETNAM_TZ,
    find_amount_candidates,
    normalize_for_model,
    parse_datetime,
    parse_vnd_amount,
    pick_amount,
    select_amounts,
)


@pytest.mark.parametrize(
    ("raw", "expected"),
    [
        # Dấu chấm ngăn nghìn kiểu Việt Nam.
        ("75.000", 75_000),
        ("1.500.000", 1_500_000),
        # Dấu phẩy ngăn nghìn kiểu en-US — app ngân hàng dùng cả hai.
        ("75,000", 75_000),
        ("2,500,000", 2_500_000),
        # Hai chữ số cuối là phần thập phân, không phải nhóm nghìn.
        ("75,000.00", 75_000),
        ("1.234,56", 1_235),
        # Viết tắt.
        ("75k", 75_000),
        ("1,5tr", 1_500_000),
        ("1.5tr", 1_500_000),
        ("2tr5", 2_500_000),
        ("75k5", 75_500),
        ("2 triệu", 2_000_000),
        ("500 nghìn", 500_000),
        ("1 tỷ", 1_000_000_000),
        # Nhiễu ký hiệu tiền tệ và dấu.
        ("-75,000VND", 75_000),
        ("+15.000.000đ", 15_000_000),
        ("75.000 VNĐ", 75_000),
        # Không đọc được.
        ("abc", None),
        ("", None),
        (None, None),
    ],
)
def test_parse_vnd_amount(raw, expected):
    assert parse_vnd_amount(raw) == expected


def test_thousand_separator_is_not_a_decimal_point():
    """Bẫy 1000 lần: đọc "75.000" theo kiểu en-US ra 75.0 — vẫn là số hợp lệ nên không
    có exception nào nổ, chỉ có giao dịch sai số tiền."""
    assert parse_vnd_amount("75.000") == 75_000
    assert parse_vnd_amount("75.000") != 75


def test_pick_amount_prefers_value_with_unit():
    # "tháng 9" là số thứ tự, "15tr" mới là tiền.
    assert pick_amount("nhận lương tháng 9 15tr từ công ty ABC").value == 15_000_000


def test_select_amounts_separates_balance_from_transaction_amount():
    """Số dư gần như luôn LỚN HƠN số tiền giao dịch, nên lấy giá trị lớn nhất là lấy
    đúng con số sai."""
    selection = select_amounts(
        "Quy khach vua thanh toan 320,000 VND. So du 1,000,000 VND"
    )
    assert selection.amount.value == 320_000
    assert selection.balance.value == 1_000_000


def test_select_amounts_without_balance():
    selection = select_amounts("trưa nay ăn phở 45k ở Phở Thìn")
    assert selection.amount.value == 45_000
    assert selection.balance is None


@pytest.mark.parametrize(
    ("raw", "expected"),
    [
        ("10:28 09/09/2026", datetime(2026, 9, 9, 10, 28, tzinfo=VIETNAM_TZ)),
        ("09/09/2026 10:28", datetime(2026, 9, 9, 10, 28, tzinfo=VIETNAM_TZ)),
        ("09-09-2026 19:05:00", datetime(2026, 9, 9, 19, 5, 0, tzinfo=VIETNAM_TZ)),
        ("2026-09-09T10:28:00", datetime(2026, 9, 9, 10, 28, tzinfo=VIETNAM_TZ)),
        # Năm 2 chữ số — MB viết "20/08/24".
        ("20/08/24 15:30", datetime(2024, 8, 20, 15, 30, tzinfo=VIETNAM_TZ)),
        ("09.09.2026 10:28", datetime(2026, 9, 9, 10, 28, tzinfo=VIETNAM_TZ)),
    ],
)
def test_parse_datetime_formats(raw, expected):
    reference = datetime(2026, 9, 9, 12, 0, tzinfo=VIETNAM_TZ)
    assert parse_datetime(raw, reference) == expected


def test_parse_datetime_rolls_year_back_across_new_year():
    """Thông báo "31/12 23:50" nhận được lúc 01/01 thuộc về năm TRƯỚC.

    Lấy năm hiện tại sẽ đẩy giao dịch tới gần một năm trong tương lai — và nó sẽ biến mất
    khỏi mọi báo cáo tháng."""
    reference = datetime(2027, 1, 1, 0, 5, tzinfo=VIETNAM_TZ)
    parsed = parse_datetime("31/12 23:50", reference)
    assert parsed == datetime(2026, 12, 31, 23, 50, tzinfo=VIETNAM_TZ)
    assert parsed < reference
    assert reference - parsed < timedelta(hours=1)


def test_parse_datetime_returns_none_when_absent():
    reference = datetime(2026, 9, 9, 12, 0, tzinfo=VIETNAM_TZ)
    assert parse_datetime("khong co ngay thang gi", reference) is None
    assert parse_datetime(None, reference) is None


def test_normalize_for_model_collapses_numbers():
    """Số tiền cụ thể là nhiễu với bài toán phân loại; giữ nguyên thì model học thuộc
    lòng đúng những con số có trong tập train."""
    normalized = normalize_for_model("TK 123: -75,000VND tai Highlands Coffee")
    assert "75,000" not in normalized
    assert "<num>" in normalized
    assert "highlands coffee" in normalized


def test_dash_separated_date_is_parsed():
    """Vietcombank viết ngày bằng dấu gạch. Không nhận dạng được thì parse_datetime trả
    None và Extractor âm thầm dùng thời điểm NHẬN thông báo thay cho thời điểm giao dịch —
    sai ngày, sai chu kỳ ngân sách, không có lỗi nào nổ ra."""
    reference = datetime(2026, 9, 10, 8, 0, tzinfo=VIETNAM_TZ)
    assert parse_datetime("09-09-2026 19:05:00", reference) == datetime(
        2026, 9, 9, 19, 5, tzinfo=VIETNAM_TZ
    )


# --------------------------------------------------------------- OCR nhả dấu cách


def _values(text: str) -> list[int]:
    return [c.value for c in find_amount_candidates(text)]


def test_space_after_thousand_separator_is_still_one_amount():
    """Tesseract đọc "100,000" trên hóa đơn thành "100, 000".

    Regex dừng ở dấu cách sẽ chỉ lấy "100" — tức là 100 đồng thay vì 100.000 đồng, sai
    1000 lần mà vẫn trả về một con số hợp lệ nên không có gì báo động.
    """
    assert _values("TONG CONG: 100, 000") == [100_000]
    assert _values("Tien mat: 200, 000") == [200_000]


def test_a_quantity_before_a_price_does_not_merge_into_it():
    """Hàng hóa đơn thường là "<tên> <số lượng> <đơn giá>".

    Nới dấu cách quá tay sẽ biến "Vinamilk 2 58,000" thành 258.000 — vẫn là một con số
    trông hợp lệ, và lần này sai theo chiều NGƯỢC lại.
    """
    assert _values("Sua tuoi Vinamilk 2 58,000") == [2, 58_000]


def test_a_spaced_pair_that_is_not_a_thousand_group_stays_apart():
    """Chỉ đúng 3 chữ số sau dấu ngăn mới là nhóm nghìn; "14, 09" là ngày, không phải tiền."""
    assert _values("Ngay: 14, 09")[:2] == [14, 9]


def test_grouping_flag_survives_the_space():
    """Cờ has_grouping là thứ phân biệt tiền với mã số thuế — dấu cách không được làm mất nó."""
    candidate = find_amount_candidates("TONG CONG: 100, 000")[0]
    assert candidate.has_grouping is True
