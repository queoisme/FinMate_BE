"""RAG phần không cần DB (Task 14): cắt đoạn theo ranh giới câu, gộp xếp hạng RRF, gọi API embedding (giả), và bộ nhớ đệm."""

import json
import uuid

import httpx
import pytest

from app.agent.rag import cache as kb_cache
from app.agent.rag.chunker import MAX_CHARS, chunk
from app.agent.rag.embedder import EmbeddingError, embed, format_input
from app.agent.rag.retriever import retrieve, rrf


def test_chunker_keeps_sentence_boundaries():
    sentences = [f"Câu số {i} nói về việc lập ngân sách hằng tháng cho người có thu nhập không ổn định." for i in range(12)]

    chunks = chunk(" ".join(sentences), max_chars=250)

    assert len(chunks) > 1
    assert all(len(c) <= 250 for c in chunks)
    assert all(c.endswith(".") for c in chunks)  # không đoạn nào kết thúc giữa câu
    assert " ".join(chunks) == " ".join(sentences)  # không mất, không thêm chữ nào


def test_chunker_splits_an_overlong_sentence_at_a_space_and_never_returns_empty_chunks():
    long_sentence = " ".join(["từ"] * 400)

    chunks = chunk(long_sentence, max_chars=100)

    assert all(0 < len(c) <= 100 for c in chunks)
    assert " ".join(chunks).split() == long_sentence.split()
    assert chunk("   \n\n  ") == []
    assert MAX_CHARS < 500  # vừa giới hạn Evidence.text


def test_chunker_does_not_split_decimal_numbers_or_ratios():
    assert chunk("Quy tắc 50/30/20 chia thu nhập. Mức 1.5 triệu là ví dụ.") == ["Quy tắc 50/30/20 chia thu nhập. Mức 1.5 triệu là ví dụ."]


def test_rrf_merges_both_rankings():
    a, b, c = (uuid.uuid4() for _ in range(3))

    # b đứng thứ 2 ở cả hai bảng; a chỉ đứng đầu một bảng; c đứng đầu bảng kia
    assert rrf([[a, b], [c, b]]) == [b, a, c]
    assert rrf([]) == []
    assert rrf([[a], []]) == [a]


def test_rrf_scores_follow_one_over_sixty_plus_rank():
    a, b = uuid.uuid4(), uuid.uuid4()
    # a: 1/61 (bảng 1) ; b: 1/62 + 1/61 (cả hai bảng) → b đứng trước dù không đứng đầu bảng nào
    assert rrf([[a, b], [b]]) == [b, a]


def _transport(values, status=200, seen=None):
    def handler(request: httpx.Request) -> httpx.Response:
        if seen is not None:
            seen.append(json.loads(request.content))
        return httpx.Response(status, json={"embedding": {"values": values}})

    return httpx.MockTransport(handler)


async def test_embedding_dim_mismatch_is_rejected():
    with pytest.raises(EmbeddingError) as error:
        await embed(["x"], model="m", dim=4, key_source=lambda: "k", transport=_transport([0.1, 0.2, 0.3]))

    assert error.value.code == "embedding_dim_mismatch"


async def test_embedding_sends_the_task_in_the_text_and_the_dimension_and_returns_floats():
    seen: list[dict] = []

    vectors = await embed(["quy tắc 50/30/20"], kind="query", model="m", dim=3, key_source=lambda: "k", transport=_transport([1, 2, 3], seen=seen))

    assert vectors == [[1.0, 2.0, 3.0]]
    assert seen[0]["output_dimensionality"] == 3
    assert seen[0]["content"]["parts"][0]["text"] == "task: search result | query: quy tắc 50/30/20"
    assert format_input("nội dung", "document", "Tiêu đề") == "title: Tiêu đề | text: nội dung"
    assert format_input("nội dung", "document") == "title: none | text: nội dung"


async def test_embedding_needs_a_model_and_a_key_and_hides_provider_errors():
    with pytest.raises(EmbeddingError) as no_model:
        await embed(["x"], model="", dim=3, key_source=lambda: "k")
    with pytest.raises(EmbeddingError) as no_key:
        await embed(["x"], model="m", dim=3, key_source=lambda: None)
    with pytest.raises(EmbeddingError) as down:
        await embed(["x"], model="m", dim=3, key_source=lambda: "k", transport=_transport([], status=500))

    assert (no_model.value.code, no_key.value.code, down.value.code) == ("embedding_not_configured", "embedding_key_missing", "embedding_unavailable")


async def test_rag_disabled_flag_returns_empty_without_touching_anything():
    class Boom:
        def __getattr__(self, name):
            raise AssertionError("không được chạm vào DB khi RAG tắt")

    assert await retrieve(Boom(), "quy tắc 50/30/20", 3, None, enabled=False) == []  # type: ignore[arg-type]
    assert await retrieve(Boom(), "   ", 3, None, enabled=True) == []  # type: ignore[arg-type]


class FakeRedis:
    def __init__(self):
        self.data: dict[str, str] = {}
        self.ttls: dict[str, int] = {}

    async def get(self, key):
        return self.data.get(key)

    async def set(self, key, value, ex=None):
        self.data[key] = value
        self.ttls[key] = ex

    async def incr(self, key):
        self.data[key] = str(int(self.data.get(key, "0")) + 1)


async def test_cache_hit_skips_database():
    redis = FakeRedis()
    cache = kb_cache.KbCache(redis)
    calls = 0

    async def loader():
        nonlocal calls
        calls += 1
        return [{"id": "kb:1"}]

    first = await cache.get_or_set("h", 0, loader)
    second = await cache.get_or_set("h", 0, loader)

    assert first == second == [{"id": "kb:1"}]
    assert calls == 1
    assert set(redis.ttls.values()) == {kb_cache.TTL_SECONDS}


async def test_cache_invalidated_when_the_version_is_bumped():
    cache = kb_cache.KbCache(FakeRedis())
    calls = 0

    async def loader():
        nonlocal calls
        calls += 1
        return []

    await cache.get_or_set("h", await cache.version(), loader)
    await cache.bump_version()  # tài liệu được duyệt hoặc gỡ
    await cache.get_or_set("h", await cache.version(), loader)

    assert calls == 2


async def test_cache_never_stores_user_specific_data():
    redis = FakeRedis()
    cache = kb_cache.KbCache(redis)
    secret_question = "Lương của tôi 18 triệu, quy tắc 50/30/20 áp dụng thế nào?"

    async def loader():
        return [{"id": "kb:1", "text": "Quy tắc 50/30/20 chia thu nhập."}]

    await cache.get_or_set(kb_cache.query_hash(secret_question, 3), 0, loader)

    stored = " ".join([*redis.data.keys(), *redis.data.values()])
    assert "18 triệu" not in stored and "Lương" not in stored  # khóa chỉ là mã băm, giá trị chỉ là nội dung tài liệu chung
    assert kb_cache.query_hash("  Quy Tắc  50/30/20 ", 3) == kb_cache.query_hash("quy tắc 50/30/20", 3)
    assert kb_cache.query_hash("quy tắc", 3) != kb_cache.query_hash("quy tắc", 5)


async def test_a_broken_redis_does_not_break_the_lookup():
    class Broken:
        async def get(self, key):
            raise ConnectionError("redis down")

        set = incr = get

    cache = kb_cache.KbCache(Broken())

    async def loader():
        return [{"id": "kb:1"}]

    assert await cache.get_or_set("h", await cache.version(), loader) == [{"id": "kb:1"}]
    await cache.bump_version()  # không ném
