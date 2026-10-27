"""Ghi mỗi lần huấn luyện mô hình danh mục bill vào lịch sử của AI Service (model có ``code = 'bill_category'`` trong ``ml_models``): lần chạy, phiên bản + số đo, đo theo từng danh mục.
(Bản trước kết nối thẳng PostgreSQL: ``training_pg.py``.)

Ghi cả lần không đạt ngưỡng hay thiếu dữ liệu (status 'rejected'): có lịch sử để biết khi nào mô hình thật sự tốt lên.
Mô hình chỉ thành 'active' khi qua cổng chất lượng (category_model.MIN_CV_ACCURACY); bản active cũ chuyển 'retired' (quay lại được).
Cần: AI_SERVICE_URL + AI_SERVICE_API_KEY.
"""
from datetime import datetime, timezone

from classification.category_model import MIN_CV_ACCURACY, MIN_RECORDS, TrainReport
from storage.ai_api import AiApi, api_from_env

STAGE = "bill_category"


def new_version() -> str:
    return datetime.now(timezone.utc).strftime("%Y%m%d-%H%M%S")


class TrainingRecorder:
    def __init__(self, api: AiApi, stage: str = STAGE):
        self._api, self._stage = api, stage

    def start(self, params: dict) -> str:
        return self._api.start_training(self._stage, params)

    def fail(self, job_id: str, error: str) -> None:
        self._api.fail_training(job_id, error)

    def finish(self, job_id: str, report: TrainReport, version: str, artifact_path: str) -> str:
        """Ghi kết quả một lần huấn luyện đã chạy xong; trả về id phiên bản model."""
        return self._api.finish_training(job_id, {
            "stage": self._stage, "version": version, "artifact_path": artifact_path if report.trained else "", "trained": report.trained,
            "n_records": report.n_records, "cv_accuracy": report.cv_accuracy, "macro_f1": report.macro_f1, "classes": report.classes, "reason": report.reason,
            "per_class": {label: list(values) for label, values in report.per_class.items()},
            "min_cv_accuracy": MIN_CV_ACCURACY, "min_records": MIN_RECORDS,
        })


def open_training_recorder() -> TrainingRecorder | None:
    api = api_from_env()
    return TrainingRecorder(api) if api else None
