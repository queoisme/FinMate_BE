"""Ghi mỗi lần huấn luyện mô hình danh mục bill vào các bảng có sẵn của finmate_ai:
bill_training_jobs (lần chạy), bill_model_versions (phiên bản + số đo), bill_evaluation_runs + bill_evaluation_category_metrics (đo theo từng danh mục).

Ghi cả lần không đạt ngưỡng hay thiếu dữ liệu (status 'rejected'): có lịch sử để biết khi nào mô hình thật sự tốt lên.
Mô hình chỉ thành 'active' khi qua cổng chất lượng (category_model.MIN_CV_ACCURACY); bản active cũ chuyển 'retired' (quay lại được).
Cần: FINMATE_DATABASE_URL. Bảng đã có từ migration của bạn nên không cần migration mới.
"""
import json
import uuid
from datetime import datetime, timezone

from category_model import MIN_CV_ACCURACY, MIN_RECORDS, TrainReport
from storage_pg import _cursor, connect_from_env

STAGE = "bill_category"
# ponytail: đối chiếu các giá trị status với quy ước của pipeline thông báo ngân hàng
JOB_RUNNING, JOB_OK, JOB_FAILED = "running", "succeeded", "failed"
MODEL_ACTIVE, MODEL_REJECTED, MODEL_RETIRED = "active", "rejected", "retired"
EVAL_SPLIT = "cv"  # kiểm tra chéo nhóm theo cửa hàng trên bill thật (chưa có tập test riêng)


def _now() -> str:
    return datetime.now(timezone.utc).isoformat()


def new_version() -> str:
    return datetime.now(timezone.utc).strftime("%Y%m%d-%H%M%S")


class TrainingRecorder:
    def __init__(self, connect, stage: str = STAGE):
        self._connect, self._stage = connect, stage

    def start(self, params: dict) -> str:
        job_id, now = str(uuid.uuid4()), _now()
        with _cursor(self._connect) as cur:
            cur.execute("INSERT INTO bill_training_jobs (id, stage, status, params, started_at, created_at) VALUES (%s, %s, %s, %s, %s, %s)",
                        (job_id, self._stage, JOB_RUNNING, json.dumps(params, ensure_ascii=False), now, now))
        return job_id

    def fail(self, job_id: str, error: str) -> None:
        with _cursor(self._connect) as cur:
            cur.execute("UPDATE bill_training_jobs SET status = %s, error_message = %s, finished_at = %s WHERE id = %s",
                        (JOB_FAILED, error[:2000], _now(), job_id))

    def finish(self, job_id: str, report: TrainReport, version: str, artifact_path: str) -> str:
        """Ghi kết quả một lần huấn luyện đã chạy xong; trả về id phiên bản model."""
        now, model_id = _now(), str(uuid.uuid4())
        active = report.trained
        metrics = {"cv_accuracy": report.cv_accuracy, "macro_f1": report.macro_f1, "classes": report.classes, "reason": report.reason,
                   "gate": {"min_cv_accuracy": MIN_CV_ACCURACY, "min_records": MIN_RECORDS}}
        with _cursor(self._connect) as cur:
            cur.execute("UPDATE bill_training_jobs SET status = %s, sample_count = %s, finished_at = %s WHERE id = %s",
                        (JOB_OK, report.n_records, now, job_id))
            if active:  # bản active cũ chuyển 'retired' để chỉ có một bản đang chạy
                cur.execute("UPDATE bill_model_versions SET status = %s, updated_at = %s WHERE stage = %s AND status = %s",
                            (MODEL_RETIRED, now, self._stage, MODEL_ACTIVE))
            cur.execute(
                "INSERT INTO bill_model_versions (id, stage, version, artifact_path, status, metrics, training_job_id, trained_at, notes, created_at, updated_at) "
                "VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s)",
                (model_id, self._stage, version, artifact_path if active else "", MODEL_ACTIVE if active else MODEL_REJECTED,
                 json.dumps(metrics, ensure_ascii=False), job_id, now, report.reason, now, now))
            if report.cv_accuracy is not None:  # có số đo thì ghi cả bảng đánh giá và đo theo từng danh mục
                run_id = str(uuid.uuid4())
                cur.execute(
                    "INSERT INTO bill_evaluation_runs (id, model_version_id, split, sample_count, accuracy, macro_f1, metrics, notes, created_at) "
                    "VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s)",
                    (run_id, model_id, EVAL_SPLIT, report.n_records, report.cv_accuracy, report.macro_f1 or 0.0,
                     json.dumps({"classes": report.classes}, ensure_ascii=False), report.reason, now))
                for label, (precision, recall, f1, support) in report.per_class.items():
                    cur.execute('INSERT INTO bill_evaluation_category_metrics (id, evaluation_run_id, label, "precision", recall, f1, support) '
                                "VALUES (%s, %s, %s, %s, %s, %s, %s)", (str(uuid.uuid4()), run_id, label, precision, recall, f1, support))
        return model_id


def open_training_recorder() -> TrainingRecorder | None:
    connect = connect_from_env()
    return TrainingRecorder(connect) if connect else None
