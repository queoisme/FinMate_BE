"""Độ tin cậy của các luật nằm ở app/data/confidence.json và ràng buộc với ngưỡng một chạm của backend được canh ở đây.

Backend chọn nhánh theo min(độ tin cậy trích xuất, độ tin cậy danh mục) so với ngưỡng 0,85: một con số rải trong hàm đổi mà không ai để ý sẽ làm cả một loại khoản chi
rơi sang nhánh khác (đã từng xảy ra khi khớp từ điển còn 0,80, xem categorizer.py)."""
import ast
import pathlib
import re

import pytest

from app.core import confidence

APP = pathlib.Path(__file__).resolve().parents[2] / "app"
ONE_TAP = confidence.get("backend.one_tap_threshold")
CONSUMERS = ("bill/classifier.py", "bill/rules_classify.py", "pipeline/stages/classifier.py", "pipeline/stages/extractor.py",
             "pipeline/stages/categorizer.py", "pipeline/stages/receipt_ocr.py", "pipeline/stages/remote_ocr.py")
NOT_CONFIDENCES = {"pipeline/stages/receipt_ocr.py": {0.84}}  # _FUZZY_THRESHOLD: ngưỡng giống nhau của chữ khi khớp mờ nhãn tổng, tham số thuật toán chứ không phải độ tin cậy
KEY = re.compile(r'"((?:backend|classifier|extractor|categorizer|receipt|remote|bill)\.[a-z_]+)"')


def get(name):
    return confidence.get(name)


def test_every_entry_is_used_by_the_code_and_every_use_has_an_entry():
    used = {key for path in APP.rglob("*.py") if path.name != "confidence.py" for key in KEY.findall(path.read_text(encoding="utf-8"))}
    assert used == set(confidence.VALUES) - {"backend.one_tap_threshold"}  # mục không ai dùng là số chết; dùng khóa sai thì import đã lỗi


@pytest.mark.parametrize("relative", CONSUMERS)
def test_no_confidence_literal_is_left_in_the_functions(relative):
    tree = ast.parse((APP / relative).read_text(encoding="utf-8"))
    stray = sorted({node.value for node in ast.walk(tree) if isinstance(node, ast.Constant) and isinstance(node.value, float) and 0 < node.value < 1} - NOT_CONFIDENCES.get(relative, set()))
    assert not stray, f"{relative}: số {stray} phải nằm ở app/data/confidence.json (kèm lý do), không viết cứng trong code"


# ---------------------------------------------------------------- ràng buộc với ngưỡng một chạm

def test_a_dictionary_hit_and_a_matched_bank_template_can_reach_one_tap():
    assert get("categorizer.lexicon_hit") >= ONE_TAP and get("categorizer.credit_income") >= ONE_TAP
    assert min(get("extractor.pattern_by_filled")) >= ONE_TAP


def test_guesses_and_receipts_always_go_to_the_dialog():
    assert max(get("categorizer.fallback"), get("categorizer.empty_text")) < ONE_TAP
    assert max(get("extractor.generic_with_merchant"), get("extractor.generic_without_merchant")) < ONE_TAP
    assert get("receipt.max") < ONE_TAP  # hóa đơn luôn có bước người xem lại
    assert max(get("bill.max_result"), get("remote.max_category"), get("bill.max_model")) < ONE_TAP


def test_the_receipt_caps_agree_with_each_other():
    assert get("receipt.max") == get("remote.max_category") == get("bill.max_result")
    assert get("remote.review_cap") < get("receipt.max")  # bill tự đánh dấu cần xem lại không được chắc bằng bill bình thường


def test_the_sources_of_a_bill_rule_are_ranked_from_strongest_to_weakest():
    assert get("bill.brand_in_merchant") >= get("bill.keyword_in_merchant") >= get("bill.brand_in_body") >= get("bill.keyword_in_body")
    assert get("bill.product_majority") > get("bill.product_plurality")
    assert get("bill.max_model") < get("bill.product_majority")  # gợi ý của mô hình học không được ngang luật chắc


def test_the_classifier_confidences_are_consistent():
    assert get("classifier.financial_strong") > get("classifier.financial_weak") > get("classifier.uncertain")
    values = get("extractor.pattern_by_filled")
    assert values == sorted(values) and len(values) == 4  # bóc được nhiều trường hơn thì không kém chắc hơn


def test_the_copy_of_the_backend_threshold_matches_the_backend_source():
    source = next((p for p in [APP.parents[1] / "backend" / "FinMate.Application" / "Notifications" / "TransactionReviewPush.cs"] if p.is_file()), None)
    if source is None:
        pytest.skip("không có mã nguồn backend cạnh ai-service (image chỉ chứa ai-service)")
    match = re.search(r"OneTapThreshold\s*=\s*([0-9.]+)", source.read_text(encoding="utf-8"))
    assert match and float(match.group(1)) == ONE_TAP, "ngưỡng backend đã đổi: cập nhật backend.one_tap_threshold và xem lại các ràng buộc ở trên"
