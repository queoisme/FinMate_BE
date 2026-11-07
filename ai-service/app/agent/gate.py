"""Cổng phạm vi của AI Financial Agent: câu hỏi này có thuộc phạm vi tài chính cá nhân không?

Quyết định bằng LUẬT + Rasa, KHÔNG gọi LLM: rẻ, nhanh, chạy được kể cả khi LLM hỏng (NFR-REL-01), và là lớp chặn đầu tiên chống câu hỏi lạc đề / đòi tư vấn đầu tư / chèn lệnh.
Thứ tự: (1) chủ đề CẤM thắng mọi thứ (kể cả khi câu có số tiền); (2) cụm tài chính hoặc số tiền → qua; (3) ý định của Rasa (nếu Rasa trả lời và đủ chắc); (4) còn lại → ngoài phạm vi.

Cụm từ và câu từ chối nằm ở DB (``agent_scope_cues``, ``agent_refusal_templates``) để admin sửa mà không cần đổi code; ``app/data/agent_scope.json`` chỉ là bản mặc định để GIEO (chỉ thêm, DB thắng)
và để cổng vẫn chạy khi DB còn trống. So khớp trên câu đã bỏ dấu và theo ranh giới từ: "mưa" (→ "mua") không bị nhầm với "mua xe"."""

from __future__ import annotations

import functools
import json
import re
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from sqlalchemy import select
from sqlalchemy.dialects.postgresql import insert
from sqlalchemy.ext.asyncio import AsyncSession

from app.db.models.agent import AgentRefusalTemplate, AgentScopeCue
from app.services.vi_text import clean_text, fold

DATA_PATH = Path(__file__).resolve().parents[1] / "data" / "agent_scope.json"
FORBIDDEN_ORDER = ("prompt_injection", "other_users_data", "investment_advice")  # chủ đề đứng trước thắng khi một câu dính nhiều chủ đề
RASA_MIN_CONFIDENCE = 0.6
FINANCE_INTENTS = frozenset({"add_expense", "add_income", "transfer_money", "ask_spending"})
SMALLTALK_INTENTS = frozenset({"greet", "goodbye", "confirm", "deny", "cancel"})
CACHE_SECONDS = 60.0
DEFAULT_REFUSAL = "Mình chỉ hỗ trợ các câu hỏi về tài chính cá nhân của bạn. Bạn thử hỏi lại theo hướng đó nhé."  # dự phòng cuối: chỉ dùng khi cả DB lẫn bản mặc định đều thiếu
_AMOUNT = re.compile(r"\d[\d.,]*\s*(k|tr|trieu|nghin|ngan|dong|vnd|d)(?![a-z0-9])")  # cơ chế nhận số tiền, không phải từ vựng


@dataclass(frozen=True)
class GateRules:
    forbidden: dict[str, tuple[str, ...]]
    in_scope: tuple[str, ...]
    refusals: dict[str, str]


@dataclass(frozen=True)
class GateResult:
    in_scope: bool
    reason_code: str


def _defaults() -> dict[str, Any]:
    return json.loads(DATA_PATH.read_text(encoding="utf-8"))


def default_rules() -> GateRules:
    data = _defaults()
    return GateRules(
        forbidden={topic: tuple(phrases) for topic, phrases in data["forbidden"].items()},
        in_scope=tuple(data["in_scope"]),
        refusals=dict(data["refusals"]),
    )


@functools.lru_cache(maxsize=512)
def _pattern(phrase: str) -> re.Pattern[str]:
    return re.compile(rf"(?<![a-z0-9]){re.escape(phrase)}(?![a-z0-9])")


def _matches(phrases: tuple[str, ...], folded: str) -> bool:
    return any(_pattern(p).search(folded) for p in phrases)


async def evaluate(text: str, rules: GateRules, nlu: Any | None) -> GateResult:
    """``nlu`` là ``RasaNlu`` (hoặc None khi tắt); Rasa lỗi/chậm thì ``parse`` trả None và cổng vẫn quyết định bằng luật."""
    folded = " ".join(fold(clean_text(text)).split())

    topics = [t for t in FORBIDDEN_ORDER if t in rules.forbidden] + sorted(t for t in rules.forbidden if t not in FORBIDDEN_ORDER)
    for topic in topics:
        if _matches(rules.forbidden[topic], folded):
            return GateResult(False, topic)

    if _AMOUNT.search(folded) or _matches(rules.in_scope, folded):
        return GateResult(True, "finance_cue")

    if nlu is not None:
        parsed = await nlu.parse(clean_text(text))
        if parsed and parsed.get("confidence", 0.0) >= RASA_MIN_CONFIDENCE:
            if parsed.get("intent") in FINANCE_INTENTS:
                return GateResult(True, "rasa_intent")
            if parsed.get("intent") in SMALLTALK_INTENTS:
                return GateResult(True, "smalltalk")

    return GateResult(False, "out_of_scope")


def refusal_text(rules: GateRules, reason_code: str) -> str:
    return rules.refusals.get(reason_code) or rules.refusals.get("out_of_scope") or DEFAULT_REFUSAL


_cache: tuple[float, GateRules] | None = None


def reset_cache() -> None:
    global _cache
    _cache = None


async def load_rules(session: AsyncSession) -> GateRules:
    """Cụm từ + câu từ chối đang hoạt động trong DB, nạp lại mỗi ``CACHE_SECONDS``. DB chưa có cụm nào thì dùng bản mặc định; thiếu câu từ chối nào thì lấy câu mặc định của mã đó."""
    global _cache
    if _cache is not None and time.monotonic() - _cache[0] < CACHE_SECONDS:
        return _cache[1]

    defaults = default_rules()
    cues = (await session.execute(select(AgentScopeCue.kind, AgentScopeCue.topic, AgentScopeCue.phrase).where(AgentScopeCue.is_active.is_(True)).order_by(AgentScopeCue.phrase))).all()
    templates = (await session.execute(select(AgentRefusalTemplate.code, AgentRefusalTemplate.text).where(AgentRefusalTemplate.is_active.is_(True)))).all()

    if cues:
        forbidden: dict[str, list[str]] = {}
        for kind, topic, phrase in cues:
            if kind == "forbidden":
                forbidden.setdefault(topic, []).append(phrase)
        rules_forbidden = {topic: tuple(phrases) for topic, phrases in forbidden.items()}
        rules_in_scope = tuple(phrase for kind, _topic, phrase in cues if kind == "in_scope")
    else:
        rules_forbidden, rules_in_scope = defaults.forbidden, defaults.in_scope

    rules = GateRules(rules_forbidden, rules_in_scope, {**defaults.refusals, **dict(templates)})
    _cache = (time.monotonic(), rules)
    return rules


async def seed_defaults(session: AsyncSession) -> dict[str, int]:
    """Gieo bản mặc định vào DB: CHỈ thêm dòng còn thiếu, không bao giờ ghi đè dòng admin đã sửa. Trả số dòng mới thêm."""
    data = _defaults()
    cue_rows = [{"kind": "forbidden", "topic": topic, "phrase": phrase} for topic, phrases in data["forbidden"].items() for phrase in phrases]
    cue_rows += [{"kind": "in_scope", "topic": "finance", "phrase": phrase} for phrase in data["in_scope"]]
    cues = await session.execute(insert(AgentScopeCue).values(cue_rows).on_conflict_do_nothing(index_elements=["kind", "topic", "phrase"]))
    refusals = await session.execute(
        insert(AgentRefusalTemplate).values([{"code": code, "text": text} for code, text in data["refusals"].items()]).on_conflict_do_nothing(index_elements=["code"])
    )
    await session.commit()
    reset_cache()
    return {"cues": cues.rowcount, "refusals": refusals.rowcount}
