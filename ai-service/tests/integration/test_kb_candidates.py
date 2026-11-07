"""Ứng viên tri thức (Task 15): chỉ mã băm và số lần gặp, không bao giờ tự vào kho."""

import hashlib

from sqlalchemy import text

from app.agent.rag import candidates

QUESTION = "Lương 18 triệu của tôi thì quy tắc 50/30/20 áp dụng thế nào?"


async def test_candidate_stores_hash_only(client, auth_headers, session):
    response = await client.post("/api/v1/kb/candidates", json={"text": QUESTION}, headers=auth_headers)
    assert response.status_code == 200 and response.json() == {"recorded": True}

    columns = (await session.execute(text("SELECT column_name FROM information_schema.columns WHERE table_name = 'agent_kb_candidates'"))).scalars().all()
    dump = " ".join(str(v) for row in (await session.execute(text("SELECT * FROM agent_kb_candidates"))).all() for v in row)
    assert not {"text", "question", "user_id_hash"} & set(columns)  # không có cột nào chứa nội dung hay người dùng
    assert "18 triệu" not in dump and "Lương" not in dump
    assert candidates.question_hash(QUESTION) in dump


async def test_the_same_question_written_differently_counts_up_one_row(client, auth_headers):
    for variant in (QUESTION, QUESTION.upper(), f"  {QUESTION}  ??", QUESTION.replace("Lương", "lương")):
        await client.post("/api/v1/kb/candidates", json={"text": variant}, headers=auth_headers)
    await client.post("/api/v1/kb/candidates", json={"text": "Quỹ khẩn cấp là gì?"}, headers=auth_headers)

    listing = (await client.get("/api/v1/admin/knowledge/candidates", headers=auth_headers)).json()

    assert listing["total"] == 2
    assert [i["count"] for i in listing["items"]] == [4, 1]  # nhiều lần gặp nhất đứng đầu
    assert all(len(i["question_hash"]) == 64 and "text" not in i for i in listing["items"])


async def test_candidate_never_enters_kb_without_approval(client, auth_headers, session):
    await client.post("/api/v1/kb/candidates", json={"text": QUESTION}, headers=auth_headers)
    (item,) = (await client.get("/api/v1/admin/knowledge/candidates", headers=auth_headers)).json()["items"]

    resolved = await client.post(f"/api/v1/admin/knowledge/candidates/{item['id']}/resolve", json={"status": "Covered"}, headers=auth_headers)

    assert resolved.status_code == 200 and resolved.json()["status"] == "Covered"
    assert (await session.execute(text("SELECT count(*) FROM agent_kb_documents"))).scalar_one() == 0
    assert (await session.execute(text("SELECT count(*) FROM agent_kb_chunks"))).scalar_one() == 0


async def test_resolving_filters_by_status_and_unknown_ids_are_404(client, auth_headers):
    await client.post("/api/v1/kb/candidates", json={"text": QUESTION}, headers=auth_headers)
    (item,) = (await client.get("/api/v1/admin/knowledge/candidates", headers=auth_headers)).json()["items"]
    await client.post(f"/api/v1/admin/knowledge/candidates/{item['id']}/resolve", json={"status": "Dismissed"}, headers=auth_headers)

    new_only = (await client.get("/api/v1/admin/knowledge/candidates", params={"status": "New"}, headers=auth_headers)).json()
    bad_status = await client.post(f"/api/v1/admin/knowledge/candidates/{item['id']}/resolve", json={"status": "New"}, headers=auth_headers)
    missing = await client.post("/api/v1/admin/knowledge/candidates/00000000-0000-0000-0000-000000000000/resolve", json={"status": "Dismissed"}, headers=auth_headers)

    assert new_only["total"] == 0
    assert bad_status.status_code == 422 and missing.status_code == 404


def test_the_hash_is_a_sha256_of_the_normalised_question():
    assert candidates.question_hash("Quỹ  khẩn cấp là gì?") == hashlib.sha256("quỹ khẩn cấp là gì".encode()).hexdigest()


async def test_the_candidate_routes_need_the_internal_key(client):
    assert (await client.post("/api/v1/kb/candidates", json={"text": "x"})).status_code == 401
    assert (await client.get("/api/v1/admin/knowledge/candidates")).status_code == 401
