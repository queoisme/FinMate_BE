"""Phân loại từng món: thừa kế → luật → nhóm mặc định cửa hàng → Gemini (chỉ khi được phép) → unknown."""

import asyncio

import httpx

from app.bill.gemini import GeminiClient, GeminiFailed
from app.bill.item_classifier import classify_items
from app.schemas.bill import ClassifyItemIn


class FakeKnowledge:
    def __init__(self, cache=None):
        self.refreshes = 0
        self.cache = dict(cache or {})  # item_key -> slug, đã có sẵn trong bảng nhớ
        self.remembered_items: dict[str, str] = {}  # những gì classify_items đã ghi thêm

    async def refresh_if_stale(self):
        self.refreshes += 1

    async def cached_items(self, keys):
        return {k: self.cache[k] for k in keys if k in self.cache}

    async def remember_items(self, answers):
        self.remembered_items.update(answers)


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


async def run(its, merchant=None, gemini=None, instruction="LUAT", allow_ai=True, knowledge=None):
    out, used = await classify_items(its, merchant, knowledge or FakeKnowledge(), gemini or FakeGemini(), instruction, allow_ai)
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


async def test_whitening_shower_gel_is_shopping_not_spa():
    out, _ = await run(items("Sữa tắm trắng da 900g"), allow_ai=False)
    assert out == [(0, "shopping", "rule")]


async def test_gemini_beats_store_default_for_unmatched_items():
    gemini = FakeGemini(answer=lambda name: "shopping")
    out, used = await run(items("Qwerty abc"), merchant="Ministop", gemini=gemini)
    assert out == [(0, "shopping", "ai")] and used


async def test_store_default_used_when_ai_not_allowed():
    out, _ = await run(items("Qwerty abc"), merchant="Ministop", allow_ai=False)
    assert out == [(0, "food", "merchant")]


async def test_store_default_used_when_gemini_says_unknown():
    out, used = await run(items("Qwerty abc"), merchant="Ministop", gemini=FakeGemini(answer=lambda name: "unknown"))
    assert out == [(0, "food", "merchant")] and used


async def test_store_default_used_when_gemini_fails():
    out, used = await run(items("Qwerty abc"), merchant="Ministop", gemini=FakeGemini(fail=True))
    assert out == [(0, "food", "merchant")] and not used


async def test_rule_hit_never_reaches_gemini_even_with_a_store_default():
    gemini = FakeGemini(answer=lambda name: "shopping")
    out, _ = await run(items("Gạo ST25"), merchant="Ministop", gemini=gemini)
    assert out == [(0, "food", "rule")] and gemini.calls == []


async def test_topping_of_a_gemini_decided_item_inherits_the_ai_result():
    gemini = FakeGemini(answer=lambda name: "shopping")
    out, _ = await run(items("Qwerty abc", ("+ Zxcv", 0)), merchant="Ministop", gemini=gemini)
    assert out == [(0, "shopping", "ai"), (1, "shopping", "inherit")]


async def test_health_store_default_still_blocks_gemini():
    gemini = FakeGemini(answer=lambda name: "shopping")
    _out, used = await run(items("Qwerty abc"), merchant="Nhà thuốc Long Châu", gemini=gemini)
    assert gemini.calls == [] and not used


async def test_cached_answer_is_used_without_calling_gemini():
    gemini = FakeGemini(answer=lambda name: "food")
    out, used = await run(items("Qwerty Abc"), gemini=gemini, knowledge=FakeKnowledge({"qwerty abc": "shopping"}))
    assert out == [(0, "shopping", "cache")] and gemini.calls == [] and not used


async def test_cache_read_works_when_ai_is_not_allowed():
    out, _ = await run(items("Qwerty Abc"), allow_ai=False, knowledge=FakeKnowledge({"qwerty abc": "family"}))
    assert out == [(0, "family", "cache")]


async def test_cache_key_ignores_case_accents_money_and_spacing():
    knowledge = FakeKnowledge({"qwerty abc": "shopping"})
    out, _ = await run(items("  QWERTY   ábc 45,000đ "), allow_ai=False, knowledge=knowledge)
    assert out == [(0, "shopping", "cache")]


async def test_rule_beats_cache():
    out, _ = await run(items("Gạo ST25"), allow_ai=False, knowledge=FakeKnowledge({"gao st25": "shopping"}))
    assert out == [(0, "food", "rule")]


async def test_gemini_answer_is_remembered():
    knowledge = FakeKnowledge()
    await run(items("Qwerty Abc"), gemini=FakeGemini(answer=lambda name: "shopping"), knowledge=knowledge)
    assert knowledge.remembered_items == {"qwerty abc": "shopping"}


async def test_unknown_answer_is_never_remembered():
    knowledge = FakeKnowledge()
    await run(items("Qwerty Abc"), gemini=FakeGemini(answer=lambda name: "unknown"), knowledge=knowledge)
    assert knowledge.remembered_items == {}


async def test_invalid_slug_from_the_model_is_never_remembered():
    knowledge = FakeKnowledge()
    await run(items("Qwerty Abc"), gemini=FakeGemini(answer=lambda name: "income"), knowledge=knowledge)
    assert knowledge.remembered_items == {}


async def test_junk_names_are_never_remembered():
    knowledge = FakeKnowledge()
    junk = ["SP002993", "A12", "ab", "x" * 61, "Qwe\nrty abc"]
    await run(items(*junk), gemini=FakeGemini(answer=lambda name: "shopping"), knowledge=knowledge)
    assert knowledge.remembered_items == {}


async def test_ai_not_allowed_never_writes_the_cache():
    knowledge = FakeKnowledge()
    await run(items("Qwerty Abc"), allow_ai=False, knowledge=knowledge)
    assert knowledge.remembered_items == {}


async def test_one_letter_ocr_typo_still_matches_a_product_keyword():
    out, _ = await run(items("KHAM UOT Bobby"), allow_ai=False)
    assert out == [(0, "shopping", "rule")]


async def test_fuzzy_match_never_reaches_gemini():
    gemini = FakeGemini(answer=lambda name: "food")
    out, _ = await run(items("KHAM UOT Bobby"), gemini=gemini)
    assert out == [(0, "shopping", "rule")] and gemini.calls == []


async def test_codes_and_short_words_get_no_fuzzy_hit():
    out, _ = await run(items("SP002993", "nuoc", "xyz"), allow_ai=False)
    assert out == [(0, None, "unknown"), (1, None, "unknown"), (2, None, "unknown")]


async def test_a_different_real_word_is_not_pulled_into_a_keyword():
    out, _ = await run(items("Khan tam bong", "Banh quy bo"), allow_ai=False)  # khăn tắm không phải khăn ướt; bánh quy là đồ ăn nhờ từ "banh"
    assert out == [(0, None, "unknown"), (1, "food", "rule")]


async def test_a_one_letter_difference_between_two_real_products_is_not_a_typo():
    out, _ = await run(items("Nuoc muoi sinh ly"), allow_ai=False)  # nước muối ≠ nước suối
    assert out == [(0, None, "unknown")]


async def test_missing_letter_and_confusable_letter_typos_match():
    out, _ = await run(items("KHAN UOTT", "KHA UOT", "KHAM UOT"), allow_ai=False)
    assert [o[1] for o in out] == ["shopping"] * 3


async def test_item_rule_step_matches_the_resolver():
    from app.core.category_resolver import resolve

    phrases = ["ta quan Bobby", "sua bot Enfa", "hoc phi", "kham benh", "bot giat Omo", "vitamin C", "nuoc ngot Pepsi", "khan uot", "khan uott", "SP002993"]
    for phrase in phrases:
        out, _ = await run(items(phrase), allow_ai=False)
        expected = resolve(phrase).slug
        assert out == [(0, expected, "rule" if expected else "unknown")], phrase


async def test_sua_bot_is_family_not_food_on_a_receipt():
    out, _ = await run(items("Sua bot Enfa A+ 800g"), allow_ai=False)
    assert out == [(0, "family", "rule")]
