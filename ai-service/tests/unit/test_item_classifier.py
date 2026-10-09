"""Phân loại từng món: thừa kế → luật → nhóm mặc định cửa hàng → Gemini (chỉ khi được phép) → unknown."""

import asyncio

from app.bill import item_classifier
from app.bill.gemini import GeminiFailed
from app.bill.item_classifier import classify_items
from app.schemas.bill import ClassifyItemIn


class FakeKnowledge:
    def __init__(self):
        self.refreshes = 0

    async def refresh_if_stale(self):
        self.refreshes += 1


class FakeGemini:
    """``answer``: hàm tên -> nhóm; ``fail``: ném GeminiFailed; ``sleep``: treo quá hạn."""

    def __init__(self, answer=lambda name: "unknown", available=True, fail=False, sleep=0.0):
        self.answer, self.available, self.fail, self.sleep = answer, available, fail, sleep
        self.calls: list[list[str]] = []

    async def classify(self, names, instruction):
        self.calls.append(list(names))
        if self.sleep:
            await asyncio.sleep(self.sleep)
        if self.fail:
            raise GeminiFailed("boom")
        return [self.answer(n) for n in names]


def items(*specs):
    return [ClassifyItemIn(name=s, parent=None) if isinstance(s, str) else ClassifyItemIn(name=s[0], parent=s[1]) for s in specs]


async def run(its, merchant=None, gemini=None, instruction="LUAT", allow_ai=True):
    out, used = await classify_items(its, merchant, FakeKnowledge(), gemini or FakeGemini(), instruction, allow_ai)
    return [(o.index, o.category_slug, o.source) for o in out], used


async def test_rule_beats_merchant_default():
    out, _ = await run(items("Paracetamol 500mg", "Abc xyz"), merchant="Bách Hóa Xanh", allow_ai=False)
    assert out == [(0, "health", "rule"), (1, "food", "merchant")]


async def test_topping_inherits_parent_category():
    out, _ = await run(items("Trà sữa trân châu", ("+ Thêm topping", 0)), allow_ai=False)
    assert out == [(0, "food", "rule"), (1, "food", "inherit")]


async def test_inherit_of_unknown_stays_unknown():
    out, _ = await run(items("Qwerty", ("+ Zzz", 0)), allow_ai=False)
    assert out == [(0, None, "unknown"), (1, None, "unknown")]


async def test_inherit_takes_parent_ai_result():
    out, used = await run(items("Qwerty", ("+ Zzz", 0)), gemini=FakeGemini(lambda n: "shopping"))
    assert out == [(0, "shopping", "ai"), (1, "shopping", "inherit")] and used


async def test_merchant_default_only_for_unmatched_items():
    out, _ = await run(items("Bột giặt Omo", "Gạo ST25", "Không rõ"), merchant="Highlands Coffee", allow_ai=False)
    assert out == [(0, "shopping", "rule"), (1, "food", "rule"), (2, "food", "merchant")]


async def test_unknown_when_nothing_matches_and_ai_not_allowed():
    out, used = await run(items("Qwerty", "Asdf"), allow_ai=False)
    assert out == [(0, None, "unknown"), (1, None, "unknown")] and used is False


async def test_allow_ai_false_never_calls_gemini():
    gemini = FakeGemini(lambda n: "food")
    await run(items("Qwerty", "Asdf"), gemini=gemini, allow_ai=False)
    assert gemini.calls == []


async def test_gemini_skipped_when_unavailable_or_no_instruction():
    for gemini, instruction in ((FakeGemini(available=False), "LUAT"), (FakeGemini(), ""), (FakeGemini(), "   ")):
        out, used = await run(items("Qwerty"), gemini=gemini, instruction=instruction)
        assert gemini.calls == [] and used is False and out == [(0, None, "unknown")]


async def test_health_items_matched_by_rules_are_never_sent_to_gemini():
    gemini = FakeGemini(lambda n: "other")
    out, _ = await run(items("Paracetamol 500mg", "Thuốc ho Bảo Thanh", "Qwerty"), gemini=gemini)
    assert gemini.calls == [["Qwerty"]]
    assert out[:2] == [(0, "health", "rule"), (1, "health", "rule")]


async def test_only_unmatched_names_sent_to_gemini_max_20():
    names = ["Gạo ST25"] + [f"Mon la {i}" for i in range(25)] + [("+ phu", 3)]
    gemini = FakeGemini(lambda n: "shopping")
    out, used = await run(items(*names), gemini=gemini)
    assert len(gemini.calls) == 1 and gemini.calls[0] == [f"Mon la {i}" for i in range(20)]
    assert used is True
    assert out[0] == (0, "food", "rule")
    assert all(o == (i, "shopping", "ai") for i, o in enumerate(out[1:21], start=1))
    assert all(o == (i, None, "unknown") for i, o in enumerate(out[21:26], start=21))
    assert out[26] == (26, "shopping", "inherit")


async def test_ai_unknown_answer_stays_unknown():
    out, used = await run(items("Qwerty", "Asdf"), gemini=FakeGemini(lambda n: "unknown" if n == "Qwerty" else "food"))
    assert out == [(0, None, "unknown"), (1, "food", "ai")] and used is True


async def test_gemini_failure_leaves_unmatched_as_unknown():
    out, used = await run(items("Gạo ST25", "Qwerty"), gemini=FakeGemini(fail=True))
    assert out == [(0, "food", "rule"), (1, None, "unknown")] and used is False


async def test_gemini_hanging_past_hard_limit_leaves_unknown(monkeypatch):
    monkeypatch.setattr(item_classifier, "GEMINI_HARD_LIMIT_SECONDS", 0.05)
    out, used = await run(items("Qwerty"), gemini=FakeGemini(lambda n: "food", sleep=5))
    assert out == [(0, None, "unknown")] and used is False


async def test_no_gemini_call_when_everything_matched():
    gemini = FakeGemini()
    _, used = await run(items("Gạo ST25", "Bột giặt"), gemini=gemini)
    assert gemini.calls == [] and used is False
