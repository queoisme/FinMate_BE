"""Ba bảng nền của agent trên Postgres thật: vết chạy, câu từ chối mẫu, bộ nhớ dài hạn.

Điều cần giữ ở tầng DB, không chỉ trong code: AI DB chỉ thấy SHA-256 của user id (AGENTS.md §3.1), bước của vòng agent thuộc một danh sách cố định,
và bộ nhớ dài hạn bị chặn ở 200 ký tự để không thành chỗ chứa dữ liệu tự do của người dùng.
"""

import pytest
from sqlalchemy import text
from sqlalchemy.exc import DBAPIError, IntegrityError  # asyncpg báo "value too long" là DBAPIError chung, không phải DataError

TABLES = ["agent_refusal_templates", "agent_runs", "agent_user_memory"]


async def test_agent_tables_exist(session):
    rows = (
        await session.execute(
            text("SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_name = ANY(:names) ORDER BY table_name"),
            {"names": TABLES},
        )
    ).scalars().all()
    assert rows == TABLES


async def _insert_run(session, user_hash: str, step: str = "plan"):
    await session.execute(
        text(
            "INSERT INTO agent_runs (trace_id, user_id_hash, step, latency_ms, input_tokens, output_tokens)"
            " VALUES (gen_random_uuid(), :h, :step, 12, 100, 20)"
        ),
        {"h": user_hash, "step": step},
    )


async def test_agent_run_is_stored_under_a_hash(session, user_hash):
    await _insert_run(session, user_hash)
    await session.commit()
    count = (await session.execute(text("SELECT count(*) FROM agent_runs WHERE user_id_hash = :h"), {"h": user_hash})).scalar_one()
    assert count == 1


async def test_agent_run_rejects_a_real_user_id(session):
    with pytest.raises(IntegrityError):
        await _insert_run(session, "3f2504e0-4f89-11d3-9a0c-0305e82c3301")
    await session.rollback()


async def test_agent_run_rejects_an_unknown_step(session, user_hash):
    with pytest.raises(IntegrityError):
        await _insert_run(session, user_hash, step="improvise")
    await session.rollback()


async def test_refusal_template_code_is_unique(session):
    insert = text("INSERT INTO agent_refusal_templates (code, text) VALUES ('out_of_scope', 'Mình chỉ hỗ trợ chuyện tài chính cá nhân.')")
    await session.execute(insert)
    with pytest.raises(IntegrityError):
        await session.execute(insert)
    await session.rollback()


async def test_user_memory_is_keyed_by_hash_and_key(session, user_hash):
    insert = text("INSERT INTO agent_user_memory (user_id_hash, key, value) VALUES (:h, 'preferred_method', 'phong bì')")
    await session.execute(insert, {"h": user_hash})
    with pytest.raises(IntegrityError):
        await session.execute(insert, {"h": user_hash})
    await session.rollback()


async def test_user_memory_rejects_a_real_user_id_and_long_values(session, user_hash):
    insert = text("INSERT INTO agent_user_memory (user_id_hash, key, value) VALUES (:h, 'k', :v)")
    with pytest.raises(IntegrityError):
        await session.execute(insert, {"h": "not-a-hash", "v": "x"})
    await session.rollback()
    with pytest.raises(DBAPIError):
        await session.execute(insert, {"h": user_hash, "v": "x" * 201})
    await session.rollback()
