"""Trang admin "Token & chi phí AI": tổng hợp token từ agent_runs. Dữ liệu tự dựng, đối chiếu với tổng tính tay; không trả băm người dùng; chỉ nhận khóa nội bộ."""

import hashlib
import uuid
from datetime import UTC, datetime, timedelta

import pytest
from sqlalchemy import text

from app.core.config import settings

USER_HASH = hashlib.sha256(b"usage-user").hexdigest()


async def _add(session, trace, step, tool, tokens_in, tokens_out, age_days=0):
    await session.execute(
        text(
            "INSERT INTO agent_runs (id, trace_id, user_id_hash, step, tool, latency_ms, input_tokens, output_tokens, created_at) "
            "VALUES (:id, :trace, :user, :step, :tool, 10, :tin, :tout, :at)"
        ),
        {"id": uuid.uuid4(), "trace": trace, "user": USER_HASH, "step": step, "tool": tool, "tin": tokens_in, "tout": tokens_out, "at": datetime.now(UTC) - timedelta(days=age_days)},
    )


@pytest.mark.asyncio
async def test_summary_adds_up_tokens_per_step_tool_and_day(client, auth_headers, session):
    a, b, old = uuid.uuid4(), uuid.uuid4(), uuid.uuid4()
    for trace in (a, b):
        await _add(session, trace, "gate", None, 0, 0)
        await _add(session, trace, "plan", None, 100, 10)
        await _add(session, trace, "compose", None, 200, 40)
    await _add(session, old, "plan", None, 5000, 500, age_days=60)  # ngoài khoảng 30 ngày
    await session.commit()

    body = (await client.get("/api/v1/admin/agent-usage/summary", params={"days": 30}, headers=auth_headers)).json()

    assert body["total"] == {"runs": 2, "input_tokens": 600, "output_tokens": 100}
    assert body["model"] == settings.agent_model
    steps = {row["step"]: row for row in body["by_step"]}
    assert (steps["plan"]["input_tokens"], steps["compose"]["output_tokens"], steps["gate"]["input_tokens"]) == (200, 80, 0)
    assert len(body["by_day"]) == 1 and body["by_day"][0]["runs"] == 2 and body["by_day"][0]["input_tokens"] == 600

    wide = (await client.get("/api/v1/admin/agent-usage/summary", params={"days": 90}, headers=auth_headers)).json()
    assert wide["total"]["input_tokens"] == 5600 and wide["total"]["runs"] == 3
    assert USER_HASH not in str(body)


@pytest.mark.asyncio
async def test_summary_of_an_empty_table_is_zero_and_needs_the_internal_key(client, auth_headers):
    empty = (await client.get("/api/v1/admin/agent-usage/summary", headers=auth_headers)).json()
    assert empty["total"] == {"runs": 0, "input_tokens": 0, "output_tokens": 0} and empty["by_day"] == []
    assert (await client.get("/api/v1/admin/agent-usage/summary")).status_code == 401
    assert (await client.get("/api/v1/admin/agent-usage/summary", params={"days": 0}, headers=auth_headers)).status_code == 422
