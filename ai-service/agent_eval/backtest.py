"""Backtest dải dự báo thu nhập: so cách đang dùng ở backend (``empirical_quantile`` = Q10/Q50/Q90 của lịch sử) với vài phương pháp khác trên hồ sơ giả lập có tương lai đã biết.

    python -m agent_eval.backtest

Mỗi hồ sơ có ``HISTORY_MONTHS`` tháng lịch sử (đưa cho phương pháp) và ``FUTURE_MONTHS`` tháng tương lai (chỉ để chấm). Mục tiêu mặc định là THU NHẬP hằng tháng, đúng thứ ``IntervalForecaster`` dự báo.
Dải 80% (Q10..Q90) lý tưởng phủ ~80% tháng tương lai; dải quá rộng thì phủ cao nhưng vô dụng nên báo kèm độ rộng. Kết quả tách theo ba kiểu hồ sơ vì trung bình gộp che mất chỗ phương pháp hỏng."""

from __future__ import annotations

import statistics
from collections import defaultdict
from typing import Callable

import numpy as np
from sklearn.ensemble import GradientBoostingRegressor

from agent_eval.personas import FUTURE_MONTHS, KINDS, Persona, make_persona

QUANTILES = (0.1, 0.5, 0.9)
Z80 = 1.2816  # phân vị 90% của phân phối chuẩn: mean ± Z80·σ là dải 80%
Forecast = tuple[list[float], list[float], list[float]]  # (Q10, Q50, Q90) cho từng tháng tương lai


def coverage(truth: list[int], lo: list[int], hi: list[int]) -> float:
    """Tỉ lệ tháng mà giá trị thật nằm trong [lo, hi]."""
    return sum(l <= t <= h for t, l, h in zip(truth, lo, hi)) / len(truth)


def pinball_loss(truth: list[int], pred: list[float], q: float) -> float:
    """Tổn thất pinball ở phân vị ``q``: phạt dự báo thấp q lần, dự báo cao (1−q) lần. Càng thấp càng tốt."""
    diff = np.asarray(truth, dtype=float) - np.asarray(pred, dtype=float)
    return float(np.mean(np.maximum(q * diff, (q - 1) * diff)))


def empirical_quantile(sorted_values: list[int], p: float) -> int:
    """Bản Python của ``Statistics.Quantile`` ở backend: nội suy tuyến tính theo phần nghìn, làm tròn nửa lên, số nguyên đồng."""
    if not sorted_values:
        return 0
    permille = round(min(max(p, 0.0), 1.0) * 1000)
    position = permille * (len(sorted_values) - 1)
    lower, remainder = divmod(position, 1000)
    if remainder == 0:
        return sorted_values[lower]
    low, high = sorted_values[lower], sorted_values[lower + 1]
    return low + ((high - low) * remainder + 500) // 1000


def _flat(history: list[int], lo: float, mid: float, hi: float) -> Forecast:
    return [lo] * FUTURE_MONTHS, [mid] * FUTURE_MONTHS, [hi] * FUTURE_MONTHS


def _empirical(history: list[int]) -> Forecast:
    ordered = sorted(history)
    q10, q50, q90 = (empirical_quantile(ordered, p) for p in QUANTILES)
    return _flat(history, q10, q50, q90)


def _naive_mean(history: list[int]) -> Forecast:
    mean, std = statistics.fmean(history), statistics.pstdev(history)
    return _flat(history, mean - Z80 * std, mean, mean + Z80 * std)


def _last_month(history: list[int]) -> Forecast:
    return _flat(history, history[-1], history[-1], history[-1])  # dự báo điểm: không có dải


def _sklearn_gbr(history: list[int]) -> Forecast:
    """Gradient boosting hồi quy phân vị trên đặc trưng chu kỳ (tháng trong năm, tháng trong kỳ học). Chỉ 12 điểm nên dễ quá khớp: đây là chỗ cần đo, không phải giả định."""
    def features(months: range) -> np.ndarray:
        return np.array([[t % 12, t % 6] for t in months], dtype=float)

    train_x, future_x = features(range(len(history))), features(range(len(history), len(history) + FUTURE_MONTHS))
    out = []
    for q in QUANTILES:
        model = GradientBoostingRegressor(loss="quantile", alpha=q, n_estimators=50, max_depth=2, min_samples_leaf=2, random_state=0)
        out.append(list(model.fit(train_x, np.asarray(history, dtype=float)).predict(future_x)))
    lo, mid, hi = out
    return [min(l, m) for l, m in zip(lo, mid)], mid, [max(h, m) for h, m in zip(hi, mid)]  # các mô hình huấn luyện riêng nên có thể chéo nhau: ép lo ≤ mid ≤ hi


METHODS: dict[str, Callable[[list[int]], Forecast]] = {
    "empirical_quantile": _empirical,
    "naive_mean": _naive_mean,
    "last_month": _last_month,
    "sklearn_quantile_gbr": _sklearn_gbr,
}


def _score(persona: Persona, forecast: Forecast, target: str) -> dict[str, float]:
    truth = persona.truth["future_income" if target == "income" else "future_capacity"]
    lo, mid, hi = forecast
    return {
        "coverage": coverage(truth, [int(x) for x in lo], [int(x) for x in hi]),
        "width": float(np.mean(np.asarray(hi) - np.asarray(lo))),
        "pinball": float(np.mean([pinball_loss(truth, series, q) for series, q in zip(forecast, QUANTILES)])),
    }


def compare_forecasters(personas: list[Persona], target: str = "income") -> dict[str, dict[str, dict[str, float]]]:
    """``{kind: {method: {coverage, width, pinball}}}``: trung bình qua các hồ sơ cùng kiểu. ``target``: ``income`` (đúng thứ production dự báo) hoặc ``capacity`` (thu − chi)."""
    scores: dict[str, dict[str, list[dict[str, float]]]] = defaultdict(lambda: defaultdict(list))
    for persona in personas:
        history = persona.snapshot["monthly_income"] if target == "income" else [i - s for i, s in zip(persona.snapshot["monthly_income"], persona.snapshot["monthly_spend"])]
        for name, method in METHODS.items():
            scores[persona.kind][name].append(_score(persona, method(history), target))
    return {kind: {name: {k: float(np.mean([s[k] for s in items])) for k in ("coverage", "width", "pinball")} for name, items in methods.items()} for kind, methods in scores.items()}


def markdown(results: dict, target: str) -> str:
    lines = [f"Mục tiêu: {target}; dải 80% (Q10–Q90). `coverage` lý tưởng ≈ 0,80; `width` và `pinball` càng thấp càng tốt (đồng VND).", "", "| Kiểu hồ sơ | Phương pháp | coverage | width | pinball |", "|---|---|---|---|---|"]
    for kind in KINDS:
        for name, s in results[kind].items():
            lines.append(f"| {kind} | {name} | {s['coverage']:.2f} | {s['width']:,.0f} | {s['pinball']:,.0f} |")
    return "\n".join(lines)


def main(seeds: int = 30) -> None:
    for target in ("income", "capacity"):
        personas = [make_persona(kind, seed) for kind in KINDS for seed in range(seeds)]
        print(markdown(compare_forecasters(personas, target), target), "\n")


if __name__ == "__main__":
    main()
