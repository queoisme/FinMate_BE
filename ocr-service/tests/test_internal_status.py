"""Trạng thái Google Vision cho backend giám sát: đếm lỗi liên tiếp đúng, cần khóa nội bộ, không lộ khóa."""
import io
import json
import pathlib
import sys
import urllib.error

import pytest
from fastapi.testclient import TestClient

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))

import main  # noqa: E402
from reading import google_vision, ocr  # noqa: E402

SECRET = "khoa-vision-bi-mat"
OK = {"responses": [{"fullTextAnnotation": {"pages": []}}]}


@pytest.fixture(autouse=True)
def fresh(monkeypatch):
    monkeypatch.setattr(google_vision, "_HEALTH", google_vision._Health())
    monkeypatch.setattr(google_vision._COOLDOWN, "until", 0.0)
    monkeypatch.setattr(google_vision, "api_key", lambda: SECRET)
    monkeypatch.delenv("FINMATE_OCR_ENGINE", raising=False)


def _fail_with(monkeypatch, error):
    def post(_request):
        raise error

    monkeypatch.setattr(google_vision, "_post", post)


def _read():
    google_vision._COOLDOWN.until = 0.0  # mỗi lần gọi như sau khi hết tạm nghỉ
    return google_vision.read_lines_or_none(b"anh")


def test_failures_count_up_with_the_http_code_then_a_success_resets(monkeypatch):
    _fail_with(monkeypatch, urllib.error.HTTPError("u", 429, "quota", {}, io.BytesIO()))
    _read()
    _read()

    status = google_vision.status()
    assert status["consecutive_failures"] == 2
    assert status["last_error_code"] == "429"
    assert status["last_error_at"] is not None

    monkeypatch.setattr(google_vision, "_post", lambda _r: OK)
    _read()

    status = google_vision.status()
    assert status["consecutive_failures"] == 0
    assert status["last_success_at"] is not None


def test_an_error_inside_a_200_response_counts_with_its_code(monkeypatch):
    monkeypatch.setattr(google_vision, "_post", lambda _r: {"responses": [{"error": {"code": 7, "message": "khóa sai"}}]})

    _read()

    status = google_vision.status()
    assert status["consecutive_failures"] == 1
    assert status["last_error_code"] == "7"
    assert "khóa sai" not in json.dumps(status)


def test_a_network_failure_records_the_type_name(monkeypatch):
    _fail_with(monkeypatch, TimeoutError("het gio"))

    _read()

    assert google_vision.status()["last_error_code"] == "TimeoutError"


def test_skipping_during_cooldown_is_not_counted(monkeypatch):
    _fail_with(monkeypatch, TimeoutError())
    _read()
    google_vision.read_lines_or_none(b"anh")  # đang tạm nghỉ: không gọi Google

    assert google_vision.status()["consecutive_failures"] == 1


def test_disabled_without_a_key_or_in_local_mode(monkeypatch):
    assert google_vision.status()["state"] == "enabled"

    monkeypatch.setenv("FINMATE_OCR_ENGINE", "local")
    assert google_vision.status()["state"] == "disabled"

    monkeypatch.delenv("FINMATE_OCR_ENGINE")
    monkeypatch.setattr(google_vision, "api_key", lambda: None)
    assert google_vision.status()["state"] == "disabled"


def test_status_stays_enabled_during_cooldown(monkeypatch):
    _fail_with(monkeypatch, TimeoutError())
    _read()

    assert google_vision.status()["state"] == "enabled"


def test_the_endpoint_returns_status_without_the_key():
    response = TestClient(main.app).get("/internal/status")

    assert response.status_code == 200
    assert response.json()["vision"]["state"] == "enabled"
    assert SECRET not in response.text


def test_the_endpoint_requires_the_internal_key(monkeypatch):
    main.app.dependency_overrides.pop(main.verify_internal_api_key, None)
    monkeypatch.setenv("OCR_SERVICE_API_KEY", "khoa-noi-bo")

    client = TestClient(main.app)
    assert client.get("/internal/status").status_code == 401
    assert client.get("/internal/status", headers={"Authorization": "Bearer sai"}).status_code == 401
    assert client.get("/internal/status", headers={"Authorization": "Bearer khoa-noi-bo"}).status_code == 200


def test_read_boxes_reports_which_engine_read_the_image(monkeypatch):
    monkeypatch.setattr(google_vision, "read_lines_or_none", lambda _b: [("Sua", 0.9, [(0, 0), (10, 0), (10, 10), (0, 10)])])
    ocr.read_boxes(b"anh")
    assert ocr.last_engine() == "google_vision"

    monkeypatch.setattr(google_vision, "read_lines_or_none", lambda _b: None)
    monkeypatch.setattr(ocr, "_read_boxes", lambda _b: [])
    ocr.read_boxes(b"anh")
    assert ocr.last_engine() == "local"
