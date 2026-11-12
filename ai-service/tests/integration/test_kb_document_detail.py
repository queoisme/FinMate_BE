"""Xem một tài liệu tri thức kèm toàn bộ nội dung, để người duyệt đọc trước khi duyệt (Postgres thật)."""

import uuid
from datetime import UTC, datetime, timedelta

from app.agent.rag import store
from tests.integration.test_rag_retriever import vec

NOW = datetime(2026, 11, 9, 12, 0, tzinfo=UTC)


async def _add(session, chunks: list[str], *, title="Quy tắc 50/30/20 (v1)") -> uuid.UUID:
    return await store.add_document(
        session, title=title, source_name="FinMate — phương pháp 50/30/20", source_url=None, as_of=NOW - timedelta(days=1),
        effective_from=NOW - timedelta(days=1), effective_to=None, chunks=chunks, embeddings=[vec(i) for i in range(len(chunks))],
    )


async def test_the_document_comes_back_with_its_chunks_joined_in_order(session):
    document_id = await _add(session, ["Câu đầu tiên.", "Câu thứ hai.", "Câu cuối cùng."])

    document = await store.get_document(session, document_id)

    assert document is not None
    assert document["text"] == "Câu đầu tiên. Câu thứ hai. Câu cuối cùng."
    assert (document["title"], document["status"], document["chunks"]) == ("Quy tắc 50/30/20 (v1)", "Draft", 3)


async def test_an_unknown_id_is_none(session):
    assert await store.get_document(session, uuid.uuid4()) is None


async def test_the_route_returns_the_text_and_404_for_unknown_ids(client, auth_headers, session):
    document_id = await _add(session, ["Nội dung để duyệt."])

    found = await client.get(f"/api/v1/admin/knowledge/documents/{document_id}", headers=auth_headers)
    missing = await client.get(f"/api/v1/admin/knowledge/documents/{uuid.uuid4()}", headers=auth_headers)

    assert found.status_code == 200
    assert found.json()["text"] == "Nội dung để duyệt."
    assert found.json()["id"] == str(document_id)
    assert missing.status_code == 404
    assert missing.json()["detail"] == "kb_document_not_found"


async def test_the_route_needs_the_internal_key(client, session):
    document_id = await _add(session, ["Chưa có khóa."])

    response = await client.get(f"/api/v1/admin/knowledge/documents/{document_id}")

    assert response.status_code in (401, 403)
