"""Mô hình phân loại danh mục học từ dataset.jsonl (TF-IDF + hồi quy logistic).

Chỉ được dùng khi qua cổng chặn: đủ dữ liệu và độ chính xác kiểm tra chéo đạt ngưỡng.
"""
import re
from dataclasses import dataclass, field
from pathlib import Path

import joblib
import numpy as np
from sklearn.linear_model import LogisticRegression
from sklearn.metrics import precision_recall_fscore_support
from sklearn.model_selection import GroupKFold
from sklearn.pipeline import FeatureUnion, Pipeline
from sklearn.feature_extraction.text import TfidfVectorizer

from parsing.extract import _alnum, _norm

MIN_RECORDS = 30        # ít hơn thì chưa đủ dữ liệu để tin
MIN_CV_ACCURACY = 0.8   # độ chính xác kiểm tra chéo (nhóm theo cửa hàng) tối thiểu để bật mô hình
_MIN_GROUPS = 5         # cần ít nhất 5 cửa hàng khác nhau để chia nhóm kiểm tra


@dataclass
class TrainReport:
    trained: bool
    reason: str
    n_records: int = 0
    classes: dict[str, int] = field(default_factory=dict)
    cv_accuracy: float | None = None
    model: Pipeline | None = None
    macro_f1: float | None = None
    per_class: dict[str, tuple[float, float, float, int]] = field(default_factory=dict)  # danh mục -> (precision, recall, f1, số mẫu)


def _group_key(row: dict) -> str:
    """Khóa nhóm khi đo: cùng một cửa hàng dù viết "Bách Hóa Xanh", "bach hoa xanh" hay "bachhoaxanh" (bỏ dấu, khoảng trắng, dấu câu),
    để một cửa hàng không nằm vừa ở phần học vừa ở phần đo."""
    return _alnum(_norm(row.get("merchant") or ""))


def record_text(row: dict) -> str:
    """Văn bản cho mô hình: tên cửa hàng + các dòng chữ, bỏ dấu, bỏ mọi chữ số (số tiền, mã chứng từ...)."""
    raw = " ".join([row.get("merchant") or ""] + list(row.get("lines") or []))
    return re.sub(r"\s+", " ", re.sub(r"[^a-z\s]", " ", _norm(raw))).strip()


def _pipeline() -> Pipeline:
    # ký tự n-gram chịu được lỗi chính tả do OCR; từ n-gram bắt cụm từ như "nuoc mam"
    features = FeatureUnion([
        ("word", TfidfVectorizer(analyzer="word", ngram_range=(1, 2), sublinear_tf=True)),
        ("char", TfidfVectorizer(analyzer="char_wb", ngram_range=(3, 5), sublinear_tf=True)),
    ])
    return Pipeline([
        ("tfidf", features),
        ("clf", LogisticRegression(C=10, max_iter=1000, class_weight="balanced")),
    ])


_SKIPPED = "__skipped__"  # lượt kiểm tra bị bỏ vì tập huấn luyện chỉ có một danh mục: tính là đoán sai


def _grouped_cv_predictions(texts: list[str], labels: list[str], groups: list[str],
                            extra_texts: list[str] = (), extra_labels: list[str] = ()) -> list[str]:
    """Dự đoán cho từng mẫu khi nó nằm ở tập kiểm tra; một cửa hàng không bao giờ vừa ở tập huấn luyện vừa ở tập kiểm tra.

    extra_*: dữ liệu giả, chỉ được thêm vào tập huấn luyện của mỗi lượt; tập kiểm tra luôn là hóa đơn thật."""
    labels_arr, texts_arr = np.array(labels), np.array(texts)
    preds = [_SKIPPED] * len(labels)
    for train_idx, test_idx in GroupKFold(n_splits=min(5, len(set(groups)))).split(texts_arr, labels_arr, groups):
        if len(set(labels_arr[train_idx])) < 2:
            continue
        model = _pipeline().fit(list(texts_arr[train_idx]) + list(extra_texts), list(labels_arr[train_idx]) + list(extra_labels))
        for i, p in zip(test_idx, model.predict(texts_arr[test_idx])):
            preds[i] = str(p)
    return preds


def train(rows: list[dict], min_records: int = MIN_RECORDS, synthetic: list[dict] = ()) -> TrainReport:
    """rows: hóa đơn thật (dùng để đo và qua cổng). synthetic: hóa đơn giả, chỉ thêm vào phần huấn luyện."""
    classes: dict[str, int] = {}
    for r in rows:
        classes[r["category"]] = classes.get(r["category"], 0) + 1
    n = len(rows)
    if n < min_records:
        return TrainReport(False, f"Cần ít nhất {min_records} hóa đơn có nhãn, hiện có {n}", n, classes)
    groups = [_group_key(r) for r in rows]
    if len(set(groups)) < _MIN_GROUPS or len(classes) < 2:
        return TrainReport(False, "Cần ít nhất 5 cửa hàng khác nhau và 2 danh mục để đo độ chính xác", n, classes)
    texts, labels = [record_text(r) for r in rows], [r["category"] for r in rows]
    extra_texts, extra_labels = [record_text(r) for r in synthetic], [r["category"] for r in synthetic]
    preds = _grouped_cv_predictions(texts, labels, groups, extra_texts, extra_labels)
    accuracy = round(sum(p == y for p, y in zip(preds, labels)) / n, 3)
    present = sorted(classes)
    precision, recall, f1, support = precision_recall_fscore_support(labels, preds, labels=present, zero_division=0)
    per_class = {c: (round(float(p), 3), round(float(r), 3), round(float(f), 3), int(sp)) for c, p, r, f, sp in zip(present, precision, recall, f1, support)}
    macro_f1 = round(float(np.mean(f1)), 3)
    if accuracy < MIN_CV_ACCURACY:
        return TrainReport(
            False, f"Độ chính xác kiểm tra chéo {accuracy:.0%} thấp hơn ngưỡng {MIN_CV_ACCURACY:.0%}", n, classes, accuracy,
            macro_f1=macro_f1, per_class=per_class
        )
    return TrainReport(True, "Đạt", n, classes, accuracy, _pipeline().fit(texts + extra_texts, labels + extra_labels), macro_f1, per_class)


class CategoryModel:
    def __init__(self, pipeline: Pipeline):
        self._pipeline = pipeline

    def predict(self, merchant: str | None, lines: list[str]) -> tuple[str, float]:
        text = record_text({"merchant": merchant, "lines": lines})
        probs = self._pipeline.predict_proba([text])[0]
        best = int(probs.argmax())
        return str(self._pipeline.classes_[best]), float(probs[best])

    def save(self, path: Path) -> None:
        joblib.dump(self._pipeline, path)

    @classmethod
    def load(cls, path: Path) -> "CategoryModel | None":
        try:
            return cls(joblib.load(path))
        except (OSError, EOFError, ValueError):
            return None  # chưa có hoặc hỏng: bỏ qua, hệ thống vẫn chạy bằng luật
