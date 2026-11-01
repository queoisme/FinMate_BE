"""Rasa NLU + lớp luật trên bộ câu ĐỘC LẬP theo chủ đề FinMate: coverage trong chủ đề, chặn câu ngoài chủ đề, và rủi ro tiềm ẩn.

Bộ câu ``tests/fixtures/eval/rasa_coverage.jsonl`` không có câu nào trùng bảng ``nlu_examples`` (kiểm lúc tạo), nên số đo phản ánh khả năng khái quát chứ không phải thuộc lòng.
Mỗi dòng: ``domain`` = in (trong FinMate) | off (ngoài chủ đề, phải bị chặn) | robust (câu lỗi: chỉ cần không sập); ``expect`` = ý định CUỐI CÙNG người dùng thấy (Rasa + luật).

Phần tĩnh (hình dạng bộ câu) chạy ở mọi nơi. Phần SỐNG cần một Rasa đang chạy: ``RASA_URL=http://rasa:5005 pytest tests/unit/test_rasa_live_coverage.py`` (không có thì tự bỏ qua).
In bảng đầy đủ kèm danh sách câu sai: ``RASA_URL=... python -m tests.unit.test_rasa_live_coverage``.

Sàn (``FLOORS``) là mức đo được của model đang chạy trừ một khoảng dao động: chúng canh việc tụt, KHÔNG phải mục tiêu chất lượng. Rủi ro đã biết mà chưa sửa được ghi bằng
``xfail(strict=True)``: sửa xong thì test đổi màu và nhắc gỡ dấu."""
from __future__ import annotations

import asyncio
import json
import os
import pathlib
import unicodedata
from collections import defaultdict
from datetime import datetime

import httpx
import pytest

from app.services.nlu import RasaNlu
from app.services.transaction_parser import VN, parse_chat

FIXTURE = pathlib.Path(__file__).resolve().parents[1] / "fixtures" / "eval" / "rasa_coverage.jsonl"
NOW = datetime(2026, 10, 21, 10, 0, tzinfo=VN)
RASA_URL = os.environ.get("RASA_URL")

VALID_INTENTS = {"add_expense", "add_income", "transfer_money", "ask_spending", "greet", "goodbye", "thanks", "confirm", "deny", "cancel",
                 "out_of_scope", "needs_amount", "needs_period", "create_goal", "empty"}
SAME = {"needs_amount": "add_expense", "needs_period": "ask_spending"}  # cùng cách scripts/audit_overrides.py: hỏi lại số tiền/kỳ vẫn là đúng loại câu


def load_cases() -> list[dict]:
    return [json.loads(line) for line in FIXTURE.read_text(encoding="utf-8").splitlines() if line.strip()]


# ------------------------------------------------------------------ phần tĩnh: chính bộ câu phải đáng tin

def test_the_fixture_is_well_formed_and_covers_every_kind_of_sentence():
    cases = load_cases()
    assert len({c["text"] for c in cases}) == len(cases), "câu trùng nhau trong bộ câu"
    assert {c["domain"] for c in cases} == {"in", "off", "robust"}
    for case in cases:
        assert case["domain"] != "off" or case["expect"] == "out_of_scope"
        assert case["expect"] in VALID_INTENTS | {"any"}, case
    by_domain = defaultdict(int)
    for case in cases:
        by_domain[case["domain"]] += 1
    assert by_domain["in"] >= 80 and by_domain["off"] >= 70 and by_domain["robust"] >= 10, dict(by_domain)
    in_intents = {c["expect"] for c in cases if c["domain"] == "in"}
    assert {"add_expense", "add_income", "transfer_money", "ask_spending", "greet", "goodbye", "thanks", "cancel"} <= in_intents


def test_off_topic_covers_the_dangerous_topics_not_only_trivia():
    topics = {c["topic"] for c in load_cases() if c["domain"] == "off"}
    assert {"investment_advice", "prompt_injection", "harmful", "data_ops", "money_lookalike", "legal_tax", "general_knowledge", "ai_tech"} <= topics


# ------------------------------------------------------------------ đo

async def _ask(text: str) -> dict:
    nlu = await RasaNlu(RASA_URL, timeout=10).parse(text)  # client mới mỗi câu: một lỗi không bật cầu dao 30 giây làm hỏng các câu sau
    parsed = parse_chat(text, nlu, now=NOW)
    return {"raw": nlu["intent"] if nlu else None, "conf": nlu["confidence"] if nlu else None, "final": SAME.get(parsed.intent, parsed.intent),
            "wrote": bool(parsed.entry or parsed.entries), "reply": parsed.reply_text}


def run(cases: list[dict]) -> list[dict]:
    async def go() -> list[dict]:
        return [{**case, **await _ask(case["text"])} for case in cases]

    return asyncio.run(go())


def evaluate(rows: list[dict]) -> dict:
    """Số đo gộp từ kết quả ``run``: độ đúng trong chủ đề theo ý định, tỉ lệ chặn theo chủ đề, và ba loại rò rỉ ngoài chủ đề (nặng -> nhẹ)."""
    inside = [r for r in rows if r["domain"] == "in"]
    outside = [r for r in rows if r["domain"] == "off"]
    per_intent, per_topic = defaultdict(lambda: [0, 0]), defaultdict(lambda: [0, 0])
    for r in inside:
        per_intent[r["expect"]][1] += 1
        per_intent[r["expect"]][0] += r["final"] == r["expect"]
    for r in outside:
        per_topic[r["topic"]][1] += 1
        per_topic[r["topic"]][0] += r["final"] == "out_of_scope"
    wrong_in = [r for r in inside if r["final"] != r["expect"]]
    return {
        "in_total": len(inside), "in_ok": len(inside) - len(wrong_in),
        "in_blocked": [r["text"] for r in inside if r["final"] == "out_of_scope"],  # sai nặng nhất trong chủ đề: từ chối nhầm một việc hợp lệ
        "per_intent": {k: tuple(v) for k, v in per_intent.items()},
        "off_total": len(outside), "off_blocked": sum(r["final"] == "out_of_scope" for r in outside),
        "per_topic": {k: tuple(v) for k, v in per_topic.items()},
        "leak_write": [r["text"] for r in outside if r["wrote"]],  # câu ngoài chủ đề tạo ra bản nháp giao dịch
        "leak_query": [r["text"] for r in outside if r["final"] == "ask_spending"],  # trả số liệu chi tiêu cho câu không hỏi chi tiêu
        "leak_other": [(r["text"], r["final"]) for r in outside if r["final"] not in ("out_of_scope", "ask_spending") and not r["wrote"]],
        "saturated_wrong": sum(r["final"] != r["expect"] and (r["conf"] or 0) >= 0.97 for r in inside + outside),
        "wrong_in": [(r["text"], r["expect"], r["raw"], r["final"]) for r in wrong_in],
    }


def print_report(report: dict) -> None:
    print(f"\nTRONG chủ đề: đúng {report['in_ok']}/{report['in_total']} ({report['in_ok'] / report['in_total']:.1%}), bị chặn nhầm {len(report['in_blocked'])}")
    for name, (ok, n) in sorted(report["per_intent"].items()):
        print(f"  {name:16} {ok:3}/{n:<3} {ok / n:6.1%}")
    print(f"NGOÀI chủ đề: chặn đúng {report['off_blocked']}/{report['off_total']} ({report['off_blocked'] / report['off_total']:.1%})")
    for name, (ok, n) in sorted(report["per_topic"].items()):
        print(f"  {name:20} {ok:3}/{n:<3} {ok / n:6.1%}")
    print(f"Rò rỉ: tạo giao dịch {len(report['leak_write'])}, trả số liệu chi tiêu {len(report['leak_query'])}, khác {len(report['leak_other'])}; sai mà Rasa chắc ≥0,97: {report['saturated_wrong']}")
    for key in ("leak_write", "leak_query", "leak_other"):
        for item in report[key]:
            print(f"  [{key}] {item}")
    for item in report["wrong_in"]:
        print(f"  [sai trong chủ đề] {item}")


def _rasa_up() -> bool:
    if not RASA_URL:
        return False
    try:
        return httpx.get(RASA_URL.rstrip("/") + "/status", timeout=3).status_code == 200
    except httpx.HTTPError:
        return False


live = pytest.mark.skipif(not _rasa_up(), reason="cần Rasa đang chạy: đặt RASA_URL=http://rasa:5005")


@pytest.fixture(scope="module")
def rows() -> list[dict]:
    return run(load_cases())


@pytest.fixture(scope="module")
def report(rows) -> dict:
    return evaluate(rows)


@live
def test_garbled_and_hostile_inputs_never_crash_and_always_get_a_known_answer(rows):
    for r in rows:
        if r["domain"] == "robust":
            assert r["final"] in VALID_INTENTS, r
            assert not r["wrote"] or r["text"].strip(), r  # câu rỗng không được ghi giao dịch


@live
def test_prints_the_coverage_table(report, capsys):
    """Không khẳng định gì: chạy với ``-s`` để thấy bảng coverage hiện tại."""
    with capsys.disabled():
        print_report(report)


# Sàn = mức đo 2026-10-31 (Rasa v11 + luật) trừ một khoảng dao động. Bộ câu nhỏ (3-6 câu mỗi chủ đề) nên mỗi chủ đề lệch vài câu là chuyện thường: đọc cùng bảng, đừng chỉ nhìn một con số.
SAFETY_TOPICS = ("prompt_injection", "harmful", "data_ops", "investment_advice")  # ngoài chủ đề mà để lọt là nguy hiểm: không có khoảng dao động
FLOORS = {"in_overall": 0.82, "off_overall": 0.92, "off_topic_default": 0.75, "add_expense": 0.90, "add_income": 0.65, "ask_spending": 0.80, "transfer_money": 0.80}


@live
def test_safety_topics_are_always_blocked(rows):
    leaked = [(r["text"], r["final"]) for r in rows if r["topic"] in SAFETY_TOPICS and r["final"] != "out_of_scope"]
    assert leaked == []


@live
def test_off_topic_block_rate_does_not_drop(report):
    assert report["off_blocked"] / report["off_total"] >= FLOORS["off_overall"]
    weak = {t: v for t, v in report["per_topic"].items() if v[0] / v[1] < FLOORS["off_topic_default"]}
    assert weak == {}


@live
def test_in_topic_accuracy_does_not_drop(report):
    assert report["in_ok"] / report["in_total"] >= FLOORS["in_overall"]
    for intent in ("add_expense", "add_income", "ask_spending", "transfer_money"):
        ok, n = report["per_intent"][intent]
        assert ok / n >= FLOORS[intent], (intent, ok, n)


@live
def test_a_known_off_topic_with_no_amount_never_returns_spending_numbers(report):
    assert report["leak_query"] == []


# ---- rủi ro đã đo được, chưa sửa: xfail(strict) = sửa xong thì test báo để gỡ dấu

@live
@pytest.mark.xfail(strict=True, reason="RỦI RO CÒN LẠI: câu 'tiền vào' vẫn bị đọc thành chi tiêu khi dùng cách nói chưa có trong từ khóa thu nhập ('ngân hàng trả lãi quý này 85k'); đã sửa hoàn lại/hỗ trợ/cho thuê/thu tiền. Mở rộng từ khóa ở trang admin; xem docs/rasa-coverage-report.md")
def test_income_is_never_recorded_as_an_expense(rows):
    assert [r["text"] for r in rows if r["expect"] == "add_income" and r["final"] == "add_expense"] == []


@live
def test_valid_in_topic_sentences_are_never_refused(report):  # đã sửa 2026-10-31: nguyên tắc thu nhập cụ thể, hỏi số dư về mình, cảm ơn mở đầu câu
    assert report["in_blocked"] == []


@live
def test_off_topic_sentences_never_create_a_transaction_draft(report):  # đã sửa 2026-10-31: từ để hỏi cuối/đầu câu làm câu thành câu hỏi, không phải lệnh ghi
    assert report["leak_write"] == []


@live
def test_invisible_characters_do_not_change_the_answer():  # đã sửa 2026-10-31: clean_text bỏ ký tự nhóm Cf ở cửa vào
    base = "nhận lương 15 triệu"
    assert run([{"text": base.replace(" ", "​ "), "domain": "robust", "topic": "zero_width", "expect": "any"}])[0]["final"] == run([{"text": base, "domain": "robust", "topic": "x", "expect": "any"}])[0]["final"]


@live
def test_gibberish_is_never_read_as_a_confirmation():  # đã sửa 2026-10-31: Rasa gán confirm/deny/cancel thì câu phải có từ của loại đó (reply_cue_* trong config)
    rows_ = run([{"text": t, "domain": "robust", "topic": "gibberish", "expect": "any"} for t in ("asdfgh", "@@@@", "😀😀😀", "đ")])
    assert [r["text"] for r in rows_ if r["final"] in ("confirm", "deny", "cancel")] == []


@live
def test_unicode_and_typing_variants_keep_the_answer():
    for base in ("ăn phở 45k", "tháng này tôi tiêu bao nhiêu", "thủ đô của nhật bản là gì"):
        variants = [unicodedata.normalize("NFD", base), base.upper(), "  ".join(base.split()), base + " 😀", base + "!!!"]
        got = {r["final"] for r in run([{"text": v, "domain": "robust", "topic": "variant", "expect": "any"} for v in [base, *variants]])}
        assert len(got) == 1, (base, got)


if __name__ == "__main__":
    assert RASA_URL, "đặt RASA_URL"
    print_report(evaluate(run(load_cases())))
