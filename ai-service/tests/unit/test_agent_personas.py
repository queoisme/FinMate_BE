"""Hồ sơ người dùng giả lập có đáp án thật (Task 10): ba kiểu thu nhập, sinh theo hạt giống cố định, tiền toàn số nguyên đồng."""

import statistics

import pytest

from agent_eval.personas import KINDS, FUTURE_MONTHS, HISTORY_MONTHS, Persona, make_persona, months_to_reach


def cv(values) -> float:
    return statistics.pstdev(values) / statistics.mean(values)


@pytest.mark.parametrize("kind", KINDS)
def test_the_same_seed_gives_the_same_persona(kind):
    assert make_persona(kind, 7) == make_persona(kind, 7)


@pytest.mark.parametrize("kind", KINDS)
def test_different_seeds_give_different_personas(kind):
    assert make_persona(kind, 1).snapshot != make_persona(kind, 2).snapshot


@pytest.mark.parametrize("kind", KINDS)
def test_the_snapshot_has_the_three_data_source_outputs_and_only_integers(kind):
    persona = make_persona(kind, 3)

    assert set(persona.snapshot) == {"monthly_income", "monthly_spend", "total_balance"}
    assert len(persona.snapshot["monthly_income"]) == len(persona.snapshot["monthly_spend"]) == HISTORY_MONTHS
    values = [*persona.snapshot["monthly_income"], *persona.snapshot["monthly_spend"], persona.snapshot["total_balance"], *persona.truth["future_income"], *persona.truth["future_spend"]]
    assert all(type(v) is int and v >= 0 and v % 1000 == 0 for v in values)  # số nguyên đồng, làm tròn nghìn, không âm


@pytest.mark.parametrize("kind", KINDS)
def test_the_truth_covers_the_future_months_after_the_history(kind):
    persona = make_persona(kind, 5)

    assert len(persona.truth["future_income"]) == len(persona.truth["future_spend"]) == len(persona.truth["future_capacity"]) == FUTURE_MONTHS
    assert persona.truth["future_capacity"] == [i - s for i, s in zip(persona.truth["future_income"], persona.truth["future_spend"])]


def test_a_stable_worker_has_low_income_variation_and_a_freelancer_has_high_variation():
    stable = [cv(make_persona("stable_worker", s).snapshot["monthly_income"]) for s in range(20)]
    freelancer = [cv(make_persona("freelancer", s).snapshot["monthly_income"]) for s in range(20)]

    assert max(stable) < 0.10
    assert min(freelancer) > 0.25
    assert statistics.mean(freelancer) > 3 * statistics.mean(stable)


def test_a_student_pays_tuition_in_a_spike_every_six_months():
    persona = make_persona("student", 11)
    spend = persona.snapshot["monthly_spend"]

    spikes = [i for i, v in enumerate(spend) if v > 1.8 * statistics.median(spend)]
    assert len(spikes) >= 1
    assert all((i + 1) % 6 == 0 for i in spikes)


def test_an_unknown_kind_is_rejected():
    with pytest.raises(ValueError):
        make_persona("millionaire", 1)


def test_months_to_reach_is_computed_exactly_from_the_true_future():
    persona = Persona("stable_worker", 0, {"monthly_income": [], "monthly_spend": [], "total_balance": 100_000_000}, {"future_income": [30_000_000] * 24, "future_spend": [20_000_000] * 24, "future_capacity": [10_000_000] * 24})

    assert months_to_reach(persona, 200_000_000) == 10  # (200 − 100) / 10
    assert months_to_reach(persona, 100_000_000) == 0  # đã đủ
    assert months_to_reach(persona, 5_000_000_000) is None  # không tới trong thời gian đã biết


def test_the_future_truth_is_not_part_of_the_snapshot():
    persona = make_persona("freelancer", 9)

    assert len(persona.snapshot["monthly_income"]) == HISTORY_MONTHS  # lịch sử chỉ gồm HISTORY_MONTHS tháng đầu
    assert "future_income" not in persona.snapshot and "future_capacity" not in persona.snapshot
