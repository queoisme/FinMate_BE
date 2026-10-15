"""Đo mức dùng Gemini/bộ nhớ/luật khi phân loại món: chỉ SỐ ĐẾM theo ngày, không tên món, cửa hàng hay người dùng."""

import sys
from pathlib import Path

import httpx
import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts"))

from app.api.v1.bill import get_ai_instruction, get_bill_knowledge, get_classify_stats, get_gemini_client  # noqa: E402
from app.bill.classify_stats import build_counts  # noqa: E402
from app.bill.gemini import MAX_NAMES  # noqa: E402
from app.core.config import settings  # noqa: E402
from app.main import app  # noqa: E402
from app.schemas.bill import ClassifyItemOut  # noqa: E402
from classify_stats import summarize  # noqa: E402
from tests.unit.test_item_classifier import FakeGemini, FakeKnowledge, items  # noqa: E402
from app.bill.item_classifier import classify_items  # noqa: E402


def out(source, slug="food"):
    return ClassifyItemOut(index=0, category_slug=None if source == "unknown" else slug, source=source)


def test_build_counts_splits_items_by_source_and_flags_the_request():
    counts = build_counts([out("cache"), out("cache"), out("rule"), out("ai"), out("merchant"), out("inherit"), out("unknown")],
                          allow_ai=True, ai_used=True, extra={"ai_candidates": 3, "ai_sent": 3})
    assert counts == {"requests": 1, "items": 7, "src_cache": 2, "src_rule": 1, "src_ai": 1, "src_merchant": 1, "src_inherit": 1, "src_unknown": 1,
                      "ai_requests": 1, "ai_candidates": 3, "ai_sent": 3}


def test_build_counts_marks_a_request_without_ai_consent_and_never_has_zero_noise():
    counts = build_counts([out("rule")], allow_ai=False, ai_used=False, extra={})
    assert counts == {"requests": 1, "items": 1, "src_rule": 1, "noconsent_requests": 1}  # chỉ ghi số khác 0


async def test_classify_items_reports_how_many_names_needed_ai_and_how_many_were_over_the_cap():
    stats: dict[str, int] = {}
    names = [f"Qwerty{i} Abc" for i in range(MAX_NAMES + 5)]
    await classify_items(items(*names), None, FakeKnowledge(), FakeGemini(lambda n: "shopping"), "LUAT", True, stats=stats)
    assert stats == {"ai_candidates": MAX_NAMES + 5, "ai_sent": MAX_NAMES}


async def test_classify_items_reports_ai_failure_and_health_bills():
    failed: dict[str, int] = {}
    await classify_items(items("Qwerty Abc"), None, FakeKnowledge(), FakeGemini(fail=True), "LUAT", True, stats=failed)
    assert failed == {"ai_candidates": 1, "ai_sent": 1, "ai_failed": 1}
    health: dict[str, int] = {}
    await classify_items(items("Paracetamol 500mg", "Qwerty Abc"), None, FakeKnowledge(), FakeGemini(), "LUAT", True, stats=health)
    assert health == {"health_bill": 1}  # hóa đơn thuốc không bao giờ gửi ra ngoài


async def test_the_route_records_counts_without_any_item_text():
    recorded: list[dict[str, int]] = []

    class Recorder:
        async def record(self, counts):
            recorded.append(counts)

    fake = FakeGemini(lambda n: "shopping")
    app.dependency_overrides[get_bill_knowledge] = lambda: FakeKnowledge()
    app.dependency_overrides[get_gemini_client] = lambda: fake
    app.dependency_overrides[get_ai_instruction] = lambda: "INSTR"
    app.dependency_overrides[get_classify_stats] = lambda: Recorder()
    try:
        transport = httpx.ASGITransport(app=app)
        async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
            res = await client.post("/api/v1/bill/classify-items", headers={"Authorization": f"Bearer {settings.internal_api_key}"},
                                    json={"user_id_hash": "a" * 64, "merchant_name": "Tiem Bi Mat", "allow_ai": True,
                                          "items": [{"name": "Qwerty Abc"}, {"name": "Gạo ST25"}]})
    finally:
        for dep in (get_bill_knowledge, get_gemini_client, get_ai_instruction, get_classify_stats):
            app.dependency_overrides.pop(dep, None)
    assert res.status_code == 200
    (counts,) = recorded
    # đã đồng ý AI: AI quyết trước, nên cả "Gạo ST25" (luật cũng biết) vẫn đi qua Gemini khi chưa có trong bộ nhớ
    assert counts["requests"] == 1 and counts["items"] == 2 and counts["src_ai"] == 2 and counts["ai_requests"] == 1
    assert all(isinstance(k, str) and isinstance(v, int) for k, v in counts.items())
    assert "Qwerty" not in str(counts) and "Bi Mat" not in str(counts)


async def test_a_failing_recorder_never_breaks_classification():
    class Broken:
        async def record(self, counts):
            raise RuntimeError("db down")

    app.dependency_overrides[get_bill_knowledge] = lambda: FakeKnowledge()
    app.dependency_overrides[get_gemini_client] = lambda: FakeGemini(lambda n: "shopping")
    app.dependency_overrides[get_ai_instruction] = lambda: "INSTR"
    app.dependency_overrides[get_classify_stats] = lambda: Broken()
    try:
        transport = httpx.ASGITransport(app=app)
        async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
            res = await client.post("/api/v1/bill/classify-items", headers={"Authorization": f"Bearer {settings.internal_api_key}"},
                                    json={"user_id_hash": "a" * 64, "allow_ai": True, "items": [{"name": "Qwerty Abc"}]})
    finally:
        for dep in (get_bill_knowledge, get_gemini_client, get_ai_instruction, get_classify_stats):
            app.dependency_overrides.pop(dep, None)
    assert res.status_code == 200 and res.json()["items"][0]["source"] == "ai"


def test_summarize_turns_daily_counters_into_rates():
    rows = {"2026-10-15": {"requests": 10, "items": 100, "src_cache": 40, "src_rule": 20, "src_ai": 30, "src_merchant": 5, "src_unknown": 5,
                           "ai_requests": 6, "noconsent_requests": 2, "ai_candidates": 50, "ai_sent": 30, "ai_failed": 1, "health_bill": 1}}
    day = summarize(rows)[0]
    assert day["date"] == "2026-10-15"
    assert day["items_per_bill"] == 10
    assert day["cache_rate"] == 0.4 and day["rule_rate"] == 0.2 and day["ai_rate"] == 0.3 and day["unknown_rate"] == 0.05
    assert day["ai_calls_per_bill"] == 0.6  # số lần gọi Gemini trên mỗi bill
    assert day["overflow_rate"] == 0.4  # 20/50 món cần AI nhưng vượt trần 20 món mỗi lần
    assert day["noconsent_rate"] == 0.2
