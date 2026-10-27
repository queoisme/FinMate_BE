#!/usr/bin/env python3
"""Xuất tập dữ liệu phân loại danh mục ra ``category-classifier/{train,validation,test}.csv``.

Nguồn: ``ml_labeled_samples`` ghép ``ml_raw_samples`` (chỉ ``source = 'receipt'``: hóa đơn đã có nhãn) (chữ hóa đơn ĐÃ lọc dữ liệu cá nhân + nhãn). Chia theo CỬA HÀNG: một cửa hàng chỉ
nằm ở một tập, nếu không việc đo độ chính xác trên tập kiểm tra chỉ đo việc học thuộc cửa hàng.

Các tệp CSV chứa chữ hóa đơn nên KHÔNG đưa vào git (.gitignore); chỉ thư mục, README và script này nằm trong git.

Chạy:  python scripts/export_category_dataset.py            (DATABASE_URL của ai-service)
"""

from __future__ import annotations

import asyncio
import csv
import hashlib
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))

OUTPUT_DIR = pathlib.Path(__file__).resolve().parent.parent / "category-classifier"
COLUMNS = ["text", "label", "merchant_key", "labeled_by"]
VALIDATION_FRACTION = 0.15
TEST_FRACTION = 0.15


def split_by_merchant(rows: list[dict]) -> tuple[list[dict], list[dict], list[dict]]:
    """(train, validation, test). Cùng ``merchant_key`` thì cùng một tập; dòng không có cửa hàng tự thành một nhóm. Cố định theo băm nên chạy
    lại cho cùng kết quả (và dòng mới không xáo trộn các dòng cũ)."""
    train: list[dict] = []
    validation: list[dict] = []
    test: list[dict] = []
    for index, row in enumerate(rows):
        group = row["merchant_key"] or f"__row_{index}"
        bucket = int(hashlib.sha256(group.encode("utf-8")).hexdigest(), 16) % 1000 / 1000
        if bucket < TEST_FRACTION:
            test.append(row)
        elif bucket < TEST_FRACTION + VALIDATION_FRACTION:
            validation.append(row)
        else:
            train.append(row)
    return train, validation, test


def write_csv(path: pathlib.Path, rows: list[dict]) -> None:
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=COLUMNS)
        writer.writeheader()
        writer.writerows({key: row[key] for key in COLUMNS} for row in rows)


async def load_rows() -> list[dict]:
    from sqlalchemy import select

    from app.bill.knowledge import merchant_key
    from app.core.category_map import SLUG_BY_LOCAL_CATEGORY
    from app.core.enums import SampleSource
    from app.db.models.labeled_sample import LabeledSample
    from app.db.models.raw_sample import RawSample
    from app.db.session import AsyncSessionLocal

    query = (
        select(RawSample.notification_title, RawSample.notification_body, LabeledSample.category_slug, LabeledSample.labeled_by)
        .join(LabeledSample, LabeledSample.raw_sample_id == RawSample.id)
        .where(LabeledSample.category_slug.is_not(None), RawSample.source == SampleSource.RECEIPT.value)
    )
    async with AsyncSessionLocal() as session:
        result = await session.execute(query)
        return [
            {
                "text": f"{title or ''}\n{body}".strip(),
                "label": SLUG_BY_LOCAL_CATEGORY.get(group, group),
                "merchant_key": merchant_key(title) if title else "",
                "labeled_by": by,
            }
            for title, body, group, by in result.all()
        ]


async def main() -> None:
    rows = await load_rows()
    parts = split_by_merchant(rows)
    OUTPUT_DIR.mkdir(exist_ok=True)
    for name, part in zip(("train", "validation", "test"), parts):
        write_csv(OUTPUT_DIR / f"{name}.csv", part)
        print(f"{name}.csv: {len(part)} dòng")
    print(f"Tổng {len(rows)} dòng, đã chia theo cửa hàng.")


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    asyncio.run(main())
