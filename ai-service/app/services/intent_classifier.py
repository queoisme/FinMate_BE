"""Bộ phân loại ý định của câu chat (thay máy chủ Rasa): TF-IDF ký tự + từ, hồi quy logistic, huấn luyện lúc khởi động từ ``data/intent/examples.jsonl``.

Cùng giao diện với ``RasaNlu`` cũ: ``parse(text)`` trả ``{"intent", "confidence", "entities"}`` hoặc None. ``entities`` luôn rỗng: số tiền và danh mục do luật đọc.
Dữ liệu là một tệp văn bản có phiên bản (không có bảng DB, không có tệp model nhị phân): thêm câu mẫu vào tệp rồi khởi động lại là có hiệu lực, và
``scripts/eval_intent.py`` đo bản đang chạy. Câu có và không có dấu cùng được nhìn thấy khi huấn luyện, vì người dùng gõ cả hai kiểu.
"""
from __future__ import annotations

import json
from pathlib import Path

import structlog
from sklearn.feature_extraction.text import TfidfVectorizer
from sklearn.linear_model import LogisticRegression
from sklearn.pipeline import make_pipeline, make_union

from app.services.amount_normalizer import normalize, parse_amount
from app.services.vi_text import fold

logger = structlog.get_logger(__name__)

DEFAULT_PATH = Path(__file__).resolve().parents[2] / "data" / "intent" / "examples.jsonl"


def load_examples(path: Path | str = DEFAULT_PATH, *, splits: tuple[str, ...] | None = None) -> list[tuple[str, str]]:
    """[(text, intent)] của các dòng thuộc ``splits`` (None = tất cả)."""
    rows = [json.loads(line) for line in Path(path).read_text(encoding="utf-8").splitlines() if line.strip()]
    return [(r["text"], r["intent"]) for r in rows if splits is None or r["split"] in splits]


def _features(text: str) -> str:
    """Chữ đã chuẩn hóa cộng một dấu hiệu CÓ/KHÔNG có số tiền: TF-IDF không tự thấy "66k" là một khoản tiền, mà câu có số tiền hầu như không bao giờ là câu lạc đề."""
    return f"{normalize(text)} {'sotien_co' if parse_amount(text) else 'sotien_khong'}"


def _augment(examples: list[tuple[str, str]]) -> list[tuple[str, str]]:
    """Thêm bản bỏ dấu của mỗi câu có dấu (cùng nhãn): câu không dấu là cách gõ rất thường gặp."""
    seen = {text for text, _ in examples}
    extra = [(fold(text), intent) for text, intent in examples if fold(text) != text.lower() and fold(text) not in seen]
    return examples + extra


class IntentClassifier:
    def __init__(self, pipeline) -> None:
        self._pipeline = pipeline
        self._classes = list(pipeline.classes_)

    @classmethod
    def train(cls, examples: list[tuple[str, str]], *, c: float = 20.0, class_weight: str | None = None) -> "IntentClassifier":
        data = _augment(examples)
        features = make_union(
            TfidfVectorizer(analyzer="char_wb", ngram_range=(2, 5), sublinear_tf=True, min_df=2),
            TfidfVectorizer(analyzer="word", ngram_range=(1, 2), sublinear_tf=True),
        )
        pipeline = make_pipeline(features, LogisticRegression(C=c, max_iter=3000, class_weight=class_weight))
        pipeline.fit([_features(t) for t, _ in data], [i for _, i in data])
        return cls(pipeline)

    def predict(self, text: str) -> tuple[str, float]:
        probabilities = self._pipeline.predict_proba([_features(text)])[0]
        index = int(probabilities.argmax())
        return str(self._classes[index]), float(probabilities[index])

    async def parse(self, text: str) -> dict | None:
        if not text.strip():
            return None
        intent, confidence = self.predict(text)
        return {"intent": intent, "confidence": confidence, "entities": {}}


_shared: IntentClassifier | None = None
_shared_ready = False


def shared_classifier() -> IntentClassifier | None:
    """Bộ phân loại dùng chung của tiến trình: huấn luyện một lần (vài giây) ở lần gọi đầu rồi giữ trong bộ nhớ.
    Thiếu tệp ví dụ hoặc lỗi thì None để chat vẫn chạy bằng luật."""
    global _shared, _shared_ready
    if not _shared_ready:
        try:
            _shared = IntentClassifier.train(load_examples())
            logger.info("intent_classifier_ready")
        except (OSError, ValueError, KeyError) as error:
            logger.warning("intent_classifier_unavailable", error_type=type(error).__name__)
        _shared_ready = True
    return _shared
