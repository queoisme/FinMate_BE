#!/usr/bin/env python3
"""Báo cáo mức dùng Gemini / bộ nhớ / luật của ``/bill/classify-items`` theo ngày (từ ``bill_classify_stats``). Chỉ đọc, chỉ số đếm.

Chạy:  ``python scripts/classify_stats.py [--days 14]``
Cho biết: bao nhiêu món được bộ nhớ và luật trả lời (không tốn Gemini), bao nhiêu phải hỏi Gemini, bao nhiêu món cần AI mà vượt trần 20 món mỗi lần,
và bao nhiêu lượt gọi Gemini trên mỗi bill. Dùng để quyết định có đáng làm mô hình học hay không.
"""

from __future__ import annotations

import argparse
import asyncio
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))

from sqlalchemy import text


def _rate(part: int, whole: int) -> float:
    return round(part / whole, 4) if whole else 0.0


def summarize(rows: dict[str, dict[str, int]]) -> list[dict]:
    """``rows``: {ngày: {metric: giá trị}} -> danh sách mới nhất trước, mỗi ngày có các tỷ lệ."""
    out = []
    for date in sorted(rows, reverse=True):
        m = rows[date]
        bills, items = m.get("requests", 0), m.get("items", 0)
        candidates, sent = m.get("ai_candidates", 0), m.get("ai_sent", 0)
        out.append({
            "date": date,
            "bills": bills,
            "items": items,
            "items_per_bill": round(items / bills, 2) if bills else 0,
            "cache_rate": _rate(m.get("src_cache", 0), items),
            "rule_rate": _rate(m.get("src_rule", 0), items),
            "merchant_rate": _rate(m.get("src_merchant", 0), items),
            "ai_rate": _rate(m.get("src_ai", 0), items),
            "unknown_rate": _rate(m.get("src_unknown", 0), items),
            "ai_calls_per_bill": round(m.get("ai_requests", 0) / bills, 4) if bills else 0,
            "overflow_rate": _rate(candidates - sent, candidates),
            "ai_failed": m.get("ai_failed", 0),
            "noconsent_rate": _rate(m.get("noconsent_requests", 0), bills),
        })
    return out


def format_report(days: list[dict]) -> str:
    if not days:
        return "Chưa có số liệu (chưa có lượt phân loại món nào kể từ khi bật bộ đếm)."
    pct = lambda x: f"{x:.0%}"  # noqa: E731
    lines = [f"{'ngày':10} {'bill':>5} {'món/bill':>8} {'bộ nhớ':>7} {'luật':>6} {'cửa hàng':>8} {'Gemini':>7} {'chưa rõ':>7} {'gọi AI/bill':>11} {'vượt trần':>9} {'lỗi AI':>6} {'không đồng ý':>12}"]
    for d in days:
        lines.append(f"{d['date']:10} {d['bills']:>5} {d['items_per_bill']:>8} {pct(d['cache_rate']):>7} {pct(d['rule_rate']):>6} {pct(d['merchant_rate']):>8} "
                     f"{pct(d['ai_rate']):>7} {pct(d['unknown_rate']):>7} {d['ai_calls_per_bill']:>11} {pct(d['overflow_rate']):>9} {d['ai_failed']:>6} {pct(d['noconsent_rate']):>12}")
    return "\n".join(lines)


async def main(days: int) -> int:
    from app.db.session import AsyncSessionLocal

    async with AsyncSessionLocal() as session:
        result = await session.execute(
            text("SELECT day::text, metric, value FROM bill_classify_stats WHERE day >= current_date - make_interval(days => :days)"), {"days": days})
        rows: dict[str, dict[str, int]] = {}
        for day, metric, value in result.all():
            rows.setdefault(day, {})[metric] = int(value)
    print(format_report(summarize(rows)))
    return 0


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--days", type=int, default=14)
    sys.exit(asyncio.run(main(parser.parse_args().days)))
