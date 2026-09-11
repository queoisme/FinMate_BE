#!/usr/bin/env python3
"""Train Classifier hoặc Categorizer từ ``labeled_samples`` (split ``train``).

Model tạo ra ở trạng thái ``candidate`` — CHƯA phục vụ request nào. Phải chấm điểm bằng
``scripts/evaluate.py`` rồi mới ``scripts/promote.py``. Tách ba bước là có chủ ý: train
xong mà tự động phục vụ luôn thì một lần train trên dữ liệu hỏng sẽ ra thẳng người dùng.

Chạy:
    python scripts/train.py --stage classifier
    python scripts/train.py --stage categorizer
"""

from __future__ import annotations

import argparse
import asyncio
import pathlib
import sys
from datetime import UTC, datetime

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))

import joblib
from sklearn.feature_extraction.text import TfidfVectorizer
from sklearn.linear_model import LogisticRegression
from sklearn.pipeline import Pipeline

from app.core.config import settings
from app.core.enums import JobStatus, ModelStage, ModelStatus, SplitName
from app.db.models.model_version import ModelVersion, TrainingJob
from app.db.repositories import model_repo, sample_repo
from app.db.session import AsyncSessionLocal
from app.utils.text_utils import normalize_for_model


def build_estimator() -> Pipeline:
    """TF-IDF trên n-gram KÝ TỰ, không phải từ.

    Tiếng Việt trong thông báo ngân hàng xuất hiện cả có dấu lẫn không dấu ("thanh toan"
    / "thanh toán"), viết hoa toàn phần, và dính liền ký hiệu. N-gram ký tự bắt được họ
    hàng giữa các biến thể đó mà không cần tách từ — nên cũng không cần underthesea ở
    đường phục vụ.
    """
    return Pipeline(
        [
            (
                "tfidf",
                TfidfVectorizer(
                    analyzer="char_wb",
                    ngram_range=(2, 5),
                    min_df=2,
                    sublinear_tf=True,
                    max_features=50_000,
                ),
            ),
            (
                "clf",
                LogisticRegression(
                    max_iter=1000,
                    # Nhãn lệch nhau nhiều (chi tiêu áp đảo thu nhập, "food" áp đảo
                    # "housing"); không cân lại thì model học cách luôn đoán lớp phổ biến.
                    class_weight="balanced",
                ),
            ),
        ]
    )


def to_features(raw, label, stage: ModelStage) -> tuple[str, str] | None:
    text = " ".join(
        part for part in (raw.notification_title, raw.notification_body) if part
    )
    if stage == ModelStage.CLASSIFIER:
        return normalize_for_model(text), (
            "financial" if label.is_financial else "non_financial"
        )

    # Categorizer chỉ học trên giao dịch CHI: mọi giao dịch thu đều là "income" theo luật,
    # đưa vào tập train chỉ dạy model một lớp mà nó không bao giờ phải quyết định.
    if not label.is_financial or label.transaction_type != "debit":
        return None
    if not label.category_slug:
        return None
    merchant = label.merchant_name or ""
    return normalize_for_model(f"{merchant} {text}"), label.category_slug


def next_version(existing: list[str]) -> str:
    if not existing:
        return "1.0.0"
    parsed = sorted(tuple(int(p) for p in v.split(".")) for v in existing)
    major, minor, patch = parsed[-1]
    return f"{major}.{minor}.{patch + 1}"


async def train(stage: ModelStage, version: str | None) -> None:
    started = datetime.now(UTC)

    async with AsyncSessionLocal() as session:
        rows = await sample_repo.load_split(session, SplitName.TRAIN)
        pairs = [
            pair for raw, label in rows if (pair := to_features(raw, label, stage))
        ]
        if len(pairs) < 10:
            raise SystemExit(
                f"Chỉ có {len(pairs)} mẫu train cho {stage.value} — quá ít để train. "
                "Chạy scripts/seed_dataset.py trước."
            )

        texts = [text for text, _ in pairs]
        labels = [label for _, label in pairs]
        distinct = sorted(set(labels))
        if len(distinct) < 2:
            raise SystemExit(
                f"Tập train chỉ có một nhãn ({distinct[0]}) — không train được."
            )

        job = await model_repo.add_training_job(
            session,
            TrainingJob(
                stage=stage.value,
                status=JobStatus.RUNNING.value,
                params={"estimator": "tfidf-char_wb-2_5+logreg", "labels": distinct},
                sample_count=len(pairs),
                started_at=started,
            ),
        )
        await session.commit()

        try:
            estimator = build_estimator()
            estimator.fit(texts, labels)
        except Exception as error:
            job.status = JobStatus.FAILED.value
            job.error_message = str(error)
            job.finished_at = datetime.now(UTC)
            await session.commit()
            raise

        existing = [
            row.version
            for row in (await model_repo.list_active(session))
            if row.stage == stage.value
        ]
        all_versions = existing + await _all_versions(session, stage)
        resolved = version or next_version(sorted(set(all_versions)))

        relative = f"{stage.value}/{resolved}/model.joblib"
        target = pathlib.Path(settings.model_registry_path) / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        joblib.dump(estimator, target)

        job.status = JobStatus.SUCCEEDED.value
        job.finished_at = datetime.now(UTC)

        await model_repo.add_version(
            session,
            ModelVersion(
                stage=stage.value,
                version=resolved,
                artifact_path=relative,
                status=ModelStatus.CANDIDATE.value,
                metrics={"train_samples": len(pairs), "labels": distinct},
                training_job_id=job.id,
                trained_at=datetime.now(UTC),
            ),
        )
        await session.commit()

    print(
        f"{stage.value} {resolved}: train trên {len(pairs)} mẫu, {len(distinct)} nhãn"
    )
    print(f"  artifact: {target}")
    print("  trạng thái: candidate — chạy evaluate.py rồi promote.py")


async def _all_versions(session, stage: ModelStage) -> list[str]:
    from sqlalchemy import select

    result = await session.execute(
        select(ModelVersion.version).where(ModelVersion.stage == stage.value)
    )
    return [row[0] for row in result.all()]


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--stage", required=True, choices=[s.value for s in ModelStage])
    parser.add_argument("--version", default=None, help="mặc định: tăng patch")
    args = parser.parse_args()
    asyncio.run(train(ModelStage(args.stage), args.version))


if __name__ == "__main__":
    main()
