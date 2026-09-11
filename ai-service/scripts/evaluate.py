#!/usr/bin/env python3
"""Chấm điểm một ``model_version`` trên split ``test`` và ghi kết quả vào AI DB.

Ghi cả từng dự đoán vào ``evaluation_predictions``, không chỉ điểm tổng: accuracy 94%
không cho biết nó hỏng ở "housing" hay ở "other", mà đó mới là thứ quyết định có nên
promote hay không.

Chạy:
    python scripts/evaluate.py --stage classifier --version 1.0.0
    python scripts/evaluate.py --stage categorizer --version 1.0.0 --split val
"""

from __future__ import annotations

import argparse
import asyncio
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))

import joblib
from sklearn.metrics import (
    accuracy_score,
    f1_score,
    precision_recall_fscore_support,
)

from app.core.config import settings
from app.core.enums import ModelStage, SplitName
from app.db.models.evaluation import (
    EvaluationCategoryMetric,
    EvaluationPrediction,
    EvaluationRun,
)
from app.db.repositories import model_repo, sample_repo
from app.db.session import AsyncSessionLocal

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from train import to_features


async def evaluate(stage: ModelStage, version: str, split: SplitName) -> None:
    async with AsyncSessionLocal() as session:
        model_version = await model_repo.get_by_stage_version(session, stage, version)
        if model_version is None:
            raise SystemExit(f"Không có {stage.value} version {version} trong AI DB")

        artifact = (
            pathlib.Path(settings.model_registry_path) / model_version.artifact_path
        )
        if not artifact.exists():
            raise SystemExit(f"Thiếu artifact: {artifact}")
        estimator = joblib.load(artifact)

        rows = await sample_repo.load_split(session, split)
        items = [
            (label.id, pair)
            for raw, label in rows
            if (pair := to_features(raw, label, stage))
        ]
        if not items:
            raise SystemExit(
                f"Split {split.value} không có mẫu nào hợp lệ cho {stage.value}"
            )

        texts = [pair[0] for _, pair in items]
        truths = [pair[1] for _, pair in items]
        predictions = list(estimator.predict(texts))
        probabilities = estimator.predict_proba(texts)
        classes = list(estimator.classes_)

        accuracy = float(accuracy_score(truths, predictions))
        macro_f1 = float(
            f1_score(truths, predictions, average="macro", zero_division=0)
        )

        run = EvaluationRun(
            model_version_id=model_version.id,
            split=split.value,
            sample_count=len(items),
            accuracy=accuracy,
            macro_f1=macro_f1,
            metrics={"model_version": version, "stage": stage.value},
        )
        session.add(run)
        await session.flush()

        labels = sorted(set(truths) | set(predictions))
        precision, recall, f1, support = precision_recall_fscore_support(
            truths, predictions, labels=labels, zero_division=0
        )
        for index, label in enumerate(labels):
            session.add(
                EvaluationCategoryMetric(
                    evaluation_run_id=run.id,
                    label=label,
                    precision=float(precision[index]),
                    recall=float(recall[index]),
                    f1=float(f1[index]),
                    support=int(support[index]),
                )
            )

        for (labeled_sample_id, _), predicted, truth, row_probabilities in zip(
            items, predictions, truths, probabilities, strict=True
        ):
            confidence = float(row_probabilities[classes.index(predicted)])
            session.add(
                EvaluationPrediction(
                    evaluation_run_id=run.id,
                    labeled_sample_id=labeled_sample_id,
                    predicted_label=str(predicted),
                    true_label=truth,
                    confidence=confidence,
                    is_correct=predicted == truth,
                )
            )

        await session.commit()
        run_id = run.id

    print(f"{stage.value} {version} trên split {split.value}: {len(items)} mẫu")
    print(f"  accuracy  = {accuracy:.3f}")
    print(f"  macro F1  = {macro_f1:.3f}")
    print(f"  run id    = {run_id}")
    for index, label in enumerate(labels):
        print(
            f"    {label:<16} P={precision[index]:.2f} R={recall[index]:.2f} "
            f"F1={f1[index]:.2f} n={int(support[index])}"
        )


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--stage", required=True, choices=[s.value for s in ModelStage])
    parser.add_argument("--version", required=True)
    parser.add_argument(
        "--split", default=SplitName.TEST.value, choices=[s.value for s in SplitName]
    )
    args = parser.parse_args()
    asyncio.run(evaluate(ModelStage(args.stage), args.version, SplitName(args.split)))


if __name__ == "__main__":
    main()
