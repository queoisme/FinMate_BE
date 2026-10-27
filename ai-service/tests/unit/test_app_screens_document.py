"""Tài liệu ``app_screens`` (chữ nhận dạng màn hình app của ocr-service): bản nháp sai bị chặn trước khi chạm vào OCR. OCR tự nạp bản đã kích hoạt từ DB
(ocr-service/storage/config_pg.py); ai-service chỉ kiểm tra và lưu phiên bản."""
import copy

import pytest

from app.bill import lexicon
from app.bill.rules_admin import RuleError
from app.services import config_documents as docs

SPEC = docs.spec_of("app_screens")


def screens() -> dict:
    return copy.deepcopy(lexicon.load("app_screens.json"))


def rejected(content, code):
    with pytest.raises(RuleError) as info:
        SPEC.validate(content)
    assert info.value.status == 422 and info.value.code == code, info.value.message


def test_the_shipped_document_passes_its_own_validation_and_is_not_previewable_here():
    SPEC.validate(screens())
    assert SPEC.apply is None and SPEC.preview is None and SPEC.consumers == ("ocr-service",)


def test_a_screen_or_key_the_code_reads_cannot_be_removed():
    removed = screens()
    del removed["ride"]
    rejected(removed, "missing_keys")
    nested = screens()
    del nested["food_delivery"]["grab"]["order_code_regex"]
    rejected(nested, "missing_keys")


def test_a_key_the_code_does_not_read_is_rejected_instead_of_silently_ignored():
    content = screens()
    content["ride"]["khoa_moi"] = "x"
    rejected(content, "unknown_key")
    content = screens()
    content["man_hinh_moi"] = {"doc": "x"}
    rejected(content, "unknown_key")


def test_a_value_cannot_change_type():
    content = screens()
    content["ride"]["total_span"] = "3"
    rejected(content, "type_changed")
    content = screens()
    content["order_list"]["completed_prefix"] = ["hoan thanh"]
    rejected(content, "type_changed")


def test_a_list_cannot_be_emptied_and_its_items_keep_their_shape():
    content = screens()
    content["cinema_ticket"]["chains"] = []
    rejected(content, "invalid_value")
    content = screens()
    del content["cinema_ticket"]["chains"][0]["name"]
    rejected(content, "missing_keys")


@pytest.mark.parametrize("phrase", ["Nhà cung cấp", "nhà cung cấp", "NHA CUNG CAP", "  "])
def test_a_phrase_must_be_lowercase_and_unaccented(phrase):
    content = screens()
    content["service_payment"]["provider_prefix"] = phrase
    rejected(content, "invalid_phrase")


def test_a_regex_must_compile_and_capture_the_amount():
    content = screens()
    content["order_list"]["total_regex"] = "san pham (chua dong"
    rejected(content, "invalid_regex")
    content = screens()
    content["order_list"]["total_regex"] = "san pham [\\d.,]+"  # biên dịch được nhưng không có nhóm bắt số tiền
    rejected(content, "invalid_regex")


def test_a_category_must_be_one_of_the_nine_groups_and_a_span_a_small_integer():
    content = screens()
    content["ride"]["category"] = "food"  # slug backend, không phải nhóm hóa đơn
    rejected(content, "invalid_category")
    for value, code in ((0, "invalid_value"), (31, "invalid_value"), (True, "type_changed")):
        content = screens()
        content["ride"]["total_span"] = value
        rejected(content, code)


def test_an_amount_range_must_be_ordered():
    content = screens()
    content["cinema_ticket"]["amount_min"], content["cinema_ticket"]["amount_max"] = 5_000_000, 10_000
    rejected(content, "invalid_value")


def test_a_wording_change_that_keeps_the_shape_is_accepted():
    content = screens()
    content["service_payment"]["provider_prefix"] = "don vi cung cap"
    content["cinema_ticket"]["chains"].append({"match": "mega gs", "whole_word": False, "name": "Mega GS"})
    content["ride"]["doc"] = "Chi tiết chuyến xe (đã sửa lời mô tả)."
    SPEC.validate(content)
