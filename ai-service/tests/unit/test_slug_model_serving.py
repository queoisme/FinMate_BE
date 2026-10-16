"""Phục vụ mô hình nhóm đã duyệt: chỉ khi luật không có câu trả lời và xác suất >= ngưỡng; không có mô hình thì hệ thống y hệt cũ."""

import httpx
import pytest

from app.api.v1.bill import get_bill_knowledge
from app.bill import slug_model
from app.bill.slug_model import MIN_PROBABILITY, SlugModel, _pipeline
from app.core.config import settings
from app.main import app

HASH = "a" * 64
UNKNOWN_SHOP = "Qwerty Zzz"  # luật không biết cửa hàng này (xem test_bill_classifier)


class FakeKnowledge:
    async def refresh_if_stale(self):
        return None

    async def remembered(self, user_id_hash, merchant_name):
        return None


@pytest.fixture(autouse=True)
def isolate():
    app.dependency_overrides[get_bill_knowledge] = lambda: FakeKnowledge()
    slug_model.set_active(None)
    yield
    slug_model.set_active(None)
    app.dependency_overrides.pop(get_bill_knowledge, None)


def fitted(labels_to_words: dict[str, list[str]]) -> SlugModel:
    texts, labels = [], []
    for label, words in labels_to_words.items():
        for word in words:
            texts += [f"{word} {word}", f"zzq {word}"]
            labels += [label, label]
    return SlugModel(_pipeline().fit(texts, labels))


SIGNAL = {"transport": ["zqaaa", "zqbbb", "zqccc"], "health": ["zqddd", "zqeee", "zqfff"]}


async def classify(body):
    transport = httpx.ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        response = await client.post(
            "/api/v1/bill/classify", json={"user_id_hash": HASH, **body}, headers={"Authorization": f"Bearer {settings.internal_api_key}"}
        )
    assert response.status_code == 200
    return response.json()


async def test_no_active_model_changes_nothing():
    body = await classify({"merchant_name": UNKNOWN_SHOP, "lines": ["zqaaa zqaaa"]})
    assert body["source"] == "none" and body["category_slug"] is None


async def test_model_answers_when_the_rules_have_no_answer():
    slug_model.set_active(fitted(SIGNAL))
    body = await classify({"merchant_name": UNKNOWN_SHOP, "lines": ["zqaaa zqaaa"]})
    assert body["source"] == "model" and body["category_slug"] == "transport"
    assert body["local_category"] is None and 0 < body["confidence"] <= 0.8  # hóa đơn luôn cần người xem lại


async def test_rules_still_win_when_they_know_the_shop():
    slug_model.set_active(fitted({"health": ["highlands", "coffee"], "transport": ["xang"]}))  # mô hình khăng khăng là health
    body = await classify({"merchant_name": "Highlands Coffee", "lines": ["Ca phe sua"]})
    assert body["source"] != "model" and body["category_slug"] == "food"


async def test_below_threshold_the_model_stays_silent():
    slug_model.set_active(fitted(SIGNAL))
    body = await classify({"merchant_name": UNKNOWN_SHOP, "lines": ["zqaaa zqddd"]})  # hai nhóm ngang nhau ~0.5
    assert body["source"] == "none" and body["category_slug"] is None


async def test_a_slug_outside_the_valid_set_is_ignored():
    slug_model.set_active(fitted({"income": ["zqggg", "zqhhh"], "transport": ["zqaaa"]}))
    body = await classify({"merchant_name": UNKNOWN_SHOP, "lines": ["zqggg zqggg"]})
    assert body["source"] == "none"


def test_predict_reports_probability_and_threshold_constant():
    slug, probability = fitted(SIGNAL).predict("Qwerty Zzz", ["zqddd zqddd"])
    assert slug == "health" and MIN_PROBABILITY <= probability <= 1


def test_corrupt_model_file_is_ignored(tmp_path):
    broken = tmp_path / "x.joblib"
    broken.write_bytes(b"not a joblib file")
    assert SlugModel.load_file(broken) is None
    assert SlugModel.load_file(tmp_path / "missing.joblib") is None
