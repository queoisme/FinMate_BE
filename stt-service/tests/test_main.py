import importlib

import pytest
from fastapi.testclient import TestClient

KEY = "k" * 32


@pytest.fixture
def client(monkeypatch):
    monkeypatch.setenv("STT_SERVICE_API_KEY", KEY)
    import main
    importlib.reload(main)

    class Fake:
        def recognize(self, data):
            return {"text": "ĂN PHỞ BỐN MƯƠI NGHÌN", "duration_ms": 1500, "engine": "fake"}

    main.slot.engine = Fake()
    return TestClient(main.app)


def post(client, key=KEY, data=b"abc", ctype="audio/wav"):
    headers = {"Authorization": f"Bearer {key}"} if key else {}
    return client.post("/transcribe", headers=headers, files={"file": ("a.wav", data, ctype)})


def test_returns_lowercase_text(client):
    # GIPFormer trả chữ HOA; chat/parse của ai-service đọc chữ thường.
    r = post(client)
    assert r.status_code == 200 and r.json()["text"] == "ăn phở bốn mươi nghìn"


def test_rejects_wrong_or_missing_key(client):
    assert post(client, key="x").status_code == 401
    assert post(client, key=None).status_code == 401


def test_rejects_non_audio(client):
    assert post(client, ctype="image/png").status_code == 415


def test_rejects_empty_and_oversized(client):
    assert post(client, data=b"").status_code in (413, 422)
    assert post(client, data=b"x" * (10 * 1024 * 1024 + 10)).status_code == 413


def test_health_open(client):
    assert client.get("/health").status_code == 200


def test_decode_rejects_garbage():
    import speech
    with pytest.raises(ValueError):
        speech.decode_audio(b"not audio at all")
