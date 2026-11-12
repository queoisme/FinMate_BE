"""Backtest dải dự báo (Task 13): chỉ số đúng trên ví dụ tính tay, bản Python của phân vị khớp bản backend, và kết quả tách theo kiểu hồ sơ."""

import pytest

from agent_eval import backtest
from agent_eval.personas import KINDS, make_persona


def test_coverage_of_perfect_interval_is_one():
    assert backtest.coverage([5, 6, 7], [5, 6, 7], [5, 6, 7]) == 1.0
    assert backtest.coverage([5, 6, 7], [0, 0, 0], [10, 10, 10]) == 1.0


def test_coverage_counts_months_outside_the_interval():
    assert backtest.coverage([5, 20, 7, -1], [0, 0, 0, 0], [10, 10, 10, 10]) == pytest.approx(0.5)


def test_pinball_loss_known_value():
    assert backtest.pinball_loss([10], [8], 0.9) == pytest.approx(1.8)  # dự báo thấp: phạt q·2
    assert backtest.pinball_loss([10], [12], 0.9) == pytest.approx(0.2)  # dự báo cao: phạt (1−q)·2
    assert backtest.pinball_loss([10, 10], [8, 12], 0.5) == pytest.approx(1.0)


def test_empirical_quantile_matches_the_backend_integer_algorithm():
    ordered = [10, 20, 30, 40, 50]

    assert backtest.empirical_quantile(ordered, 0.5) == 30
    assert backtest.empirical_quantile(ordered, 0.1) == 14  # vị trí 0,4 → 10 + (20−10)·0,4
    assert backtest.empirical_quantile(ordered, 0.9) == 46
    assert backtest.empirical_quantile([], 0.5) == 0
    assert backtest.empirical_quantile([7], 0.9) == 7


def test_results_are_split_by_persona_kind():
    personas = [make_persona(kind, seed) for kind in KINDS for seed in range(3)]

    results = backtest.compare_forecasters(personas)

    assert set(results) == set(KINDS)
    for kind in KINDS:
        assert set(results[kind]) == set(backtest.METHODS)
        for scores in results[kind].values():
            assert 0.0 <= scores["coverage"] <= 1.0 and scores["width"] >= 0 and scores["pinball"] >= 0


def test_a_point_forecast_has_no_width_and_the_empirical_interval_is_wider_for_freelancers():
    personas = [make_persona(kind, seed) for kind in ("stable_worker", "freelancer") for seed in range(5)]

    results = backtest.compare_forecasters(personas)

    assert results["freelancer"]["last_month"]["width"] == 0
    assert results["freelancer"]["empirical_quantile"]["width"] > 3 * results["stable_worker"]["empirical_quantile"]["width"]
