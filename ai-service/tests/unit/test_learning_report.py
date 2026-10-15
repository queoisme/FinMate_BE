"""Báo cáo mức sẵn sàng của dữ liệu học: đếm mẫu, đủ ngưỡng chưa, luật hiện tại và từng nguồn dự đoán đúng bao nhiêu so với nhãn người dùng."""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts"))

from learning_report import MIN_LABELED, MIN_MERCHANTS, SampleRow, summarize  # noqa: E402


def row(merchant="cua hang a", lines=("Gao thom 5kg",), final="food", source=None, predicted=None) -> SampleRow:
    return SampleRow(merchant_key=merchant, lines=list(lines), final_slug=final, predicted_source=source, predicted_slug=predicted)


def enough_rows() -> list[SampleRow]:
    """Đúng ngưỡng: MIN_LABELED mẫu, MIN_MERCHANTS cửa hàng, 3 danh mục mỗi cái đủ 20 mẫu."""
    slugs = ["food", "transport", "shopping"]
    return [row(merchant=f"cua hang {i % MIN_MERCHANTS}", final=slugs[i % 3]) for i in range(MIN_LABELED)]


def test_report_counts_labeled_and_pending():
    report = summarize([row(final="food"), row(final="food", merchant="b"), row(final="transport", merchant="c")], pending=4)
    assert (report.labeled, report.pending, report.merchants) == (3, 4, 3)
    assert report.per_slug == {"food": 2, "transport": 1}


def test_merchants_counts_distinct_and_ignores_missing():
    report = summarize([row(merchant="a"), row(merchant="a"), row(merchant=None)], pending=0)
    assert report.merchants == 1


def test_ready_false_below_thresholds_lists_what_is_missing():
    report = summarize([row(final="food")] * 5, pending=0)
    assert report.ready is False
    assert any(f"{MIN_LABELED - 5} mẫu" in m for m in report.missing)  # nói rõ thiếu bao nhiêu
    assert any("cửa hàng" in m for m in report.missing)
    assert any("danh mục" in m for m in report.missing)


def test_ready_true_when_all_thresholds_met():
    report = summarize(enough_rows(), pending=0)
    assert report.ready is True and report.missing == []


def test_ready_false_when_one_category_dominates():
    report = summarize([row(merchant=f"cua hang {i % MIN_MERCHANTS}", final="food") for i in range(MIN_LABELED)], pending=0)
    assert report.ready is False
    assert any("danh mục" in m for m in report.missing)


def test_rule_accuracy_none_without_labels():
    assert summarize([], pending=3).rule_accuracy is None


def test_rule_accuracy_matches_hand_computed():
    rows = [
        row(merchant="highlands coffee", lines=["Ca phe sua"], final="food"),  # luật đoán đúng (thương hiệu)
        row(merchant="grab", lines=["Chuyen xe"], final="transport"),  # đúng
        row(merchant="grab", lines=["Chuyen xe"], final="shopping"),  # luật đoán transport, người dùng chọn shopping: sai
        row(merchant="zzz khong ro", lines=["xyz qwerty"], final="food"),  # luật không biết: tính là sai
    ]
    assert summarize(rows, pending=0).rule_accuracy == 0.5


def test_accuracy_by_source_separates_ai_from_rules():
    rows = [
        row(final="food", source="ai", predicted="food"),
        row(final="food", source="ai", predicted="shopping"),
        row(final="food", source="brand", predicted="food"),
        row(final="food", source="brand", predicted="food"),
        row(final="food", source="brand", predicted="food"),
    ]
    by_source = summarize(rows, pending=0).accuracy_by_source
    assert by_source["ai"] == (2, 0.5)
    assert by_source["brand"] == (3, 1.0)


def test_accuracy_by_source_omits_sources_without_labeled_samples():
    rows = [row(final="food", source="ai", predicted="food"), row(final="food", source=None, predicted=None)]
    assert set(summarize(rows, pending=0).accuracy_by_source) == {"ai"}


def test_samples_without_a_prediction_are_not_a_source():
    # "none" = hệ thống không đoán được gì; không phải một nguồn dự đoán có độ đúng 0%
    rows = [row(final="food", source="none", predicted=None), row(final="food", source="ai", predicted="food")]
    assert set(summarize(rows, pending=0).accuracy_by_source) == {"ai"}
