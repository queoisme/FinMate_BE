"""Tài liệu cấu hình sửa từ trang quản trị (app/services/config_documents.py): bản nháp sai bị chặn TRƯỚC khi chạm vào dịch vụ, và bản đúng thay nóng được hành vi thật."""

import copy
from datetime import datetime

import pytest

from app.bill import lexicon
from app.bill.rules_admin import RuleError
from app.services import config_documents as docs
from app.services import intent_cues
from app.services.transaction_parser import VN, parse_chat

NOW = datetime(2026, 10, 21, 10, 0, tzinfo=VN)


def cues() -> dict:
    return copy.deepcopy(intent_cues.default_data())


def vocab() -> dict:
    return copy.deepcopy(lexicon.load("parsing_vocab.json"))


def thresholds() -> dict:
    return copy.deepcopy(lexicon.load("thresholds.json"))


def rejected(validate, content, code):
    with pytest.raises(RuleError) as info:
        validate(content)
    assert info.value.status == 422 and info.value.code == code, info.value.message


# ---------------------------------------------------------------- bản mặc định luôn hợp lệ

@pytest.mark.parametrize("name,make", [("intent_cues", cues), ("parsing_vocab", vocab), ("thresholds", thresholds)])
def test_the_shipped_default_passes_its_own_validation(name, make):
    docs.spec_of(name).validate(make())


def test_an_unknown_document_is_not_found():
    with pytest.raises(RuleError) as info:
        docs.spec_of("khong_co")
    assert info.value.status == 404


# ---------------------------------------------------------------- intent_cues

def test_a_draft_may_add_a_key_but_not_remove_one_the_code_uses():
    content = cues()
    content["patterns"]["moi"] = {"on": "folded", "regex": "xyz", "doc": "thêm khóa mới thì được"}
    docs.spec_of("intent_cues").validate(content)
    removed = cues()
    del removed["patterns"]["fee"]
    rejected(docs.spec_of("intent_cues").validate, removed, "missing_keys")


def test_a_pattern_that_does_not_compile_is_rejected_before_it_can_break_chat():
    content = cues()
    content["patterns"]["fee"]["regex"] = "(?<![a-z])phi (chua dong ngoac"
    rejected(docs.spec_of("intent_cues").validate, content, "invalid_document")


def test_a_reference_to_a_missing_list_is_rejected():
    content = cues()
    content["patterns"]["fee"]["regex"] = "<<danh_sach_khong_co>>"
    rejected(docs.spec_of("intent_cues").validate, content, "invalid_document")


@pytest.mark.parametrize("value", [1.5, -0.1, "0.9", True, None])
def test_a_confidence_threshold_must_be_between_zero_and_one(value):
    content = cues()
    content["thresholds"]["kind_trust"]["value"] = value
    rejected(docs.spec_of("intent_cues").validate, content, "invalid_threshold")


def test_an_empty_reply_is_rejected():
    content = cues()
    content["replies"]["greet"] = "  "
    rejected(docs.spec_of("intent_cues").validate, content, "invalid_reply")


def stored_before_a_key_was_added() -> dict:
    """Tài liệu admin đã lưu trong DB TRƯỚC khi code thêm khóa: có chỉnh sửa của admin, thiếu một mẫu, một danh sách và một câu trả lời."""
    content = cues()
    content["replies"]["greet"] = "Câu chào do admin sửa."
    for section, key in (("patterns", "fee"), ("lists", "fee_kinds"), ("replies", "cancelled")):
        del content[section][key]
    content["patterns"]["moi_cua_admin"] = {"on": "folded", "regex": "xyz", "doc": "khóa admin tự thêm"}
    return content


def test_keys_added_by_a_deploy_are_filled_in_to_a_stored_document_without_losing_the_admin_edits():
    stored = stored_before_a_key_was_added()
    snapshot = copy.deepcopy(stored)
    merged = intent_cues.with_defaults(stored)
    assert stored == snapshot  # không sửa tài liệu đã lưu
    assert merged["patterns"]["fee"] == intent_cues.default_data()["patterns"]["fee"]
    assert merged["lists"]["fee_kinds"] and merged["replies"]["cancelled"]
    assert merged["replies"]["greet"] == "Câu chào do admin sửa." and "moi_cua_admin" in merged["patterns"]
    merged["lists"]["fee_kinds"].append("x")
    assert "x" not in intent_cues.default_data()["lists"]["fee_kinds"]  # bản mặc định không bị chia sẻ tham chiếu


def test_a_stored_document_missing_a_new_key_is_valid_after_the_merge_but_a_fresh_upload_still_is_not():
    spec = docs.spec_of("intent_cues")
    stored = stored_before_a_key_was_added()
    rejected(spec.validate, stored, "missing_keys")  # bản nháp admin tải lên: thiếu khóa vẫn bị từ chối
    spec.validate(docs.stored_content(spec, stored))
    intent_cues.build(docs.stored_content(spec, stored), "DB")


def test_the_merge_leaves_documents_without_an_upgrade_untouched():
    content = vocab()
    assert docs.stored_content(docs.spec_of("parsing_vocab"), content) is content


async def test_the_runtime_applies_an_admin_document_saved_before_a_key_was_added(restore_cues, monkeypatch):
    """Lỗi cũ: tài liệu DB thiếu khóa mới bị bỏ im lặng (config_apply_failed) và tiến trình quay về mặc định dù admin đã kích hoạt."""
    from types import SimpleNamespace

    from app.services import config_runtime

    class Session:
        async def __aenter__(self):
            return self

        async def __aexit__(self, *exc):
            return False

        async def scalar(self, statement):
            return SimpleNamespace(version=7, content=stored_before_a_key_was_added())

    monkeypatch.setattr(docs, "SPECS", {"intent_cues": docs.SPECS["intent_cues"]})
    monkeypatch.setattr(docs, "APPLIED", {})
    assert await config_runtime.refresh(lambda: Session()) == {"intent_cues": 7}
    text = "hôm nay trời đẹp quá"
    assert parse_chat("xin chào", None, now=NOW).reply_text == "Câu chào do admin sửa."
    assert parse_chat(text, None, now=NOW).reply_text  # khóa được bổ sung từ bản mặc định vẫn dùng được


# ---------------------------------------------------------------- parsing_vocab và thresholds (OCR)

def test_the_ocr_vocabulary_cannot_lose_a_table_or_change_its_type():
    spec = docs.spec_of("parsing_vocab")
    key = next(iter(vocab()))
    removed = vocab()
    del removed[key]
    rejected(spec.validate, removed, "missing_keys")
    retyped = vocab()
    retyped[key]["type"] = "dict" if retyped[key]["type"] != "dict" else "set"
    rejected(spec.validate, retyped, "type_changed")
    emptied = vocab()
    emptied[key]["value"] = []
    rejected(spec.validate, emptied, "invalid_value")


def test_the_ocr_vocabulary_may_gain_entries_in_an_existing_table():
    content = vocab()
    key, entry = next((k, v) for k, v in content.items() if v["type"] in ("tuple", "set", "list"))
    entry["value"] = entry["value"] + ["mot-nhan-moi"]
    docs.spec_of("parsing_vocab").validate(content)


def test_a_threshold_must_record_where_its_value_came_from():
    spec = docs.spec_of("thresholds")
    key = "parsing.extract._MIN_LINE_CONFIDENCE"
    content = thresholds()
    content[key]["source"] = " "
    rejected(spec.validate, content, "invalid_source")
    content = thresholds()
    content[key]["samples"] = 0
    rejected(spec.validate, content, "invalid_samples")
    content = thresholds()
    content[key]["value"] = 1.7  # tỉ lệ/độ chắc không thể lớn hơn 1
    rejected(spec.validate, content, "invalid_value")
    content = thresholds()
    content[key]["value"] = 0.8  # đổi giá trị hợp lệ và ghi rõ nguồn thì được
    content[key]["samples"], content[key]["source"] = 40, "đo lại trên 40 ảnh mới"
    spec.validate(content)


# ---------------------------------------------------------------- khác biệt

def test_diff_names_what_was_added_removed_and_changed():
    old, new = {"a": 1, "b": 2, "c": 3}, {"b": 2, "c": 4, "d": 5}
    assert docs.diff(old, new) == {"added": ["d"], "removed": ["a"], "changed": ["c"]}
    assert docs.diff(None, new) == {"added": [], "removed": [], "changed": []}


# ---------------------------------------------------------------- thay nóng và cổng đo

@pytest.fixture
def restore_cues():
    active = intent_cues.current()
    yield
    intent_cues.apply(active)


def test_applying_a_new_document_changes_what_chat_actually_replies(restore_cues):
    text = "hôm nay trời đẹp quá"
    before = parse_chat(text, None, now=NOW).reply_text
    content = cues()
    content["replies"]["out_of_scope"] = "Câu trả lời do admin đổi."
    docs.spec_of("intent_cues").validate(content)
    docs.spec_of("intent_cues").apply(content)
    assert parse_chat(text, None, now=NOW).reply_text == "Câu trả lời do admin đổi." != before
    docs.spec_of("intent_cues").apply(cues())  # đưa về bản mặc định
    assert parse_chat(text, None, now=NOW).reply_text == before


def test_a_vocabulary_edit_changes_the_decision_not_just_the_reply(restore_cues):
    # "bán" là động từ tiền vào; làm nó không khớp nữa thì luật thuần đọc "bán ... được 3 triệu" thành chi tiêu.
    text = "bán chiếc điện thoại cũ 3 triệu"
    assert parse_chat(text, None, now=NOW).intent == "add_income"
    content = cues()
    content["fragments"]["sell"] = "(?!x)x"
    docs.spec_of("intent_cues").apply(content)
    assert parse_chat(text, None, now=NOW).intent == "add_expense"


def test_the_preview_reports_no_change_for_the_active_document():
    result = docs._preview_intent_cues(cues())
    assert result["available"] and result["delta"] == 0 and result["changed_total"] == 0 and result["cases"] > 100


def test_the_preview_flags_a_document_that_makes_chat_worse_and_restores_the_active_one(restore_cues):
    active = intent_cues.current()
    content = cues()
    content["fragments"]["sell"] = "(?!x)x"
    result = docs._preview_intent_cues(content)
    assert result["available"] and result["delta"] < 0 and result["changed_total"] >= 1
    assert result["changed"][0]["before"] != result["changed"][0]["after"]
    assert intent_cues.current() is active  # xem trước chỉ đổi tạm, không để lại dấu vết
