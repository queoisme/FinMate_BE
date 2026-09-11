#!/usr/bin/env python3
"""Đưa một ``model_version`` lên ``active`` — tức là bắt đầu phục vụ request thật.

Cổng chặn: từ chối promote model chưa có lần chấm nào trên split ``test``. Không có cổng
này thì "train xong là dùng" và chẳng ai biết model mới tốt hơn hay tệ hơn model cũ.

Version đang active của cùng stage tự chuyển sang ``archived`` (partial unique index
``uq_model_versions_active`` không cho hai dòng cùng active).

Chạy: ``python scripts/promote.py --stage categorizer --version 1.0.1``
"""

from __future__ import annotations

import argparse
import asyncio
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))

from sqlalchemy import select

from app.core.enums import ModelStage, SplitName
from app.db.models.evaluation import EvaluationRun
from app.db.repositories import model_repo
from app.db.session import AsyncSessionLocal

MIN_ACCURACY = 0.70


async def promote(stage: ModelStage, version: str, force: bool) -> None:
    async with AsyncSessionLocal() as session:
        target = await model_repo.get_by_stage_version(session, stage, version)
        if target is None:
            raise SystemExit(f"Không có {stage.value} version {version}")

        result = await session.execute(
            select(EvaluationRun)
            .where(
                EvaluationRun.model_version_id == target.id,
                EvaluationRun.split == SplitName.TEST.value,
            )
            .order_by(EvaluationRun.created_at.desc())
            .limit(1)
        )
        run = result.scalar_one_or_none()

        if run is None and not force:
            raise SystemExit(
                f"{stage.value} {version} chưa được chấm trên split test. "
                f"Chạy: python scripts/evaluate.py --stage {stage.value} "
                f"--version {version}"
            )
        if run is not None and run.accuracy < MIN_ACCURACY and not force:
            raise SystemExit(
                f"accuracy {run.accuracy:.3f} dưới ngưỡng {MIN_ACCURACY}. "
                "Dùng --force nếu vẫn muốn promote."
            )

        previous = await model_repo.get_active(session, stage)
        await model_repo.promote(session, target.id)
        await session.commit()

    print(f"{stage.value}: {previous.version if previous else '(chưa có)'} → {version}")
    if run is not None:
        print(
            f"  accuracy trên test = {run.accuracy:.3f}, macro F1 = {run.macro_f1:.3f}"
        )
    print("  AI Service nạp lại trong vòng 60 giây, không cần restart.")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--stage", required=True, choices=[s.value for s in ModelStage])
    parser.add_argument("--version", required=True)
    parser.add_argument(
        "--force", action="store_true", help="bỏ qua cổng chặn evaluation"
    )
    args = parser.parse_args()
    asyncio.run(promote(ModelStage(args.stage), args.version, args.force))


if __name__ == "__main__":
    main()
