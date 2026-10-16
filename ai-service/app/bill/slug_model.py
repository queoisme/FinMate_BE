"""Mô hình học nhóm của cả hóa đơn (TF-IDF + hồi quy logistic, như ``ocr-service/category_model.py``) từ nhãn do NGƯỜI DÙNG chốt.

Mô hình không bao giờ tự phục vụ: ``train`` chỉ tạo bản ``candidate`` khi qua cổng chặn; chỉ lệnh ``promote`` mới đổi sang ``active``.
Cổng: đủ mẫu / cửa hàng / nhóm, độ chính xác kiểm tra chéo THEO CỬA HÀNG đạt ngưỡng, và hơn luật hiện tại trên cùng mẫu (luật + bộ nhớ,
không tính Gemini vì không có nhãn Gemini nào đáng tin để so). Không log chữ hóa đơn, tên cửa hàng hay nhóm.
"""

from __future__ import annotations

import re
import uuid
from collections import Counter
from collections.abc import Callable, Sequence
from dataclasses import dataclass, field
from datetime import UTC, datetime
from pathlib import Path
from typing import Any, Protocol

import joblib
import numpy as np
from sklearn.feature_extraction.text import TfidfVectorizer
from sklearn.linear_model import LogisticRegression
from sklearn.model_selection import GroupKFold
from sklearn.pipeline import FeatureUnion, Pipeline
from sqlalchemy import select, update
from sqlalchemy.ext.asyncio import AsyncSession

from app.bill.classifier import _norm
from app.db.models.bill import BillModelVersion, BillTrainingJob

# Cổng tối thiểu để huấn luyện (kế hoạch learning-loop, Global Constraints). Là giả định chưa kiểm chứng; chỉnh khi có dữ liệu thật.
MIN_LABELED = 300
MIN_MERCHANTS = 30
MIN_STRONG_SLUGS = 3  # số danh mục đủ MIN_PER_SLUG mẫu
MIN_PER_SLUG = 20
MIN_CV_ACCURACY = 0.85
MIN_PROBABILITY = 0.8  # dưới ngưỡng này lúc phục vụ thì mô hình không trả lời
STAGE = "bill_slug"  # cột ``stage`` của bill_model_versions / bill_training_jobs


class Labeled(Protocol):
    merchant_key: str | None
    lines: list[str]
    final_slug: str


def sample_gate(rows: Sequence[Labeled]) -> list[str]:
    """Những gì còn thiếu để đủ dữ liệu huấn luyện (rỗng = đủ)."""
    per_slug = Counter(r.final_slug for r in rows)
    merchants = len({r.merchant_key for r in rows if r.merchant_key})
    missing: list[str] = []
    if len(rows) < MIN_LABELED:
        missing.append(f"cần thêm {MIN_LABELED - len(rows)} mẫu có nhãn (đang có {len(rows)}/{MIN_LABELED})")
    if merchants < MIN_MERCHANTS:
        missing.append(f"cần thêm {MIN_MERCHANTS - merchants} cửa hàng khác nhau (đang có {merchants}/{MIN_MERCHANTS})")
    strong = sum(1 for n in per_slug.values() if n >= MIN_PER_SLUG)
    if strong < MIN_STRONG_SLUGS:
        missing.append(f"cần thêm {MIN_STRONG_SLUGS - strong} danh mục có từ {MIN_PER_SLUG} mẫu trở lên (đang có {strong}/{MIN_STRONG_SLUGS})")
    return missing


def record_text(merchant_key: str | None, lines: Sequence[str]) -> str:
    """Văn bản cho mô hình: khóa cửa hàng + các dòng chữ, bỏ dấu, bỏ mọi chữ số (số tiền, mã chứng từ, số điện thoại...)."""
    raw = _norm(" ".join([merchant_key or "", *lines]))
    return re.sub(r"\s+", " ", re.sub(r"[^a-z\s]", " ", raw)).strip()


def _pipeline() -> Pipeline:
    # ký tự n-gram chịu được lỗi chính tả do OCR; từ n-gram bắt cụm từ như "nuoc mam"
    features = FeatureUnion([
        ("word", TfidfVectorizer(analyzer="word", ngram_range=(1, 2), sublinear_tf=True)),
        ("char", TfidfVectorizer(analyzer="char_wb", ngram_range=(3, 5), sublinear_tf=True)),
    ])
    return Pipeline([("tfidf", features), ("clf", LogisticRegression(C=10, max_iter=1000, class_weight="balanced"))])


@dataclass
class TrainResult:
    status: str  # candidate | rejected
    reason: str
    cv_accuracy: float | None = None
    rule_accuracy: float | None = None
    pipeline: Pipeline | None = None
    metrics: dict[str, Any] = field(default_factory=dict)


def _cv_predictions(texts: list[str], labels: list[str], groups: list[str]) -> list[tuple[str | None, float]]:
    """(nhóm đoán, xác suất) cho từng mẫu khi nó nằm ở tập kiểm tra; một cửa hàng không bao giờ vừa ở tập học vừa ở tập kiểm tra."""
    texts_arr, labels_arr = np.array(texts), np.array(labels)
    out: list[tuple[str | None, float]] = [(None, 0.0)] * len(texts)  # lượt bị bỏ (tập học chỉ có một nhóm) tính là đoán sai
    for train_idx, test_idx in GroupKFold(n_splits=min(5, len(set(groups)))).split(texts_arr, labels_arr, groups):
        if len(set(labels_arr[train_idx])) < 2:
            continue
        model = _pipeline().fit(list(texts_arr[train_idx]), list(labels_arr[train_idx]))
        for i, probs in zip(test_idx, model.predict_proba(list(texts_arr[test_idx]))):
            best = int(probs.argmax())
            out[i] = (str(model.classes_[best]), float(probs[best]))
    return out


def train(rows: Sequence[Labeled], rule_predict: Callable[[Labeled], str | None]) -> TrainResult:
    """``rule_predict``: nhóm mà luật hiện tại (không Gemini) đoán cho mẫu; dùng để bắt mô hình phải hơn luật."""
    if missing := sample_gate(rows):
        return TrainResult("rejected", "Chưa đủ dữ liệu: " + "; ".join(missing))

    texts = [record_text(r.merchant_key, r.lines) for r in rows]
    labels = [r.final_slug for r in rows]
    groups = [r.merchant_key or f"__row_{i}" for i, r in enumerate(rows)]  # mẫu không có cửa hàng tự thành một nhóm
    predictions = _cv_predictions(texts, labels, groups)

    n = len(rows)
    cv_accuracy = round(sum(p == y for (p, _), y in zip(predictions, labels)) / n, 3)
    rule_accuracy = round(sum(rule_predict(r) == r.final_slug for r in rows) / n, 3)
    confident = [(p, y) for (p, prob), y in zip(predictions, labels) if p is not None and prob >= MIN_PROBABILITY]
    metrics = {
        "samples": n,
        "classes": dict(Counter(labels)),
        "cv_accuracy": cv_accuracy,
        "rule_accuracy": rule_accuracy,
        "threshold": MIN_PROBABILITY,
        # số đo quan trọng nhất lúc phục vụ: mô hình chỉ trả lời khi xác suất >= ngưỡng
        "coverage_at_threshold": round(len(confident) / n, 3),
        "accuracy_at_threshold": round(sum(p == y for p, y in confident) / len(confident), 3) if confident else None,
    }
    if cv_accuracy < MIN_CV_ACCURACY:
        return TrainResult("rejected", f"Độ chính xác kiểm tra chéo {cv_accuracy:.0%} thấp hơn ngưỡng {MIN_CV_ACCURACY:.0%}", cv_accuracy, rule_accuracy, None, metrics)
    if cv_accuracy <= rule_accuracy:
        return TrainResult("rejected", f"Không hơn luật hiện tại (mô hình {cv_accuracy:.0%}, luật {rule_accuracy:.0%})", cv_accuracy, rule_accuracy, None, metrics)
    return TrainResult("candidate", "Đạt", cv_accuracy, rule_accuracy, _pipeline().fit(texts, labels), metrics)


async def record_training(session: AsyncSession, result: TrainResult, *, sample_count: int, registry_path: str) -> str:
    """Ghi một lần huấn luyện (kể cả bị loại, kèm lý do) và trả mã phiên bản. Bản ``candidate`` có tệp ``<registry>/bill_slug/<version>.joblib``
    để lệnh ``promote`` ở một tiến trình khác còn dùng được; không bao giờ đặt ``active``."""
    now = datetime.now(UTC)
    version = f"{now:%Y%m%d-%H%M%S}-{uuid.uuid4().hex[:4]}"
    artifact = ""
    if result.pipeline is not None:
        path = Path(registry_path) / STAGE / f"{version}.joblib"
        path.parent.mkdir(parents=True, exist_ok=True)
        joblib.dump(result.pipeline, path)
        artifact = str(path)
    job = BillTrainingJob(stage=STAGE, status="finished", params={"min_cv_accuracy": MIN_CV_ACCURACY}, sample_count=sample_count, started_at=now, finished_at=now)
    session.add(job)
    await session.flush()
    session.add(BillModelVersion(
        stage=STAGE, version=version, artifact_path=artifact, status=result.status, metrics=result.metrics, training_job_id=job.id,
        trained_at=now, notes=result.reason,
    ))
    await session.commit()
    return version


async def promote(session: AsyncSession, version: str) -> None:
    """Chỉ bản ``candidate`` được bật; bản đang ``active`` trước đó chuyển ``retired`` (một bản active duy nhất)."""
    row = (await session.execute(select(BillModelVersion).where(BillModelVersion.stage == STAGE, BillModelVersion.version == version))).scalar_one_or_none()
    if row is None or row.status != "candidate":
        raise ValueError("Chỉ bản candidate mới được promote")
    await session.execute(update(BillModelVersion).where(BillModelVersion.stage == STAGE, BillModelVersion.status == "active").values(status="retired"))
    row.status = "active"
    await session.commit()


async def retire(session: AsyncSession) -> int:
    """Quay lại luật: bản đang ``active`` (nếu có) chuyển ``retired``. Trả số bản đã nghỉ."""
    result = await session.execute(update(BillModelVersion).where(BillModelVersion.stage == STAGE, BillModelVersion.status == "active").values(status="retired"))
    await session.commit()
    return result.rowcount
