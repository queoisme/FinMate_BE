"""Bộ khớp pattern khi định dạng khai báo ánh xạ group, trường phụ và ưu tiên (không cần DB)."""

from app.utils.provider_patterns import ExtraField, PatternMatcher, PatternSpec

PACKAGE = "com.test.bank"
BODY = "GD 75,000VND ma ABC tk 123456789012 tai HIGHLANDS"
REGEX = r"GD (?P<tien>[\d,.]+)VND ma (?P<ref>[A-Z]+) tk (?P<acct>\d+) tai (?P<noi>[A-Z ]+)"


def spec(**overrides) -> PatternSpec:
    fields = dict(provider_key="test", package_name=PACKAGE, pattern_name="p1", regex=REGEX, transaction_type="debit")
    return PatternSpec(**{**fields, **overrides})


def test_default_group_names_still_work_without_a_format():
    matched = PatternMatcher([spec(regex=r"GD (?P<amount>[\d,.]+)VND")]).match(PACKAGE, BODY)
    assert matched.amount_cents == 75_000 and matched.extra_fields == {}


def test_a_format_can_map_contract_fields_to_other_group_names():
    matcher = PatternMatcher([spec(groups=(("amount_cents", "tien"), ("merchant_name", "noi")))])

    matched = matcher.match(PACKAGE, BODY)

    assert (matched.amount_cents, matched.merchant_name) == (75_000, "HIGHLANDS")


def test_without_the_mapping_a_differently_named_amount_group_is_not_found():
    assert PatternMatcher([spec()]).match(PACKAGE, BODY) is None  # không có group "amount": pattern coi như không khớp, không bịa số tiền


def test_extra_fields_come_from_groups_or_constants():
    extras = (ExtraField("ref_code", "regex_group", "ref"), ExtraField("channel", "literal", "sms"), ExtraField("fee", "regex_group", "tien", data_type="amount"))
    matched = PatternMatcher([spec(groups=(("amount_cents", "tien"),), extras=extras)]).match(PACKAGE, BODY)

    assert matched.extra_fields == {"ref_code": "ABC", "channel": "sms", "fee": 75_000}


def test_extra_text_is_masked_even_if_the_format_forgot_to_flag_it():
    extras = (ExtraField("note", "regex_group", "acct"),)  # số tài khoản bị khai báo nhầm là trường phụ thường
    matched = PatternMatcher([spec(groups=(("amount_cents", "tien"),), extras=extras)]).match(PACKAGE, BODY)

    assert matched.extra_fields["note"] == "xxxxxxxx9012"  # lớp bảo vệ thứ hai: chỉ còn 4 số cuối


def test_missing_group_values_are_left_out_not_stored_empty():
    extras = (ExtraField("ghost", "regex_group", "khong_co_group"),)
    matched = PatternMatcher([spec(groups=(("amount_cents", "tien"),), extras=extras)]).match(PACKAGE, BODY)

    assert matched.extra_fields == {}


def test_lower_format_priority_is_tried_first_then_pattern_priority():
    broad = spec(pattern_name="broad", regex=r"GD (?P<amount>[\d,.]+)VND", priority=1, format_priority=50)
    narrow = spec(pattern_name="narrow", regex=r"GD (?P<amount>[\d,.]+)VND ma (?P<ref>[A-Z]+)", priority=99, format_priority=10)

    matched = PatternMatcher([broad, narrow]).match(PACKAGE, BODY)

    assert matched.pattern_name == "narrow"  # định dạng ưu tiên 10 thắng dù pattern của nó có priority 99
