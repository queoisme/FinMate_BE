"""Bước AI phân loại phần nội dung đã tách của thông báo: chỉ gửi cụm đã che số, không bao giờ gửi cả thông báo; lỗi/quá hạn/unknown giữ kết quả luật."""

import asyncio

import pytest

from app.bill.gemini import GeminiFailed
from app.core.enums import ExtractionMethod, TransactionType
from app.pipeline.stages.ai_categorizer import AiDeps, categorize_with_ai
from app.pipeline.stages.categorizer import CategorizationOutcome
from app.pipeline.stages.extractor import ExtractionOutcome
from tests.unit.test_item_classifier import FakeGemini, FakeKnowledge

RULE_OTHER = CategorizationOutcome("other", 0.3, ExtractionMethod.RULE)


def extraction(description, narrative=True, tx=TransactionType.DEBIT):
    return ExtractionOutcome(
        amount_cents=5_000_000, transaction_type=tx, merchant_name=None, description=description, transacted_at=None,
        balance_after_cents=None, confidence=0.9, method=ExtractionMethod.RULE, matched_pattern_name=None, description_is_narrative=narrative,
    )


async def run(description, gemini, outcome=RULE_OTHER, allow_ai=True, deps=True, timeout=1.0, **kw):
    d = AiDeps(gemini, FakeKnowledge(), "LUAT") if deps else None
    return await categorize_with_ai(outcome, extraction(description, **kw), allow_ai, d, timeout=timeout)


async def test_ai_categorizes_the_narrative_phrase_only():
    gemini = FakeGemini(lambda n: "shopping")
    got = await run("mua quan ao", gemini)
    assert (got.category_slug, got.confidence, got.method) == ("shopping", 0.70, ExtractionMethod.RULE)
    assert gemini.calls == [["mua quan ao"]]


async def test_long_digit_runs_are_masked_before_sending():
    gemini = FakeGemini(lambda n: "shopping")
    await run("ck 0123456789012 mua quan ao", gemini)
    assert len(gemini.calls) == 1 and "0123456789012" not in gemini.calls[0][0] and "mua quan ao" in gemini.calls[0][0]


@pytest.mark.parametrize("kwargs", [
    {"allow_ai": False},
    {"deps": False},
    {"outcome": CategorizationOutcome("food", 0.9, ExtractionMethod.RULE)},
    {"outcome": CategorizationOutcome("other", 0.5, ExtractionMethod.MODEL, "v1")},
    {"tx": TransactionType.CREDIT},
    {"narrative": False},
])
async def test_ai_is_not_called_when_a_condition_fails(kwargs):
    gemini = FakeGemini(lambda n: "shopping")
    outcome = kwargs.get("outcome", RULE_OTHER)
    got = await run("mua quan ao", gemini, **kwargs)
    assert got == outcome and gemini.calls == []


@pytest.mark.parametrize("description", ["mua vi thuoc ha sot", "50,000", "", "   "])
async def test_health_and_empty_phrases_are_never_sent(description):
    gemini = FakeGemini(lambda n: "shopping")
    assert await run(description, gemini) == RULE_OTHER and gemini.calls == []


@pytest.mark.parametrize("answer", ["unknown", "income", "khong-co-slug"])
async def test_unusable_answers_keep_the_rule_result(answer):
    assert await run("mua quan ao", FakeGemini(lambda n: answer)) == RULE_OTHER


async def test_gemini_errors_keep_the_rule_result():
    assert await run("mua quan ao", FakeGemini(fail=True)) == RULE_OTHER

    class Boom(FakeGemini):
        async def classify(self, names, instruction):
            raise RuntimeError("x")

    assert await run("mua quan ao", Boom()) == RULE_OTHER


async def test_slow_gemini_past_the_timeout_keeps_the_rule_result():
    class Slow(FakeGemini):
        async def classify(self, names, instruction):
            await asyncio.sleep(1)
            return ["shopping"]

    assert await run("mua quan ao", Slow(), timeout=0.01) == RULE_OTHER
