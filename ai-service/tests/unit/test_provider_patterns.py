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


def test_seeded_providers_are_covered(pattern_specs):
    packages = {spec.package_name for spec in pattern_specs}
    # Tập tối thiểu phải có: thêm ngân hàng mới vào seed không làm test này đỏ.
    assert packages >= {
        "com.mbmobile",
        "com.VCB",
        "com.mservice.momotransfer",
        "vn.com.vng.zalopay",
        "vn.vnpay.vnpayewallet",
        "com.vnpay.bidv",
        "com.tpb.mb.gprsandroid",
        "com.vnpay.Agribank3g",
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
    assert matched.pattern_name == "mb_qr_v2"
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


def _spec(regex_text: str, name: str = "p"):
    from app.utils.provider_patterns import PatternSpec

    return PatternSpec(provider_key="x", package_name="com.x", pattern_name=name, regex=regex_text)


def test_unaccented_regex_matches_accented_text_and_keeps_the_accents_in_groups():
    matcher = PatternMatcher([_spec(r"So du (?P<amount>[\d.,]+) VND\. Noi dung: (?P<description>.+?)\s*$")])
    matched = matcher.match("com.x", "Số dư 50,000 VND. Nội dung: Chuyển tiền ăn trưa đồng nghiệp")
    assert matched.amount_cents == 50_000
    assert matched.description == "Chuyển tiền ăn trưa đồng nghiệp"  # chữ gốc, còn dấu, kể cả đ


def test_accented_regex_still_matches_the_raw_text_first():
    matcher = PatternMatcher([_spec(r"Số dư (?P<amount>[\d.,]+)")])
    assert matcher.match("com.x", "Số dư 70,000").amount_cents == 70_000
    assert matcher.match("com.x", "So du 70,000") is None  # regex có dấu không tự dung nạp chữ không dấu: chỉ chiều ngược lại


def test_real_accented_vcb_notification_matches_the_unaccented_pattern(matcher: PatternMatcher):
    matched = matcher.match(
        "com.VCB", "Số dư TK VCB 1038646763 +20,000,000 VND lúc 19-07-2024 10:19:38. Số dư 20,550,000 VND. Ref 213085.190724.101936.SW494931287"
    )
    assert (matched.pattern_name, matched.amount_cents, matched.balance_after_cents) == ("vcb_v1", 20_000_000, 20_550_000)
    assert matched.transaction_type.value == "credit"


def test_accented_bidv_narrative_keeps_its_accents(matcher: PatternMatcher):
    matched = matcher.match(
        "com.vnpay.bidv",
        "Thời gian giao dịch: 09:05 29/12/2025.\nTài khoản thanh toán: 8863207475\nSố tiền GD: -50,000 VND\nSố dư cuối: 120,000 VND\nNội dung giao dịch: Chuyển tiền ăn trưa\nMã giao dịch: 0002AbCd",
    )
    assert (matched.pattern_name, matched.amount_cents) == ("bidv_v1", 50_000)
    assert matched.description == "Chuyển tiền ăn trưa"


def test_real_vietinbank_debit_notification(matcher: PatternMatcher):
    """§16.2 — mẫu thật trên máy: SDC (số dư) không được lẫn vào amount, và tiền tố 'CT DI:<số>'
    trước tên người nhận phải bị lọc khỏi merchant."""
    matched = matcher.match(
        "com.vietinbank.ipay",
        "VietinBank:08/10/2026 11:08\nTK:107876452515\nGD:-5,000 VND\nSDC:2,860,075 VND\n"
        "ND:CT DI:628111031960 LE XUAN THO Chuyen tien; tai iPay",
    )
    assert (matched.pattern_name, matched.amount_cents, matched.balance_after_cents) == (
        "vietinbank_v3", 5_000, 2_860_075,
    )
    assert matched.transaction_type.value == "debit"
    assert matched.merchant_name == "LE XUAN THO Chuyen tien; tai iPay"


def test_real_vietinbank_credit_notification_has_no_ct_di_prefix(matcher: PatternMatcher):
    """Credit không có tiền tố 'CT DI:' — regex vẫn phải khớp và không lẫn SDC vào amount.
    Đây CHÍNH LÀ residual gap đã biết (§16.8): không có 'CT DI:'/'CT DEN:' nào để lọc nên
    tham chiếu alphanumeric vẫn còn dính trong merchant — chấp nhận, không xử lý thêm."""
    matched = matcher.match(
        "com.vietinbank.ipay",
        "VietinBank:19/10/2026 14:34\nTK:107876452515\nGD:+5,000 VND\nSDC:78,975 VND\n"
        "ND:504D610198WKW4EV LE XUAN THO chuyen tien (0889341474)",
    )
    assert (matched.pattern_name, matched.amount_cents, matched.balance_after_cents) == (
        "vietinbank_v3", 5_000, 78_975,
    )
    assert matched.transaction_type.value == "credit"
    assert matched.merchant_name == "504D610198WKW4EV LE XUAN THO chuyen tien (0889341474)"


def test_real_vietinbank_qr_debit_notification_strips_alphanumeric_reference_and_qr_dash(matcher: PatternMatcher):
    """§16.8 — biến thể QR: tham chiếu alphanumeric (không chỉ số) + token 'QR - ' phải bị lọc."""
    matched = matcher.match(
        "com.vietinbank.ipay",
        "VietinBank:09/09/2026 11:08\nTK:107876452515\nGD:-5,000 VND\nSDC:2,860,075 VND\n"
        "ND:CT DI:504K26B0LGD04NYZ QR - LE XUAN THO Chuyen tien; tai iPay",
    )
    assert (matched.pattern_name, matched.amount_cents, matched.balance_after_cents) == (
        "vietinbank_v3", 5_000, 2_860_075,
    )
    assert matched.merchant_name == "LE XUAN THO Chuyen tien; tai iPay"


def test_real_vietinbank_credit_with_ct_den_prefix(matcher: PatternMatcher):
    """§16.8 — credit có thể mang tiền tố 'CT DEN:' song song với 'CT DI:' của debit."""
    matched = matcher.match(
        "com.vietinbank.ipay",
        "VietinBank:19/10/2026 14:34\nTK:107876452515\nGD:+5,000 VND\nSDC:78,975 VND\n"
        "ND:CT DEN:504T26B0LG7RAJFF LE XUAN THO chuyen tien",
    )
    assert (matched.pattern_name, matched.amount_cents, matched.balance_after_cents) == (
        "vietinbank_v3", 5_000, 78_975,
    )
    assert matched.transaction_type.value == "credit"
    assert matched.merchant_name == "LE XUAN THO chuyen tien"


def test_mb_debit_notification_with_den_segment_strips_it_from_merchant(matcher: PatternMatcher):
    """§16.7 — MB thật chèn |DEN: (debit) hoặc |TU: (credit) giữa SD: và ND:; không có group
    cho đoạn này thì toàn bộ ND: (gồm cả DEN:/TU:) sẽ lẫn vào description/merchant."""
    matched = matcher.match(
        "com.mbmobile",
        "TK 00xxx130|GD: -5,000VND 19/10/26 14:34 |SD: 95,894VND|DEN: LE XUAN THO - 0889341474|"
        "ND: MBCT LE XUAN THO chuyen tien DLLWIF57/844935",
    )
    assert (matched.pattern_name, matched.amount_cents, matched.balance_after_cents) == (
        "mb_generic_v2", 5_000, 95_894,
    )
    assert matched.transaction_type.value == "debit"
    assert matched.description == "MBCT LE XUAN THO chuyen tien DLLWIF57/844935"


def test_mb_credit_notification_with_tu_segment_strips_it_from_merchant(matcher: PatternMatcher):
    matched = matcher.match(
        "com.mbmobile",
        "TK 00xxx130|GD: +5,000VND 08/10/26 11:08 |SD: 3,465,980VND|TU: LE XUAN THO - 107876452515|"
        "ND: LE XUAN THO Chuyen tien cJI1SU2A/031960",
    )
    assert (matched.pattern_name, matched.amount_cents, matched.balance_after_cents) == (
        "mb_generic_v2", 5_000, 3_465_980,
    )
    assert matched.transaction_type.value == "credit"
    assert matched.description == "LE XUAN THO Chuyen tien cJI1SU2A/031960"
