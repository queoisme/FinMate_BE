"""Ghi lịch sử huấn luyện (lần chạy, phiên bản, số đo theo danh mục) qua API của AI Service. Chạy bằng AI Service giả (xem fake_ai_api.py)."""
import pathlib
import sys

import pytest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))

import train_category_model  # noqa: E402
from storage.training_api import TrainingRecorder  # noqa: E402
from tests.fake_ai_api import FakeAiApi  # noqa: E402


@pytest.fixture
def api():
    return FakeAiApi()


class _Rows:
    def __init__(self, rows):
        self._rows = rows

    def read_all(self):
        return self._rows


def _toy(n_per_class=15, source="user"):
    rows = []
    for i in range(n_per_class):
        rows.append({"merchant": f"Quan Pho So {i}", "category": "dining", "label_source": source, "lines": ["Pho bo tai", "Tra da", "Com suon nuong"], "total_amount": 50000})
        rows.append({"merchant": f"Tram Xang So {i}", "category": "transport", "label_source": source, "lines": ["Xang RON 95", "Gui xe may", "Rua xe"], "total_amount": 90000})
    return rows


def test_a_passing_run_is_recorded_as_active_with_per_category_metrics_and_a_versioned_file(api, tmp_path):
    code = train_category_model.run(_Rows(_toy()), tmp_path / "category_model.joblib", recorder=TrainingRecorder(api))
    assert code == 0 and (tmp_path / "category_model.joblib").exists()
    (job,) = api.jobs.values()
    assert (job["status"], job["sample_count"], job["error"], job["stage"]) == ("succeeded", 30, None, "bill_category")
    (version,) = api.versions
    assert version["status"] == "active" and pathlib.Path(version["artifact_path"]).exists() and "category_models" in version["artifact_path"]  # bản có phiên bản, quay lại được
    assert version["cv_accuracy"] >= 0.8 and version["classes"] == {"dining": 15, "transport": 15}
    assert sorted(label for _, label, *_ in api.metrics) == ["dining", "transport"]


def test_a_rejected_run_is_recorded_too_and_creates_no_model_file(api, tmp_path):
    code = train_category_model.run(_Rows(_toy(3)), tmp_path / "category_model.joblib", recorder=TrainingRecorder(api))  # thiếu dữ liệu
    assert code == 1 and not (tmp_path / "category_model.joblib").exists()
    (version,) = api.versions
    assert version["status"] == "rejected" and version["artifact_path"] == "" and "30" in version["reason"]
    assert version["cv_accuracy"] is None and api.metrics == []  # chưa đo được thì chưa có số đo theo danh mục
    assert [job["status"] for job in api.jobs.values()] == ["succeeded"]


def test_a_new_active_model_retires_the_previous_one(api, tmp_path):
    rec = TrainingRecorder(api)
    train_category_model.run(_Rows(_toy()), tmp_path / "m.joblib", recorder=rec)
    import time
    time.sleep(1.1)  # phiên bản theo giây
    train_category_model.run(_Rows(_toy()), tmp_path / "m.joblib", recorder=rec)
    assert sorted(v["status"] for v in api.versions) == ["active", "retired"]


def test_synthetic_rows_from_the_database_train_but_are_never_measured(api, tmp_path):
    rows = _toy() + _toy(10, source="synthetic_ocr")
    train_category_model.run(_Rows(rows), tmp_path / "m.joblib", recorder=TrainingRecorder(api))
    (job,) = api.jobs.values()
    assert job["params"]["real_rows"] == 30 and job["params"]["synthetic_rows"] == 20
    assert job["sample_count"] == 30  # số đo chỉ tính bill thật


def test_a_crashing_run_is_recorded_as_failed_and_still_raises(api, tmp_path, monkeypatch):
    monkeypatch.setattr(train_category_model, "train", lambda *a, **k: (_ for _ in ()).throw(RuntimeError("boom")))
    with pytest.raises(RuntimeError):
        train_category_model.run(_Rows(_toy()), tmp_path / "m.joblib", recorder=TrainingRecorder(api))
    (job,) = api.jobs.values()
    assert job["status"] == "failed" and "boom" in job["error"]
    assert api.versions == []


def test_without_a_recorder_training_works_exactly_as_before(tmp_path):
    assert train_category_model.run(_Rows(_toy()), tmp_path / "m.joblib") == 0
