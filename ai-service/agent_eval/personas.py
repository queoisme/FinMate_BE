"""Hồ sơ người dùng GIẢ LẬP có đáp án thật cho bộ đánh giá của AI Financial Agent.

Dữ liệu thật không cho ta "sự thật" về tương lai của một người; dữ liệu giả lập thì có: mỗi hồ sơ gồm ``HISTORY_MONTHS`` tháng lịch sử (đưa cho agent qua ``snapshot``) và ``FUTURE_MONTHS`` tháng
tương lai đã biết (``truth``, KHÔNG bao giờ đưa cho agent) để chấm dự báo. Ba kiểu thu nhập: công nhân lương cố định, freelancer dao động mạnh, sinh viên trợ cấp + làm thêm + học phí theo kỳ.
Sinh bằng ``random.Random(seed)`` nên cùng hạt giống cho đúng cùng hồ sơ. Tiền là số nguyên đồng làm tròn nghìn (không bao giờ float).

``snapshot`` có đúng các đầu ra của ``IFinancialDataSource`` phía backend (``monthly_income``, ``monthly_spend``, ``total_balance``, cũ nhất trước; ``category_spend`` = chi theo nhóm của 3 tháng gần nhất)."""

from __future__ import annotations

import random
from dataclasses import dataclass
from typing import Any

KINDS = ("stable_worker", "freelancer", "student")
HISTORY_MONTHS = 12
FUTURE_MONTHS = 24
CATEGORY_MONTHS = 3  # chi theo nhóm phủ đúng 3 tháng gần nhất: cùng cửa sổ plan_budget/spending_summary(months=3) đọc

# Tỉ trọng chi theo nhóm của từng kiểu hồ sơ (slug là nhóm hệ thống thật trong CategorySeeder); nhóm ĐẦU TIÊN nhận phần dư làm tròn.
CATEGORY_WEIGHTS: dict[str, tuple[tuple[str, str, int], ...]] = {
    "stable_worker": (("food", "Ăn uống", 30), ("housing", "Nhà ở", 25), ("transport", "Di chuyển", 12), ("bills", "Hóa đơn", 10), ("shopping", "Mua sắm", 10), ("entertainment", "Giải trí", 6), ("health", "Sức khỏe", 4), ("other", "Khác", 3)),
    "freelancer": (("food", "Ăn uống", 28), ("housing", "Nhà ở", 22), ("bills", "Hóa đơn", 12), ("shopping", "Mua sắm", 12), ("transport", "Di chuyển", 10), ("entertainment", "Giải trí", 8), ("health", "Sức khỏe", 5), ("other", "Khác", 3)),
    "student": (("food", "Ăn uống", 35), ("education", "Giáo dục", 25), ("housing", "Nhà ở", 15), ("transport", "Di chuyển", 8), ("shopping", "Mua sắm", 7), ("bills", "Hóa đơn", 5), ("entertainment", "Giải trí", 5)),
}


@dataclass(frozen=True)
class Persona:
    kind: str
    seed: int
    snapshot: dict[str, Any]  # {"monthly_income": [int], "monthly_spend": [int], "total_balance": int, "category_spend": [{"slug", "name", "spent_vnd"}]}
    truth: dict[str, list[int]]  # {"future_income", "future_spend", "future_capacity"}: chỉ để chấm, agent không thấy


def _round_k(value: float) -> int:
    return max(0, int(round(value / 1000.0)) * 1000)


def _series(kind: str, rng: random.Random, months: int) -> tuple[list[int], list[int], int]:
    """(thu theo tháng, chi theo tháng, số dư khởi điểm) của MỘT hồ sơ, dài ``months`` tháng liên tiếp."""
    income: list[int] = []
    spend: list[int] = []
    if kind == "stable_worker":
        salary = rng.randrange(9_000_000, 18_000_001, 100_000)
        spend_ratio = rng.uniform(0.60, 0.75)
        for _ in range(months):
            income.append(_round_k(salary * rng.uniform(0.98, 1.03)))  # làm thêm giờ nhỏ
            spend.append(_round_k(salary * spend_ratio * rng.uniform(0.92, 1.08)))
        balance = _round_k(salary * spend_ratio * rng.uniform(1.5, 4.0))
    elif kind == "freelancer":
        base = rng.randrange(12_000_000, 25_000_001, 100_000)
        spend_level = base * rng.uniform(0.50, 0.60)
        for _ in range(months):
            dry = rng.random() < 0.22  # tháng ế khách
            factor = rng.uniform(0.10, 0.25) if dry else rng.uniform(0.60, 1.90)
            income.append(_round_k(base * factor))
            spend.append(_round_k(spend_level * rng.uniform(0.85, 1.15)))
        balance = _round_k(spend_level * rng.uniform(0.3, 1.5))
    else:  # student
        allowance = rng.randrange(3_000_000, 5_000_001, 100_000)
        for index in range(months):
            part_time = rng.randrange(1_000_000, 3_000_001, 100_000) if rng.random() < 0.5 else 0
            income.append(_round_k(allowance + part_time))
            tuition = rng.randrange(8_000_000, 12_000_001, 100_000) if (index + 1) % 6 == 0 else 0  # học phí mỗi kỳ (6 tháng)
            spend.append(_round_k(allowance * rng.uniform(0.80, 0.95) + tuition))
        balance = _round_k(allowance * rng.uniform(0.2, 1.0))
    return income, spend, balance


def _category_spend(kind: str, seed: int, total: int) -> list[dict[str, Any]]:
    """Chia ``total`` (bội của 1.000) theo tỉ trọng cố định có dao động nhỏ theo hạt giống; cộng lại ĐÚNG bằng ``total``.
    Dùng RNG riêng nên chuỗi thu/chi của hồ sơ không đổi so với trước khi có phần này."""
    rng = random.Random(f"{kind}:{seed}:category")
    weights = [(slug, name, weight * rng.uniform(0.8, 1.2)) for slug, name, weight in CATEGORY_WEIGHTS[kind]]
    scale = sum(w for _, _, w in weights)
    rest = [{"slug": slug, "name": name, "spent_vnd": int(total * w / scale) // 1000 * 1000} for slug, name, w in weights[1:]]  # làm tròn XUỐNG nên phần dư ≥ 0
    first_slug, first_name, _ = weights[0]
    return [{"slug": first_slug, "name": first_name, "spent_vnd": total - sum(r["spent_vnd"] for r in rest)}, *rest]


def make_persona(kind: str, seed: int, months: int = HISTORY_MONTHS) -> Persona:
    if kind not in KINDS:
        raise ValueError(f"kiểu hồ sơ không hợp lệ: {kind!r}; được phép: {', '.join(KINDS)}")
    rng = random.Random(f"{kind}:{seed}")
    income, spend, balance = _series(kind, rng, months + FUTURE_MONTHS)
    history_income, history_spend = income[:months], spend[:months]
    future_income, future_spend = income[months:], spend[months:]
    return Persona(
        kind=kind,
        seed=seed,
        snapshot={
            "monthly_income": history_income,
            "monthly_spend": history_spend,
            "total_balance": balance,
            "category_spend": _category_spend(kind, seed, sum(history_spend[-CATEGORY_MONTHS:])),
        },
        truth={
            "future_income": future_income,
            "future_spend": future_spend,
            "future_capacity": [i - s for i, s in zip(future_income, future_spend)],
        },
    )


def months_to_reach(persona: Persona, price_vnd: int) -> int | None:
    """Số tháng THẬT để đủ ``price_vnd`` theo tương lai đã biết (không tính tăng giá); None nếu không tới trong ``FUTURE_MONTHS`` tháng."""
    saved = persona.snapshot["total_balance"]
    if saved >= price_vnd:
        return 0
    for month, capacity in enumerate(persona.truth["future_capacity"], start=1):
        saved += capacity
        if saved >= price_vnd:
            return month
    return None
