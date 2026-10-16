"""Huấn luyện mô hình nhóm hóa đơn có cổng chặn: chỉ ra bản "candidate" khi đủ mẫu, CV theo cửa hàng đạt ngưỡng và hơn luật hiện tại."""

import random
from dataclasses import dataclass

from app.bill.slug_model import MIN_CV_ACCURACY, MIN_LABELED, MIN_MERCHANTS, train


@dataclass
class Row:
    merchant_key: str | None
    lines: list[str]
    final_slug: str


WORDS = {
    "food": ["pho", "bun", "com", "tra sua", "ca phe", "banh mi"],
    "transport": ["xang", "gui xe", "ve xe", "taxi", "grab bike", "rua xe"],
    "health": ["thuoc", "kham benh", "vitamin", "khau trang", "xet nghiem", "nha thuoc"],
    "shopping": ["ao", "quan", "giay", "tui xach", "kinh", "dong ho"],
}


def merchant_name(i: int) -> str:
    """Tên chỉ có chữ cái (record_text bỏ chữ số) và khác nhau giữa các cửa hàng."""
    return "".join(chr(97 + (i // 26**k) % 26) for k in range(3)) + " store"


def signal_rows(per_slug: int = 100, merchants_per_slug: int = 12) -> list[Row]:
    """Dòng chữ của mỗi nhóm có từ đặc trưng riêng: học được và đo theo cửa hàng vẫn đúng."""
    rng = random.Random(0)
    rows = []
    for s, (slug, words) in enumerate(WORDS.items()):
        for i in range(per_slug):
            shop = merchant_name(s * merchants_per_slug + i % merchants_per_slug)
            rows.append(Row(shop, [rng.choice(words), rng.choice(words), "tong cong"], slug))
    return rows


def no_rules(row) -> str | None:
    return None


def test_rejected_below_sample_gate():
    result = train(signal_rows(per_slug=5), no_rules)
    assert result.status == "rejected"
    assert str(MIN_LABELED) in result.reason
    assert result.pipeline is None


def test_rejected_when_too_few_merchants():
    rows = signal_rows(per_slug=100, merchants_per_slug=2)  # 8 cửa hàng < MIN_MERCHANTS
    assert len({r.merchant_key for r in rows}) < MIN_MERCHANTS
    result = train(rows, no_rules)
    assert result.status == "rejected" and "cửa hàng" in result.reason


def test_rejected_when_cv_below_threshold():
    rng = random.Random(1)
    rows = [Row(merchant_name(i % 40), [rng.choice(sum(WORDS.values(), []))], rng.choice(list(WORDS))) for i in range(400)]  # nhãn ngẫu nhiên
    result = train(rows, no_rules)
    assert result.status == "rejected"
    assert result.cv_accuracy is not None and result.cv_accuracy < MIN_CV_ACCURACY
    assert "kiểm tra chéo" in result.reason


def test_rejected_when_not_better_than_rules():
    rows = signal_rows()
    result = train(rows, lambda row: row.final_slug)  # luật hoàn hảo: mô hình không thể hơn
    assert result.status == "rejected" and "luật" in result.reason
    assert result.rule_accuracy == 1.0


def test_candidate_when_all_gates_pass():
    result = train(signal_rows(), no_rules)
    assert result.status == "candidate", result.reason
    assert result.cv_accuracy >= MIN_CV_ACCURACY and result.rule_accuracy == 0.0
    assert result.pipeline is not None
    assert result.metrics["accuracy_at_threshold"] >= 0.9 and 0 < result.metrics["coverage_at_threshold"] <= 1


def test_cv_groups_by_merchant():
    """Mỗi cửa hàng có chữ riêng và nhãn ngẫu nhiên: nếu CV để cùng cửa hàng ở cả hai phía thì học thuộc cửa hàng và đạt ~100%."""
    rng = random.Random(2)
    label_of = {i: rng.choice(list(WORDS)) for i in range(40)}
    rows = [Row(merchant_name(i), [merchant_name(i)], label_of[i]) for i in range(40) for _ in range(10)]
    result = train(rows, no_rules)
    assert result.status == "rejected"
    assert result.cv_accuracy < 0.6


def test_rows_without_merchant_are_their_own_group():
    rows = signal_rows()
    for r in rows[::2]:
        r.merchant_key = None
    assert train(rows, no_rules).status in ("candidate", "rejected")  # không nổ khi thiếu khóa cửa hàng
