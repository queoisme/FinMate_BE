"""Ghi lịch sử huấn luyện vào bill_training_jobs / bill_model_versions / evaluation_*. Chạy bằng SQLite giả lập (xem pg_fake.py)."""
import json
import pathlib
import sys

import pytest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))

import train_category_model  # noqa: E402
from storage.storage_pg import _cursor  # noqa: E402
from tests.pg_fake import make_connect  # noqa: E402
from storage.training_pg import TrainingRecorder  # noqa: E402


@pytest.fixture
def connect(tmp_path):
    return make_connect(tmp_path / "pg.sqlite")


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


def _q(connect, sql, params=()):
    with _cursor(connect) as cur:
        cur.execute(sql, params)
        return cur.fetchall()


def test_a_passing_run_is_recorded_as_active_with_per_category_metrics_and_a_versioned_file(connect, tmp_path):
    code = train_category_model.run(_Rows(_toy()), tmp_path / "category_model.joblib", recorder=TrainingRecorder(connect))
    assert code == 0 and (tmp_path / "category_model.joblib").exists()
    job = _q(connect, "SELECT status, sample_count, error_message FROM bill_training_jobs")[0]
    assert job == ("succeeded", 30, None)
    status, path = _q(connect, "SELECT status, artifact_path FROM bill_model_versions")[0]
    assert status == "active" and pathlib.Path(path).exists() and "category_models" in path  # bản có phiên bản, quay lại được
    run = _q(connect, "SELECT split, sample_count, accuracy, macro_f1 FROM bill_evaluation_runs")[0]
    assert run[0] == "cv" and run[1] == 30 and run[2] >= 0.8
    labels = sorted(r[0] for r in _q(connect, "SELECT label FROM bill_evaluation_category_metrics"))
    assert labels == ["dining", "transport"]


def test_a_rejected_run_is_recorded_too_and_creates_no_model_file(connect, tmp_path):
    code = train_category_model.run(_Rows(_toy(3)), tmp_path / "category_model.joblib", recorder=TrainingRecorder(connect))  # thiếu dữ liệu
    assert code == 1 and not (tmp_path / "category_model.joblib").exists()
    status, path, notes = _q(connect, "SELECT status, artifact_path, notes FROM bill_model_versions")[0]
    assert status == "rejected" and path == "" and "30" in notes
    assert _q(connect, "SELECT count(*) FROM bill_evaluation_runs")[0][0] == 0  # chưa đo được thì chưa có bảng đánh giá
    assert _q(connect, "SELECT status FROM bill_training_jobs")[0][0] == "succeeded"


def test_a_new_active_model_retires_the_previous_one(connect, tmp_path):
    rec = TrainingRecorder(connect)
    train_category_model.run(_Rows(_toy()), tmp_path / "m.joblib", recorder=rec)
    import time
    time.sleep(1.1)  # phiên bản theo giây
    train_category_model.run(_Rows(_toy()), tmp_path / "m.joblib", recorder=rec)
    statuses = sorted(r[0] for r in _q(connect, "SELECT status FROM bill_model_versions"))
    assert statuses == ["active", "retired"]


def test_synthetic_rows_from_the_database_train_but_are_never_measured(connect, tmp_path):
    rows = _toy() + _toy(10, source="synthetic_ocr")
    train_category_model.run(_Rows(rows), tmp_path / "m.joblib", recorder=TrainingRecorder(connect))
    params = json.loads(_q(connect, "SELECT params FROM bill_training_jobs")[0][0])
    assert params["real_rows"] == 30 and params["synthetic_rows"] == 20
    assert _q(connect, "SELECT sample_count FROM bill_evaluation_runs")[0][0] == 30  # số đo chỉ tính bill thật


def test_a_crashing_run_is_recorded_as_failed_and_still_raises(connect, tmp_path, monkeypatch):
    monkeypatch.setattr(train_category_model, "train", lambda *a, **k: (_ for _ in ()).throw(RuntimeError("boom")))
    with pytest.raises(RuntimeError):
        train_category_model.run(_Rows(_toy()), tmp_path / "m.joblib", recorder=TrainingRecorder(connect))
    status, error = _q(connect, "SELECT status, error_message FROM bill_training_jobs")[0]
    assert status == "failed" and "boom" in error
    assert _q(connect, "SELECT count(*) FROM bill_model_versions")[0][0] == 0


def test_without_a_recorder_training_works_exactly_as_before(tmp_path):
    assert train_category_model.run(_Rows(_toy()), tmp_path / "m.joblib") == 0
