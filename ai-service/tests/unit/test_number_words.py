"""Số tiền đọc bằng chữ — đầu vào của luồng Voice.

Speech-to-Text trả về đúng những gì người dùng nói, nên nếu module này sai thì câu nói mẫu
của chính tài liệu thiết kế cũng không bóc ra được số tiền.
"""

import pytest

from app.utils.number_words import find_number_words, parse_number_words


@pytest.mark.parametrize(
    ("spoken", "expected"),
    [
        # Ví dụ nguyên văn trong FinMate_Core_User_Flows.docx phương thức 2.
        ("Cà phê Highland bốn mươi lăm ngàn", 45_000),
        ("hai mươi lăm nghìn", 25_000),
        ("một trăm năm mươi ngàn", 150_000),
        ("năm chục nghìn", 50_000),
        ("một trăm lẻ năm nghìn", 105_000),
        ("ba tỷ", 3_000_000_000),
        # Biến thể của 1/4/5 khi đứng sau hàng chục.
        ("hai mươi mốt nghìn", 21_000),
        ("hai mươi tư nghìn", 24_000),
        ("hai mươi nhăm nghìn", 25_000),
        # "rưỡi" = một nửa của hàng vừa dùng.
        ("hai triệu rưỡi", 2_500_000),
        ("một tỷ rưỡi", 1_500_000_000),
        # Chữ số lẻ ngay sau đơn vị là phần mười của đơn vị đó.
        ("một triệu hai", 1_200_000),
        ("hai triệu tám", 2_800_000),
    ],
)
def test_spoken_amounts(spoken, expected):
    assert parse_number_words(spoken) == expected


def test_muoi_is_ten_not_a_multiplier():
    """ "mười" (10) và "mươi" (nhân 10) chỉ khác nhau ở dấu.

    Gộp chúng lại — điều xảy ra nếu bỏ dấu trước khi khớp — biến
    "tháng chín mười lăm triệu" thành 95,4 triệu thay vì 15 triệu.
    """
    assert parse_number_words("mười lăm triệu") == 15_000_000
    assert parse_number_words("hai mươi lăm triệu") == 25_000_000
    assert parse_number_words("nhận lương tháng chín mười lăm triệu") == 15_000_000


@pytest.mark.parametrize(
    "text",
    [
        # "từ" ≠ "tư"(4), "ty" trong "công ty" ≠ "tỷ".
        "chuyển từ công ty ABC",
        # "làm" ≠ "lăm"(5).
        "làm việc cả ngày",
        # "sau" ≠ "sáu"(6).
        "sau đó đi về",
    ],
)
def test_words_that_only_collide_when_accents_are_stripped(text):
    assert find_number_words(text) == []


def test_bare_quantity_loses_to_the_amount_with_a_unit():
    """ "ba ly cà phê" — "ba" là số lượng, "bốn mươi lăm ngàn" mới là tiền."""
    assert parse_number_words("ba ly cà phê bốn mươi lăm ngàn") == 45_000


def test_longest_readable_group_wins():
    matches = find_number_words("một trăm năm mươi ngàn")
    assert [m.value for m in matches] == [150_000]


def test_offsets_point_into_the_original_accented_string():
    """Extractor cắt phần sau số tiền để lấy tên cửa hàng, nên vị trí phải đúng trên chuỗi
    GỐC chứ không phải trên một bản đã biến đổi."""
    text = "ba ly cà phê bốn mươi lăm ngàn"
    match = next(m for m in find_number_words(text) if m.value == 45_000)
    assert text[match.start : match.end] == "bốn mươi lăm ngàn"


def test_no_silent_thousand_multiplier():
    """ "hai trăm rưỡi" trả về 250, không phải 250.000.

    Đời thường người Việt nói vậy thường có ý 250 nghìn, nhưng tự thêm một dấu nhân 1000 là
    đúng loại lỗi tệ nhất của ứng dụng này: một con số tiền sai gấp nghìn lần mà vẫn hợp lệ.
    """
    assert parse_number_words("hai trăm rưỡi") == 250
    assert parse_number_words("hai trăm rưỡi nghìn") == 250_000


@pytest.mark.parametrize("text", ["hôm nay trời đẹp", "", "abc xyz", "45k"])
def test_returns_none_when_there_is_no_number_word(text):
    assert parse_number_words(text) is None
