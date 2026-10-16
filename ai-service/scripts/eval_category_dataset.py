#!/usr/bin/env python3
"""Đo mô hình nhóm hóa đơn trên bộ ``category-classifier/*.csv`` (chỉ để BÁO CÁO, không bật mô hình nào).

Bộ này phần lớn là ``synthetic_ocr`` (chữ hóa đơn sinh tự động); nhãn thật (``user``/``gold``/``imported``) rất ít. Vì vậy mọi con số được tách
riêng hai nhóm, và có thí nghiệm "học trên dữ liệu giả, đo trên dữ liệu thật" cho thấy khoảng cách giữa hai thế giới.
Chạy: ``python scripts/eval_category_dataset.py``  (cần các tệp CSV, sinh bằng ``scripts/export_category_dataset.py``).
"""

from __future__ import annotations

import csv
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))

from app.bill.slug_model import MIN_PROBABILITY, _cv_predictions, _pipeline, record_text
from learning_report import SampleRow, rule_predict

DATA_DIR = pathlib.Path(__file__).resolve().parent.parent / "category-classifier"
SYNTHETIC = "synthetic_ocr"


def load(name: str) -> list[dict]:
    with (DATA_DIR / f"{name}.csv").open(encoding="utf-8", newline="") as handle:
        return list(csv.DictReader(handle))


def _text(row: dict) -> str:
    return record_text(None, [row["text"]])  # CSV đã gộp tên cửa hàng vào chữ hóa đơn


def _rule(row: dict) -> str | None:
    return rule_predict(SampleRow(row["merchant_key"] or None, row["text"].split("\n"), row["label"], None, None))


def evaluate(train_rows: list[dict], eval_rows: list[dict]) -> dict:
    """Học trên ``train_rows``, đo trên ``eval_rows``: độ đúng của mô hình, của luật, và của mô hình khi chỉ trả lời lúc xác suất >= ngưỡng."""
    if not train_rows or not eval_rows or len({r["label"] for r in train_rows}) < 2:
        return {"n": len(eval_rows), "note": "không đủ dữ liệu để học hoặc đo"}
    model = _pipeline().fit([_text(r) for r in train_rows], [r["label"] for r in train_rows])
    probs = model.predict_proba([_text(r) for r in eval_rows])
    guesses = [(str(model.classes_[int(p.argmax())]), float(p.max())) for p in probs]
    n = len(eval_rows)
    confident = [(g, r["label"]) for (g, p), r in zip(guesses, eval_rows) if p >= MIN_PROBABILITY]
    return {
        "n": n,
        "model": round(sum(g == r["label"] for (g, _), r in zip(guesses, eval_rows)) / n, 3),
        "rules": round(sum(_rule(r) == r["label"] for r in eval_rows) / n, 3),
        "coverage_at_threshold": round(len(confident) / n, 3),
        "accuracy_at_threshold": round(sum(g == y for g, y in confident) / len(confident), 3) if confident else None,
    }


def real_only(rows: list[dict]) -> list[dict]:
    return [r for r in rows if r["labeled_by"] != SYNTHETIC]


def synthetic_only(rows: list[dict]) -> list[dict]:
    return [r for r in rows if r["labeled_by"] == SYNTHETIC]


def real_grouped_cv(rows: list[dict]) -> dict:
    """Chỉ nhãn thật, CV theo cửa hàng: nhỏ nên chỉ là chỉ báo thô."""
    real = real_only(rows)
    if len({r["merchant_key"] for r in real}) < 5:
        return {"n": len(real), "note": "dưới 5 cửa hàng, không chia nhóm được"}
    groups = [r["merchant_key"] or f"__row_{i}" for i, r in enumerate(real)]
    predictions = _cv_predictions([_text(r) for r in real], [r["label"] for r in real], groups)
    return {"n": len(real), "model_cv": round(sum(p == r["label"] for (p, _), r in zip(predictions, real)) / len(real), 3)}


def main() -> int:
    train, validation, test = load("train"), load("validation"), load("test")
    held_out = validation + test
    print(f"train {len(train)} (thật {len(real_only(train))}), validation+test {len(held_out)} (thật {len(real_only(held_out))})")
    print("\n[A] học trên toàn bộ train, đo trên validation+test")
    print("   tất cả      ", evaluate(train, held_out))
    print("   tổng hợp    ", evaluate(train, synthetic_only(held_out)))
    print("   dữ liệu THẬT", evaluate(train, real_only(held_out)))
    print("\n[B] học CHỈ trên dữ liệu tổng hợp của train, đo trên dữ liệu thật của validation+test")
    print("   ", evaluate(synthetic_only(train), real_only(held_out)))
    print("\n[C] chỉ nhãn thật (cả ba tệp), kiểm tra chéo theo cửa hàng")
    print("   ", real_grouped_cv(train + held_out))
    return 0


if __name__ == "__main__":
    sys.exit(main())
