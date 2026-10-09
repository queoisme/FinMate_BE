"""Chỉ backend được gọi OCR service: thiếu hoặc sai khóa nội bộ thì bị từ chối, /health vẫn mở."""
import io

import pytest
from fastapi.testclient import TestClient
from PIL import Image

import main


@pytest.fixture
def client():
    main.app.dependency_overrides.pop(main.verify_internal_api_key, None)  # dùng xác thực thật
    return TestClient(main.app)


def _png() -> bytes:
    buf = io.BytesIO()
    Image.new("RGB", (8, 8), "white").save(buf, "PNG")
    return buf.getvalue()


def _post(client, headers=None):
    return client.post("/analyze-bill", files={"image": ("b.png", _png(), "image/png")}, headers=headers or {})


def test_health_is_open_without_a_key(client):
    assert client.get("/health").status_code == 200


def test_service_refuses_everything_when_no_key_is_configured(client, monkeypatch):
    monkeypatch.delenv("OCR_SERVICE_API_KEY", raising=False)
    assert _post(client, {"Authorization": "Bearer anything"}).status_code == 503


def test_missing_or_wrong_key_is_unauthorized(client, monkeypatch):
    monkeypatch.setenv("OCR_SERVICE_API_KEY", "k" * 40)
    assert _post(client).status_code == 401
    assert _post(client, {"Authorization": "Bearer wrong"}).status_code == 401
    assert _post(client, {"Authorization": "k" * 40}).status_code == 401  # thiếu chữ Bearer
    assert client.post("/merchant-category", json={"merchant": "a", "category": "dining"}).status_code == 401


def test_right_key_reaches_the_endpoint(client, monkeypatch):
    monkeypatch.setenv("OCR_SERVICE_API_KEY", "k" * 40)
    assert _post(client, {"Authorization": "Bearer " + "k" * 40}).status_code != 401
