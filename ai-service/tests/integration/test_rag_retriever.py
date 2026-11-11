"""Truy xuất tri thức trên Postgres + pgvector thật (Task 14): chỉ tài liệu Approved còn hiệu lực, có nguồn và ngày, lai vector + full-text, đệm theo phiên bản kho, và các route /kb."""

from datetime import UTC, datetime, timedelta

import pytest

from app.agent.rag import store
from app.agent.rag.cache import KbCache
from app.agent.rag.retriever import retrieve
from app.db.models.agent_kb import EMBEDDING_DIM
from tests.unit.test_rag_chunker import FakeRedis

NOW = datetime(2026, 11, 7, 12, 0, tzinfo=UTC)


def vec(axis: int) -> list[float]:
    """Vector đơn vị trên một trục: hai vector khác trục trực giao (khoảng cách cosine = 1), cùng trục thì trùng."""
    v = [0.0] * EMBEDDING_DIM
    v[axis] = 1.0
    return v


def embedder_for(mapping: dict[str, list[float]]):
    async def embed(texts: list[str]) -> list[list[float]]:
        return [mapping.get(t, vec(100)) for t in texts]

    return embed


async def add(session, text, axis, *, title="Tài liệu", source="Nguồn A", url=None, approve=True, cache=None, effective_from=None, effective_to=None, as_of=None):
    document_id = await store.add_document(
        session, title=title, source_name=source, source_url=url, as_of=as_of or NOW - timedelta(days=3),
        effective_from=effective_from or NOW - timedelta(days=30), effective_to=effective_to, chunks=[text], embeddings=[vec(axis)],
    )
    if approve:
        await store.set_status(session, document_id, "Approved", cache)
    return document_id


async def test_retrieval_skips_draft_and_retired(session):
    await add(session, "Quỹ khẩn cấp nên bằng ba đến sáu tháng chi tiêu.", 1, approve=False)  # Draft
    retired = await add(session, "Mẹo tiết kiệm cũ không còn đúng.", 1)
    await store.set_status(session, retired, "Retired")
    approved = await add(session, "Phong bì là cách chia tiền vào từng khoản chi.", 1)

    found = await retrieve(session, "chia tiền", 5, NOW, embedder=embedder_for({"chia tiền": vec(1)}), enabled=True)

    assert [e.text for e in found] == ["Phong bì là cách chia tiền vào từng khoản chi."]
    assert approved is not None


async def test_retrieval_skips_expired_and_not_yet_effective_documents(session):
    await add(session, "Mức giảm trừ gia cảnh cũ đã hết hiệu lực.", 1, effective_to=NOW - timedelta(days=1))
    await add(session, "Quy định sẽ có hiệu lực vào tháng sau.", 1, effective_from=NOW + timedelta(days=10))
    await add(session, "Quy định đang có hiệu lực.", 1, effective_to=NOW + timedelta(days=10))

    found = await retrieve(session, "quy định", 5, NOW, embedder=embedder_for({"quy định": vec(1)}), enabled=True)

    assert [e.text for e in found] == ["Quy định đang có hiệu lực."]


async def test_retrieval_returns_source_and_as_of(session):
    as_of = NOW - timedelta(days=9)
    await add(session, "Lạm phát mục tiêu được công bố hằng năm.", 2, source="Ngân hàng Nhà nước", url="https://example.org/lam-phat", as_of=as_of)

    (evidence,) = await retrieve(session, "lạm phát", 3, NOW, embedder=embedder_for({"lạm phát": vec(2)}), enabled=True)

    assert evidence.source == "document"
    assert evidence.source_name == "Ngân hàng Nhà nước" and evidence.source_url == "https://example.org/lam-phat"
    assert evidence.as_of == as_of
    assert evidence.value_vnd is None and 0 < evidence.confidence <= 1 and evidence.id.startswith("kb:")


async def test_exact_keyword_surfaces_via_full_text_even_when_the_vector_ranks_it_low(session):
    # đoạn khớp VECTOR nhưng không chứa từ khóa, và đoạn chứa "50/30/20" nhưng vector trực giao với truy vấn
    await add(session, "Chia thu nhập thành ba phần cho nhu cầu, mong muốn và tiết kiệm.", 5)
    await add(session, "Quy tắc 50/30/20 dành 20 phần trăm cho tiết kiệm.", 7)

    found = await retrieve(session, "50/30/20", 1, NOW, embedder=embedder_for({"50/30/20": vec(5)}), enabled=True)

    assert [e.text for e in found] == ["Quy tắc 50/30/20 dành 20 phần trăm cho tiết kiệm."]  # vector xếp nó thứ 2, full-text xếp nó thứ 1 → RRF đưa lên đầu


async def test_the_vector_alone_finds_a_paraphrase_with_no_shared_word(session):
    await add(session, "Chuẩn bị khoản dự phòng cho biến cố bất ngờ.", 3)

    found = await retrieve(session, "đồng nào cũng quý", 1, NOW, embedder=embedder_for({"đồng nào cũng quý": vec(3)}), enabled=True)

    assert len(found) == 1


async def test_k_limits_the_results(session):
    for i in range(4):
        await add(session, f"Đoạn số {i} về ngân sách.", 1)

    assert len(await retrieve(session, "ngân sách", 2, NOW, embedder=embedder_for({"ngân sách": vec(1)}), enabled=True)) == 2


async def test_cache_invalidated_when_document_approved_or_retired(session):
    cache = KbCache(FakeRedis())
    embedder = embedder_for({"quỹ": vec(1)})
    first = await add(session, "Quỹ dự phòng thứ nhất.", 1, cache=cache)
    assert [e.text for e in await retrieve(session, "quỹ", 5, NOW, cache=cache, embedder=embedder, enabled=True)] == ["Quỹ dự phòng thứ nhất."]

    second = await add(session, "Quỹ dự phòng thứ hai.", 1, cache=cache)  # duyệt thêm → đổi phiên bản
    assert len(await retrieve(session, "quỹ", 5, NOW, cache=cache, embedder=embedder, enabled=True)) == 2

    await store.set_status(session, first, "Retired", cache)  # gỡ → đổi phiên bản
    assert [e.text for e in await retrieve(session, "quỹ", 5, NOW, cache=cache, embedder=embedder, enabled=True)] == ["Quỹ dự phòng thứ hai."]
    assert second is not None


async def test_a_cache_hit_does_not_touch_the_database_or_the_embedder(session):
    cache = KbCache(FakeRedis())
    await add(session, "Quỹ dự phòng.", 1, cache=cache)
    calls = 0

    async def counting(texts):
        nonlocal calls
        calls += 1
        return [vec(1) for _ in texts]

    await retrieve(session, "quỹ", 3, NOW, cache=cache, embedder=counting, enabled=True)
    await retrieve(session, "quỹ", 3, NOW, cache=cache, embedder=counting, enabled=True)

    assert calls == 1


async def test_status_transitions_are_enforced(session):
    document = await add(session, "Một đoạn.", 1, approve=False)

    with pytest.raises(store.KbError) as retire_draft_as_approved_first:
        await store.set_status(session, document, "Draft")
    await store.set_status(session, document, "Approved")
    with pytest.raises(store.KbError) as back_to_draft:
        await store.set_status(session, document, "Draft")
    await store.set_status(session, document, "Retired")
    with pytest.raises(store.KbError) as revive:
        await store.set_status(session, document, "Approved")

    assert {e.value.code for e in (retire_draft_as_approved_first, back_to_draft, revive)} == {"kb_invalid_transition"}


async def test_a_chunk_embedding_count_mismatch_creates_nothing(session):
    with pytest.raises(store.KbError):
        await store.add_document(session, title="t", source_name="s", source_url=None, as_of=NOW, effective_from=NOW, effective_to=None, chunks=["a", "b"], embeddings=[vec(1)])

    total, _ = await store.list_documents(session, status=None, search=None, limit=10, offset=0)
    assert total == 0


# ---- route /api/v1/kb/* ----

@pytest.fixture
def kb_api(monkeypatch, client):
    from app.api.v1 import kb as kb_module
    from app.main import app

    async def fake_embed(texts, **kwargs):
        return [vec(1) for _ in texts]

    monkeypatch.setattr(kb_module, "embed", fake_embed)
    monkeypatch.setattr("app.agent.rag.retriever.embed", fake_embed)
    app.dependency_overrides[kb_module.get_kb_cache] = lambda: KbCache(FakeRedis())
    return client


def _body(**over):
    return {"title": "Phương pháp phong bì", "source_name": "Sổ tay FinMate", "as_of": "2026-11-01T00:00:00Z", "effective_from": "2026-11-01T00:00:00Z", "text": "Phong bì chia tiền theo khoản chi. Mỗi phong bì có một hạn mức.", **over}


async def test_create_is_draft_and_not_searchable_until_approved_then_not_after_retired(kb_api, auth_headers):
    created = await kb_api.post("/api/v1/admin/knowledge/documents", json=_body(), headers=auth_headers)
    assert created.status_code == 201 and created.json()["status"] == "Draft"
    document_id = created.json()["id"]

    async def search():
        response = await kb_api.post("/api/v1/kb/search", json={"query": "phong bì", "k": 3}, headers=auth_headers)
        assert response.status_code == 200
        return response.json()["evidence"]

    assert await search() == []
    assert (await kb_api.post(f"/api/v1/admin/knowledge/documents/{document_id}/approve", headers=auth_headers)).status_code == 200
    found = await search()
    assert found and found[0]["source_name"] == "Sổ tay FinMate" and found[0]["source"] == "document"
    assert (await kb_api.post(f"/api/v1/admin/knowledge/documents/{document_id}/retire", headers=auth_headers)).status_code == 200
    assert await search() == []


async def test_a_document_without_a_source_is_rejected_and_transitions_report_conflicts(kb_api, auth_headers):
    body = _body()
    del body["source_name"]
    assert (await kb_api.post("/api/v1/admin/knowledge/documents", json=body, headers=auth_headers)).status_code == 422

    document_id = (await kb_api.post("/api/v1/admin/knowledge/documents", json=_body(), headers=auth_headers)).json()["id"]
    assert (await kb_api.post(f"/api/v1/admin/knowledge/documents/{document_id}/retire", headers=auth_headers)).status_code == 200
    assert (await kb_api.post(f"/api/v1/admin/knowledge/documents/{document_id}/approve", headers=auth_headers)).status_code == 409
    assert (await kb_api.post("/api/v1/admin/knowledge/documents/00000000-0000-0000-0000-000000000000/approve", headers=auth_headers)).status_code == 404


async def test_listing_paginates_and_searches_on_the_server(kb_api, auth_headers):
    for i in range(3):
        await kb_api.post("/api/v1/admin/knowledge/documents", json=_body(title=f"Tài liệu {i}"), headers=auth_headers)

    page = (await kb_api.get("/api/v1/admin/knowledge/documents", params={"limit": 2, "offset": 0}, headers=auth_headers)).json()
    one = (await kb_api.get("/api/v1/admin/knowledge/documents", params={"search": "Tài liệu 1"}, headers=auth_headers)).json()

    assert page["total"] == 3 and len(page["items"]) == 2
    assert one["total"] == 1 and one["items"][0]["title"] == "Tài liệu 1"


async def test_the_kb_routes_need_the_internal_key(kb_api):
    assert (await kb_api.post("/api/v1/kb/search", json={"query": "x"})).status_code == 401


async def _text_branch_top(session, query: str) -> list[str]:
    """Thứ tự của RIÊNG nhánh full-text (bỏ vector), để kiểm xếp hạng từ khóa mà không bị nhiễu."""
    from sqlalchemy import text

    from app.agent.rag.retriever import _FETCH_SQL, _TEXT_SQL

    ids = (await session.execute(text(_TEXT_SQL), {"t": query, "now": NOW})).scalars().all()
    rows = {r["id"]: r["text"] for r in (await session.execute(text(_FETCH_SQL), {"ids": [str(i) for i in ids]})).mappings().all()}
    return [rows[i] for i in ids]


async def test_a_rare_exact_term_outranks_chunks_full_of_common_words(session):
    # Câu hỏi "Phương pháp 50/30/20 là gì?": cụm quyết định chỉ có ở MỘT đoạn, còn "phương pháp", "là", "gì" có ở rất nhiều đoạn khác.
    # Xếp theo số từ khớp thì các đoạn nhiều từ thông dụng thắng; xếp theo độ hiếm (IDF) thì đoạn chứa "50/30/20" đứng đầu.
    target = "Quy tắc 50/30/20 chia thu nhập thành ba phần cho nhu cầu, mong muốn và mục tiêu."
    await add(session, target, 1, title="Quy tắc 50/30/20")
    for axis in range(2, 9):
        await add(session, f"Phương pháp quản lý tiền là gì? Đây là cách số {axis} giúp bạn chi tiêu, và phương pháp này là một lựa chọn.", axis, title=f"Phương pháp {axis}")

    ranked = await _text_branch_top(session, "Phương pháp 50/30/20 là gì?")

    assert ranked[0] == target


async def test_terms_found_in_every_chunk_add_nothing(session):
    # Từ có ở mọi đoạn có điểm 0: một câu chỉ gồm từ như vậy không trả đoạn nào thay vì trả tất cả.
    for axis in range(1, 4):
        await add(session, f"Đoạn số {axis} nói về tiền.", axis, title=f"Tài liệu {axis}")

    assert await _text_branch_top(session, "tiền") == []
