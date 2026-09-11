#!/usr/bin/env python3
"""Nạp ``data/corpus/*.jsonl`` vào ``raw_samples`` + ``labeled_samples`` + ``sample_splits``.

Idempotent: chạy lại không nhân đôi mẫu (khoá là ``content_hash``) và không đổi tập
train/val/test của mẫu cũ.

Chạy: ``python scripts/seed_dataset.py [--corpus-dir data/corpus]``
"""

from __future__ import annotations

import argparse
import asyncio
import json
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))

from app.core.anonymizer import anonymize_body, content_hash
from app.core.enums import LabelSource, SampleSource, SplitName
from app.db.models.labeled_sample import LabeledSample, SampleSplit
from app.db.models.raw_sample import RawSample
from app.db.repositories import sample_repo
from app.db.session import AsyncSessionLocal

# 70 / 15 / 15. Tỉ lệ nằm ở đây một lần, không rải trong code.
TRAIN_CUTOFF = 70
VAL_CUTOFF = 85


def split_for(sample_content_hash: str) -> tuple[SplitName, int]:
    """Gán tập bằng hash TẤT ĐỊNH của nội dung, không dùng random.

    Random mỗi lần chạy làm một mẫu trôi từ test sang train giữa hai lần train; điểm đánh
    giá sẽ đẹp lên vì rò rỉ dữ liệu chứ không phải vì model tốt hơn, và không ai nhìn ra.
    """
    bucket = int(sample_content_hash[:8], 16) % 100
    if bucket < TRAIN_CUTOFF:
        return SplitName.TRAIN, bucket
    if bucket < VAL_CUTOFF:
        return SplitName.VAL, bucket
    return SplitName.TEST, bucket


async def seed(corpus_dir: pathlib.Path) -> None:
    files = sorted(corpus_dir.glob("*.jsonl"))
    if not files:
        raise SystemExit(f"Không tìm thấy file corpus nào trong {corpus_dir}")

    added_raw = added_label = 0
    counts: dict[str, int] = {}

    async with AsyncSessionLocal() as session:
        for path in files:
            for line in path.read_text(encoding="utf-8").splitlines():
                if not line.strip():
                    continue
                row = json.loads(line)
                label = row["label"]

                body = anonymize_body(row["body"])
                digest = content_hash(row["package_name"], row.get("title"), body)

                existing = await sample_repo.get_raw_by_content_hash(session, digest)
                raw = existing or await sample_repo.add_raw_if_absent(
                    session,
                    RawSample(
                        source=SampleSource.SEED.value,
                        package_name=row["package_name"],
                        notification_title=row.get("title"),
                        notification_body=body,
                        received_at=_parse(row["received_at"]),
                        user_id_hash=None,
                        content_hash=digest,
                    ),
                )
                if existing is None:
                    added_raw += 1

                if await sample_repo.get_label_for_raw(session, raw.id) is not None:
                    continue

                labeled = await sample_repo.add_label(
                    session,
                    LabeledSample(
                        raw_sample_id=raw.id,
                        is_financial=label["is_financial"],
                        transaction_type=label.get("transaction_type"),
                        amount_cents=label.get("amount_cents"),
                        merchant_name=label.get("merchant_name"),
                        category_slug=label.get("category_slug"),
                        labeled_by=LabelSource.SEED.value,
                    ),
                )
                added_label += 1

                split, bucket = split_for(digest)
                await sample_repo.add_split(
                    session,
                    SampleSplit(
                        labeled_sample_id=labeled.id,
                        split=split.value,
                        split_seed=bucket,
                    ),
                )
                counts[split.value] = counts.get(split.value, 0) + 1

        await session.commit()

    print(f"raw_samples mới: {added_raw}")
    print(f"labeled_samples mới: {added_label}")
    for name in (SplitName.TRAIN, SplitName.VAL, SplitName.TEST):
        print(f"  {name.value}: {counts.get(name.value, 0)}")


def _parse(value: str):
    from datetime import datetime

    return datetime.fromisoformat(value)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--corpus-dir", default="data/corpus", type=pathlib.Path)
    args = parser.parse_args()
    asyncio.run(seed(args.corpus_dir))


if __name__ == "__main__":
    main()
