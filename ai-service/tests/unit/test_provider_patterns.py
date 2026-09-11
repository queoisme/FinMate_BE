"""Regex bóc trường theo provider."""

import pytest

from app.utils.provider_patterns import PatternMatcher


def test_every_active_pattern_still_matches_its_own_sample(pattern_specs, matcher):
    """Mỗi pattern mang theo chính mẫu nó được viết ra từ đó.

    Đây là lưới an toàn cho việc sửa regex: một thay đổi làm hỏng template cũ sẽ đỏ ở đây
    thay vì lộ ra khi giao dịch của người dùng không còn được nhận diện.
    """
    for spec in pattern_specs:
        assert spec.sample_text, f"{spec.pattern_name} thiếu sample_text"
        matched = matcher.match(spec.package_name, spec.sample_text)
        assert matched is not None, f"{spec.pattern_name} không khớp mẫu của chính nó"
        assert matched.pattern_name == spec.pattern_name
        assert matched.amount_cents > 0


def test_all_five_providers_are_covered(pattern_specs):
    packages = {spec.package_name for spec in pattern_specs}
    assert packages == {
        "com.mbmobile",
        "com.VCB",
        "com.mservice.momotransfer",
        "vn.com.vng.zalopay",
        "vn.vnpay.vnpayewallet",
    }


def test_unknown_package_matches_nothing(matcher: PatternMatcher):
    assert matcher.match("com.unknown.bank", "GD: -75,000VND") is None


def test_mb_qr_pattern_wins_over_generic_by_priority(matcher: PatternMatcher):
    """Template hẹp phải thắng template rộng, nếu không tên đơn vị bán không bao giờ
    được bóc ra."""
    matched = matcher.match(
        "com.mbmobile",
        "TK 0123456789|GD: -75,000VND 09/09/2026 10:28|SD: 2,500,000VND|ND: TT QR HIGHLANDS COFFEE",
    )
    assert matched.pattern_name == "mb_qr_v1"
    assert matched.merchant_name == "HIGHLANDS COFFEE"


def test_sign_group_decides_direction(matcher: PatternMatcher):
    debit = matcher.match(
        "com.mbmobile",
        "TK 0123456789|GD: -75,000VND 09/09/2026 10:28|SD: 2,500,000VND|ND: ABC",
    )
    credit = matcher.match(
        "com.mbmobile",
        "TK 0123456789|GD: +75,000VND 09/09/2026 10:28|SD: 2,500,000VND|ND: ABC",
    )
    assert debit.transaction_type.value == "debit"
    assert credit.transaction_type.value == "credit"


def test_pattern_that_matches_but_yields_no_amount_is_rejected():
    """Regex khớp nhưng số tiền không đọc được nghĩa là pattern sai, không phải thông báo
    lạ — phải để matcher thử tiếp thay vì dựng giao dịch 0 đồng."""
    from app.utils.provider_patterns import PatternSpec

    broken = PatternSpec(
        provider_key="x",
        package_name="com.x",
        pattern_name="broken",
        regex=r"GD: (?P<amount>[a-z]+)VND",
    )
    assert PatternMatcher([broken]).match("com.x", "GD: abcVND") is None


@pytest.mark.parametrize(
    ("package", "body", "amount", "merchant"),
    [
        (
            "com.mservice.momotransfer",
            "Bạn đã thanh toán 75.000đ cho Highlands Coffee. Số dư: 2.500.000đ",
            75_000,
            "Highlands Coffee",
        ),
        (
            "vn.com.vng.zalopay",
            "Thanh toán thành công 45.000đ tại VinMart+ Nguyen Trai. Số dư ví: 820.000đ",
            45_000,
            "VinMart+ Nguyen Trai",
        ),
    ],
)
def test_wallet_patterns(matcher, package, body, amount, merchant):
    matched = matcher.match(package, body)
    assert matched.amount_cents == amount
    assert matched.merchant_name == merchant
