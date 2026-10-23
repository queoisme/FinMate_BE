"""Lối tắt quen thuộc của ASP.NET: /swagger/index.html (và /swagger) mở Swagger UI của FastAPI ở /docs. /docs vẫn giữ nguyên."""

import httpx

from app.main import app


def _client() -> httpx.AsyncClient:
    # Không chạy lifespan nên không cần Postgres.
    return httpx.AsyncClient(transport=httpx.ASGITransport(app=app), base_url="http://test")


async def test_the_swagger_shortcut_redirects_to_the_docs_page():
    async with _client() as client:
        for path in ("/swagger/index.html", "/swagger"):
            res = await client.get(path)
            assert res.status_code in (302, 307) and res.headers["location"] == "/docs", path


async def test_the_docs_page_itself_still_works():
    async with _client() as client:
        assert (await client.get("/docs")).status_code == 200


def test_the_shortcut_is_not_listed_as_an_api_endpoint():
    assert not [p for p in app.openapi()["paths"] if p.startswith("/swagger")]
