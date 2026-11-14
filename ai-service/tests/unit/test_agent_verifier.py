"""Bộ kiểm chứng số của agent (Task 8): mọi số tiền trong câu trả lời phải bằng giá trị của bằng chứng được trích dẫn; số nào không có gốc là bịa."""

from datetime import datetime, timezone

import pytest

from app.agent.schemas import Claim, Evidence
from app.agent.verifier import claim_amounts, format_vnd, verify

AS_OF = datetime(2026, 11, 7, 3, 0, tzinfo=timezone.utc)


def ev(id_, value=None, text=None, **extra) -> Evidence:
    return Evidence(id=id_, key=id_, value_vnd=value, text=text, unit="VND", source="confirmed", as_of=AS_OF, confidence=0.9, **extra)


BALANCE = ev("balance", 1_500_000)
PRICE = ev("price", 600_000_000)
EVIDENCE = [BALANCE, PRICE, ev("verdict", None, "verdict: tight")]


def kinds(claims, evidence=EVIDENCE) -> list[tuple[int, str]]:
    return [(v.claim_index, v.kind) for v in verify(claims, evidence)]


def test_accepts_vn_rounded_format_equal_to_evidence():
    for text in ("Bạn đang có 1,5 triệu trong ví", "Bạn đang có 1.500.000 đồng", "Bạn đang có 1500000 đ", "Bạn đang có 1,5tr"):
        assert kinds([Claim(text=text, evidence_ids=["balance"])]) == [], text


def test_rejects_number_absent_from_evidence():
    assert kinds([Claim(text="Bạn đang có 1,6 triệu trong ví", evidence_ids=["balance"])]) == [(0, "number_unsupported")]


def test_a_number_must_match_the_evidence_the_claim_cites_not_just_any_evidence():
    claim = Claim(text="Giá mục tiêu là 1,5 triệu", evidence_ids=["price"])  # 1,5 triệu có trong bằng chứng khác (balance) nhưng không phải bằng chứng được trích

    assert kinds([claim]) == [(0, "number_unsupported")]


def test_every_amount_in_a_claim_is_checked():
    ok = Claim(text="Số dư 1,5 triệu, giá mục tiêu 600 triệu", evidence_ids=["balance", "price"])
    bad = Claim(text="Số dư 1,5 triệu, giá mục tiêu 700 triệu", evidence_ids=["balance", "price"])

    assert kinds([ok]) == []
    assert kinds([bad]) == [(0, "number_unsupported")]


def test_rejects_claim_without_evidence():
    assert kinds([Claim(text="Bạn nên tiết kiệm nhiều hơn", evidence_ids=[])]) == [(0, "no_evidence")]
    assert kinds([Claim(text="Bạn có 5 triệu", evidence_ids=[])]) == [(0, "no_evidence")]


def test_a_clarifying_question_needs_no_evidence():
    assert kinds([Claim(text="Bạn muốn mua trong bao lâu?", evidence_ids=[])]) == []


def test_rejects_unknown_evidence_id():
    assert kinds([Claim(text="Số dư là 1,5 triệu", evidence_ids=["balance", "made_up"])]) == [(0, "unknown_evidence_id")]


def test_words_without_money_are_fine_when_they_cite_evidence():
    assert kinds([Claim(text="Kết luận: cần cân nhắc, nhóm thu nhập ổn định, sau 24 tháng, tăng 5,50%/năm", evidence_ids=["verdict"])]) == []


def test_a_year_is_not_mistaken_for_money():
    assert kinds([Claim(text="Số liệu cập nhật tháng 11 năm 2026", evidence_ids=["balance"])]) == []


def test_each_claim_is_reported_by_its_index():
    claims = [Claim(text="Số dư 1,5 triệu", evidence_ids=["balance"]), Claim(text="Giá 9 triệu", evidence_ids=["price"]), Claim(text="Tự bịa", evidence_ids=[])]

    assert kinds(claims) == [(1, "number_unsupported"), (2, "no_evidence")]


@pytest.mark.parametrize("text,expected", [("1,5 triệu", {1_500_000}), ("600 triệu", {600_000_000}), ("50k", {50_000}), ("1.500.000 đồng", {1_500_000}), ("24 tháng", set()), ("năm 2026", set())])
def test_claim_amounts_reads_money_deterministically(text, expected):
    assert claim_amounts(text) == expected


def test_format_vnd_uses_vietnamese_grouping():
    assert format_vnd(1_500_000) == "1.500.000 đồng"
    assert format_vnd(-500_000) == "-500.000 đồng"
    assert format_vnd(0) == "0 đồng"


def test_month_and_year_are_not_mistaken_for_money():
    assert claim_amounts("Kế hoạch từ tháng 8/2026 đến tháng 10/2026") == set()
    ok = Claim(text="Từ tháng 8/2026, số dư 1,5 triệu", evidence_ids=["balance"])
    bad = Claim(text="Từ tháng 8/2026, số dư 1,6 triệu", evidence_ids=["balance"])

    assert kinds([ok]) == []
    assert kinds([bad]) == [(0, "number_unsupported")]


# Phase 84 (S5): câu cố định về độ phủ dữ liệu mà backend ghép vào cuối câu trả lời phải qua được bộ kiểm số thật.
# Câu mẫu do backend dựng (ForecastCoverage.Warning); con số duy nhất là phần trăm, bằng chứng data_coverage mang đúng câu đó.
def _coverage_sentence(percent: int) -> str:
    return f"Chỉ {percent}% số ngày gần đây FinMate đọc được thông báo ngân hàng, nên dự báo có thể thiếu giao dịch."


@pytest.mark.parametrize("percent", [0, 5, 30, 50, 59])
def test_the_fixed_data_coverage_sentence_passes_the_verifier(percent):
    sentence = _coverage_sentence(percent)
    coverage = Evidence(
        id="data_coverage",
        key="data_coverage",
        value_vnd=None,
        text=sentence,
        unit="percent",
        source="estimated",
        as_of=AS_OF,
        confidence=1.0,
    )

    assert claim_amounts(sentence) == set()
    assert kinds([Claim(text=sentence, evidence_ids=["data_coverage"])], [*EVIDENCE, coverage]) == []
