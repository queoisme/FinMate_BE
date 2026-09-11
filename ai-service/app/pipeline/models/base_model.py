"""Giao diện chung cho model của một stage.

Pipeline chỉ biết tới ``StageModel``, không biết bên dưới là scikit-learn hay gì khác.
Đây là chỗ để đổi TF-IDF sang PhoBERT sau này: viết một lớp mới hiện thực đúng protocol
này và ghi artifact tương ứng, không stage nào phải sửa.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Protocol, runtime_checkable


@runtime_checkable
class StageModel(Protocol):
    version: str

    def predict(self, text: str) -> tuple[str, float]:
        """Trả (nhãn, độ tin cậy 0..1)."""


@dataclass
class SklearnStageModel:
    """Bọc một ``sklearn.pipeline.Pipeline`` đã fit (TF-IDF → LogisticRegression)."""

    version: str
    pipeline: Any

    def predict(self, text: str) -> tuple[str, float]:
        probabilities = self.pipeline.predict_proba([text])[0]
        classes = self.pipeline.classes_
        best = int(probabilities.argmax())
        return str(classes[best]), float(probabilities[best])
