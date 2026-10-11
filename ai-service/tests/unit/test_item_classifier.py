"""Phân loại từng món: thừa kế → luật → nhóm mặc định cửa hàng → Gemini (chỉ khi được phép) → unknown."""

import asyncio

import httpx

from app.bill.gemini import GeminiClient, GeminiFailed
from app.bill.item_classifier import classify_items
from app.schemas.bill import ClassifyItemIn


class FakeKnowledge:
    def __init__(self):
        self.refreshes = 0

    async def refresh_if_stale(self):
        self.refreshes += 1


class FakeGemini:
    """``answer``: hàm tên -> nhóm; ``fail``: ném GeminiFailed."""

    def __init__(self, answer=lambda name: "unknown", available=True, fail=False):
        self.answer, self.available, self.fail = answer, available, fail
        self.calls: list[list[str]] = []

    async def classify(self, names, instruction):
        self.calls.append(list(names))
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
    # Một món thuốc khớp luật => cả hóa đơn không gửi Gemini (món lạ còn lại có thể cũng là thuốc).
    gemini = FakeGemini(lambda n: "other")
    out, _ = await run(items("Paracetamol 500mg", "Thuốc ho Bảo Thanh", "Qwerty"), gemini=gemini)
    assert gemini.calls == []
    assert out == [(0, "health", "rule"), (1, "health", "rule"), (2, None, "unknown")]


async def test_pharmacy_not_in_brand_table_never_reaches_gemini():
    gemini = FakeGemini(lambda n: "shopping")
    out, used = await run(items("Panadol Extra", "Efferalgan 500mg", "V.Rohto"), merchant="Nhà thuốc Minh Châu", gemini=gemini)
    assert gemini.calls == [] and used is False
    assert all(o[2] != "ai" for o in out)


async def test_grocery_bill_unknown_item_still_reaches_gemini():
    gemini = FakeGemini(lambda n: "shopping")
    out, used = await run(items("Gạo ST25", "Qwerty"), merchant="Tạp hóa Cô Ba", gemini=gemini)
    assert gemini.calls == [["Qwerty"]] and used is True
    assert out[1] == (1, "shopping", "ai")


async def test_money_tokens_are_stripped_before_gemini():
    gemini = FakeGemini(lambda n: "food")
    out, _ = await run(items("Qwerty 2 45,000", "Asdf 12.000đ", "Zxcv vnd 1.250.000", "Nuoc 1,500ml"), gemini=gemini)
    assert gemini.calls == [["Qwerty 2", "Asdf", "Zxcv", "Nuoc 1,500ml"]]
    assert [o[1] for o in out] == ["food"] * 4


async def test_name_empty_after_money_strip_is_not_sent():
    gemini = FakeGemini(lambda n: "food")
    out, _ = await run(items("45.000đ", "Qwerty"), gemini=gemini)
    assert gemini.calls == [["Qwerty"]]
    assert out == [(0, None, "unknown"), (1, "food", "ai")]


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


async def test_gemini_hanging_past_hard_limit_leaves_unknown():
    async def hang(req):
        await asyncio.Event().wait()

    gemini = GeminiClient("key", "m", 5.0, transport=httpx.MockTransport(hang), overall_timeout=0.05)
    out, used = await run(items("Qwerty"), gemini=gemini)
    assert out == [(0, None, "unknown")] and used is False and not gemini.available


async def test_merchant_name_keywords_are_not_a_default_only_brands_are():
    for merchant in ("Nhà thuốc Minh Tâm", "Smartphone Center", "Tạp hóa Cô Ba"):
        out, _ = await run(items("Qwerty"), merchant=merchant, allow_ai=False)
        assert out == [(0, None, "unknown")], merchant


async def test_no_gemini_call_when_everything_matched():
    gemini = FakeGemini()
    _, used = await run(items("Gạo ST25", "Bột giặt"), gemini=gemini)
    assert gemini.calls == [] and used is False


async def test_wet_wipes_are_shopping_and_drinks_take_store_default_without_ai():
    # bill cửa hàng tiện lợi: khăn ướt theo luật là shopping, nước uống tên lạ nhận nhóm mặc định của thương hiệu → 2 nhóm
    its = items("Khan uot ALLCARE 20", "BonchaTacMatOng450", "Boncha Viet Quat 45", "TraYuzumatongboncha")
    out, used = await run(its, merchant="Ministop", allow_ai=False)
    assert out == [(0, "shopping", "rule"), (1, "food", "merchant"), (2, "food", "merchant"), (3, "food", "merchant")]
    assert used is False


async def test_personal_care_disposables_are_shopping_by_rule():
    out, _ = await run(items("Khăn giấy rút 100 tờ", "Kem đánh răng 180g", "Dầu gội 650ml", "Sữa tắm 900g"), allow_ai=False)
    assert [o[1:] for o in out] == [("shopping", "rule")] * 4
