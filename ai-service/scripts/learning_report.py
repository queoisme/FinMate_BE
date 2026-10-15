#!/usr/bin/env python3
"""Báo cáo vòng học: đã đủ dữ liệu có nhãn để huấn luyện chưa, luật hiện tại đúng bao nhiêu, và từng nguồn dự đoán (Gemini, bộ nhớ, thương hiệu...) đúng bao nhiêu
so với nhãn do NGƯỜI DÙNG chọn. Chỉ đọc, không ghi gì. Chạy: ``python scripts/learning_report.py``
"""

from __future__ import annotations

import asyncio
import pathlib
import sys
from collections import Counter
from dataclasses import dataclass, field

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))

from sqlalchemy import text

from app.bill.classifier import classify_category_detailed
from app.core.category_map import SLUG_BY_LOCAL_CATEGORY
from app.services.category_resolver import resolve

# Cổng tối thiểu để huấn luyện (kế hoạch learning-loop, Global Constraints). Là giả định chưa kiểm chứng; chỉnh khi có dữ liệu thật.
MIN_LABELED = 300
MIN_MERCHANTS = 30
MIN_STRONG_SLUGS = 3  # số danh mục đủ MIN_PER_SLUG mẫu
MIN_PER_SLUG = 20


@dataclass(frozen=True)
class SampleRow:
    merchant_key: str | None
    lines: list[str]
    final_slug: str
    predicted_source: str | None
    predicted_slug: str | None


@dataclass
class LearningReport:
    labeled: int
    pending: int
    merchants: int
    per_slug: dict[str, int]
    rule_accuracy: float | None
    accuracy_by_source: dict[str, tuple[int, float]]
    ready: bool
    missing: list[str] = field(default_factory=list)


def rule_predict(row: SampleRow) -> str | None:
    """Luật hiện tại (thương hiệu/từ khóa/sản phẩm), rồi tới bộ từ vựng từng dòng. Không có bộ nhớ người dùng, không Gemini: đó là mốc để model phải vượt."""
    category, source, _ = classify_category_detailed(row.merchant_key, row.lines)
    if source != "none":
        return SLUG_BY_LOCAL_CATEGORY.get(category)
    votes = Counter(s for s in (resolve(line).slug for line in row.lines) if s)
    return votes.most_common(1)[0][0] if votes else None


def summarize(rows: list[SampleRow], pending: int) -> LearningReport:
    per_slug = dict(Counter(r.final_slug for r in rows))
    merchants = len({r.merchant_key for r in rows if r.merchant_key})

    by_source: dict[str, list[bool]] = {}
    for r in rows:
        if r.predicted_source and r.predicted_source != "none":  # "none" = không đoán được gì, không phải một nguồn dự đoán
            by_source.setdefault(r.predicted_source, []).append(r.predicted_slug == r.final_slug)

    missing: list[str] = []
    if len(rows) < MIN_LABELED:
        missing.append(f"cần thêm {MIN_LABELED - len(rows)} mẫu có nhãn (đang có {len(rows)}/{MIN_LABELED})")
    if merchants < MIN_MERCHANTS:
        missing.append(f"cần thêm {MIN_MERCHANTS - merchants} cửa hàng khác nhau (đang có {merchants}/{MIN_MERCHANTS})")
    strong = sum(1 for n in per_slug.values() if n >= MIN_PER_SLUG)
    if strong < MIN_STRONG_SLUGS:
        missing.append(f"cần thêm {MIN_STRONG_SLUGS - strong} danh mục có từ {MIN_PER_SLUG} mẫu trở lên (đang có {strong}/{MIN_STRONG_SLUGS})")

    return LearningReport(
        labeled=len(rows),
        pending=pending,
        merchants=merchants,
        per_slug=per_slug,
        rule_accuracy=sum(rule_predict(r) == r.final_slug for r in rows) / len(rows) if rows else None,
        accuracy_by_source={src: (len(hits), sum(hits) / len(hits)) for src, hits in by_source.items()},
        ready=not missing,
        missing=missing,
    )


async def build_report(session) -> LearningReport:
    labeled = (
        await session.execute(
            text("SELECT merchant_key, lines, final_slug, predicted_source, predicted_slug FROM bill_training_samples WHERE final_slug IS NOT NULL")
        )
    ).all()
    pending = (await session.execute(text("SELECT count(*) FROM bill_training_samples WHERE final_slug IS NULL"))).scalar_one()
    rows = [SampleRow(r.merchant_key, r.lines.split("\n"), r.final_slug, r.predicted_source, r.predicted_slug) for r in labeled]
    return summarize(rows, pending)


def format_report(report: LearningReport) -> str:
    pct = lambda x: "chưa có" if x is None else f"{x:.0%}"  # noqa: E731
    out = [
        f"Mẫu có nhãn: {report.labeled}   Chờ nhãn: {report.pending}   Cửa hàng khác nhau: {report.merchants}",
        "Theo danh mục: " + (", ".join(f"{s}={n}" for s, n in sorted(report.per_slug.items(), key=lambda kv: -kv[1])) or "chưa có"),
        f"Luật hiện tại đúng: {pct(report.rule_accuracy)}",
        "Độ đúng của từng nguồn dự đoán so với nhãn người dùng:",
    ]
    out += [f"  {src:<8} {pct(acc):>5}  ({n} mẫu)" for src, (n, acc) in sorted(report.accuracy_by_source.items())] or ["  chưa có"]
    out.append("SẴN SÀNG huấn luyện." if report.ready else "CHƯA đủ để huấn luyện:")
    out += [f"  - {m}" for m in report.missing]
    return "\n".join(out)


async def main() -> int:
    from app.db.session import AsyncSessionLocal

    async with AsyncSessionLocal() as session:
        print(format_report(await build_report(session)))
    return 0


if __name__ == "__main__":
    sys.exit(asyncio.run(main()))
