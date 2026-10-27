#!/usr/bin/env python3
"""Huấn luyện / bật / tắt mô hình nhóm hóa đơn học từ nhãn người dùng chốt (``ml_training_samples``).

  python scripts/train_bill_model.py --train             huấn luyện; ra candidate hoặc bị loại kèm lý do. KHÔNG bao giờ tự bật.
  python scripts/train_bill_model.py --promote <version>  bật một bản candidate (bản active cũ nghỉ). Khởi động lại ai-service để nạp.
  python scripts/train_bill_model.py --retire             tắt bản active, quay về luật. Khởi động lại ai-service để có hiệu lực.
  python scripts/train_bill_model.py --list               các phiên bản và số đo của chúng
Trước khi huấn luyện nên xem ``python scripts/learning_report.py`` (đã đủ dữ liệu chưa).
"""

from __future__ import annotations

import argparse
import asyncio
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))

from sqlalchemy import select

from app.bill import slug_model
from app.bill.slug_model import TrainResult
from learning_report import load_labeled, rule_predict


async def run_train(session, registry_path: str) -> tuple[str, TrainResult]:
    rows = await load_labeled(session)
    result = slug_model.train(rows, rule_predict)
    version = await slug_model.record_training(session, result, sample_count=len(rows), registry_path=registry_path)
    return version, result


async def list_versions(session) -> list[str]:
    from app.db.models.model_version import ModelVersion

    rows = (await session.execute(select(ModelVersion).where(ModelVersion.stage == slug_model.STAGE).order_by(ModelVersion.created_at.desc()))).scalars().all()
    return [f"{r.version}  {r.status:<9} {r.metrics.get('cv_accuracy') if r.metrics else '-'}  {r.notes or ''}" for r in rows]


async def main() -> int:
    parser = argparse.ArgumentParser()
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument("--train", action="store_true")
    group.add_argument("--promote", metavar="VERSION")
    group.add_argument("--retire", action="store_true")
    group.add_argument("--list", action="store_true")
    args = parser.parse_args()

    from app.core.config import settings
    from app.db.session import AsyncSessionLocal

    async with AsyncSessionLocal() as session:
        if args.train:
            version, result = await run_train(session, settings.model_registry_path)
            print(f"Phiên bản {version}: {result.status.upper()} - {result.reason}")
            print(f"  kiểm tra chéo theo cửa hàng: {result.cv_accuracy}   luật hiện tại: {result.rule_accuracy}")
            for key in ("coverage_at_threshold", "accuracy_at_threshold"):
                print(f"  {key}: {result.metrics.get(key)}")
            if result.status == "candidate":
                print(f"Để bật: python scripts/train_bill_model.py --promote {version}")
        elif args.promote:
            try:
                await slug_model.promote(session, args.promote)
            except ValueError as error:
                print(error)
                return 1
            print("Đã bật. Khởi động lại ai-service để nạp mô hình.")
        elif args.retire:
            print(f"Đã tắt {await slug_model.retire(session)} bản. Khởi động lại ai-service để có hiệu lực.")
        else:
            print("\n".join(await list_versions(session)) or "Chưa có phiên bản nào.")
    return 0


if __name__ == "__main__":
    sys.exit(asyncio.run(main()))
