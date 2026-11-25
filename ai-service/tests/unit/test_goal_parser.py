"""Đặt mục tiêu tiết kiệm bằng chat: nhận diện hẹp, số tiền, tên, và cách hiểu hạn ("2 tháng sau", "tháng sau"...).

"Hôm nay" cố định là 17/10/2026 15:30 giờ Việt Nam để bảng hạn kiểm được từng ngày cụ thể.
"""

from datetime import datetime

import pytest

from app.services import intent_cues
from app.services.goal_parser import parse_goal
from app.services.transaction_parser import VN, parse_chat

NOW = datetime(2026, 10, 17, 15, 30, tzinfo=VN)
DEFAULT_NAME = "Mục tiêu tiết kiệm"  # bản mặc định trong intent_cues.json (replies.goal_default_name)


def goal(text, now=NOW):
    result = parse_goal(text, now)
    assert result is not None and result.draft is not None, f"{text!r} -> {result}"
    return result.draft


def day(draft):
    return draft.deadline.strftime("%d/%m/%Y %H:%M:%S") if draft.deadline else None


def test_the_two_sentences_from_the_design():
    first = goal("đặt mục tiêu vào tháng tiếp theo tôi sẽ tiết kiệm được hai mươi triệu")
    assert (first.name, first.name_is_default, first.target, day(first)) == (DEFAULT_NAME, True, 20_000_000, "30/11/2026 23:59:59")
    assert first.deadline_text == "tháng tiếp theo"

    second = goal("đặt mục tiêu vào 2 tháng sau tôi sẽ mua được xe máy khoảng 50 triệu")
    assert (second.name, second.name_is_default, second.target, day(second)) == ("Mua xe máy", False, 50_000_000, "17/12/2026 23:59:59")
    assert second.deadline_text == "2 tháng sau"


@pytest.mark.parametrize(
    ("said", "expected"),
    [
        ("2 tháng sau", "17/12/2026"),
        ("hai tháng nữa", "17/12/2026"),
        ("2 tháng tới", "17/12/2026"),
        ("6 tháng nữa", "17/04/2027"),
        ("nửa năm nữa", "17/04/2027"),
        ("1 năm nữa", "17/10/2027"),
        ("tháng sau", "30/11/2026"),  # cuối tháng sau, không phải 1 tháng sau
        ("tháng tới", "30/11/2026"),
        ("tháng tiếp theo", "30/11/2026"),
        ("tuần sau", "24/10/2026"),
        ("3 tuần nữa", "07/11/2026"),
        ("10 ngày nữa", "27/10/2026"),
        ("cuối tháng", "31/10/2026"),
        ("cuối năm", "31/12/2026"),
        ("năm sau", "31/12/2027"),
        ("trước 30/12", "30/12/2026"),
        ("ngày 1/1", "01/01/2027"),  # năm nay đã qua: lấy năm sau
        ("trước 5/3/2027", "05/03/2027"),
    ],
)
def test_how_a_deadline_phrase_is_understood(said, expected):
    draft = goal(f"đặt mục tiêu tiết kiệm 20 triệu {said}")
    assert day(draft) == f"{expected} 23:59:59"
    assert draft.target == 20_000_000  # con số trong cụm hạn không bị đọc thành tiền


def test_a_month_without_the_day_falls_back_to_its_last_day():
    assert day(goal("đặt mục tiêu tiết kiệm 5 triệu 1 tháng sau", datetime(2026, 8, 31, 9, 0, tzinfo=VN))) == "30/09/2026 23:59:59"
    assert day(goal("đặt mục tiêu tiết kiệm 5 triệu tháng sau", datetime(2026, 1, 31, 9, 0, tzinfo=VN))) == "28/02/2026 23:59:59"


@pytest.mark.parametrize(
    ("text", "name"),
    [
        ("tạo mục tiêu đi du lịch Đà Nẵng 15 triệu trong 6 tháng nữa", "Đi du lịch Đà Nẵng"),
        ("đặt mục tiêu mua laptop 25 triệu trước 30/12", "Mua laptop"),
        ("đặt mục tiêu mua điện thoại 20 triệu cuối năm", "Mua điện thoại"),
        ("muốn tiết kiệm 30 triệu để mua xe máy trong 6 tháng nữa", "Mua xe máy"),
        ("để dành 5 triệu để đi du lịch tháng sau", "Đi du lịch"),
        ("tạo mục tiêu tiết kiệm 10 triệu", DEFAULT_NAME),
    ],
)
def test_goal_name(text, name):
    assert goal(text).name == name


@pytest.mark.parametrize(
    ("text", "deadline", "said"),
    [
        ("đặt mục tiêu mua nhà khoảng 1 tỷ trong 3 năm", "17/10/2029 23:59:59", "trong 3 năm"),  # lần thử thật: bị đọc thành tên "Mua 3 năm" không hạn
        ("đặt mục tiêu mua nhà 1 tỷ trong vòng 36 tháng", "17/10/2029 23:59:59", "trong vòng 36 tháng"),
        ("đặt mục tiêu mua nhà 1 tỷ sau 3 năm", "17/10/2029 23:59:59", "sau 3 năm"),
        ("đặt mục tiêu mua nhà 1 tỷ kỳ hạn 2 năm", "17/10/2028 23:59:59", "kỳ hạn 2 năm"),
        ("đặt mục tiêu mua nhà 1 tỷ trong 6 tháng", "17/04/2027 23:59:59", "trong 6 tháng"),
        ("đặt mục tiêu mua nhà 1 tỷ 3 năm nữa", "17/10/2029 23:59:59", "3 năm nữa"),  # cách nói cũ vẫn hiểu
    ],
)
def test_a_span_of_time_counted_from_today_is_a_deadline(text, deadline, said):
    draft = goal(text)

    assert (draft.name, draft.target, day(draft), draft.deadline_text) == ("Mua nhà", 1_000_000_000, deadline, said)


@pytest.mark.parametrize(
    ("text", "deadline", "said"),
    [
        ("đặt mục tiêu mua nhà 2 tỷ 3 năm rưỡi nữa", "17/04/2030 23:59:59", "3 năm rưỡi nữa"),  # "2 tỷ 3" không được đọc thành 2,3 tỷ
        ("đặt mục tiêu mua xe 600 triệu một năm rưỡi nữa", "17/04/2028 23:59:59", "một năm rưỡi nữa"),
        ("đặt mục tiêu mua nhà 1 tỷ đến năm 2029", "31/12/2029 23:59:59", "năm 2029"),
        ("đặt mục tiêu mua nhà 1 tỷ đến cuối năm 2029", "31/12/2029 23:59:59", "cuối năm 2029"),  # trước đây đọc thành cuối năm NAY và tự tạo sai
        ("đặt mục tiêu mua xe 500 triệu trước tháng 6 năm 2028", "30/06/2028 23:59:59", "tháng 6 năm 2028"),
        ("đặt mục tiêu mua xe 500 triệu quý 2 năm sau", "30/06/2027 23:59:59", "quý 2 năm sau"),  # trước đây đọc thành "2 năm sau" và tự tạo sai
        ("đặt mục tiêu mua xe 500 triệu quý 4 năm 2027", "31/12/2027 23:59:59", "quý 4 năm 2027"),
        ("đặt mục tiêu mua xe 500 triệu cuối năm nay", "31/12/2026 23:59:59", "cuối năm nay"),
    ],
)
def test_absolute_and_half_year_deadlines(text, deadline, said):
    draft = goal(text)

    assert (day(draft), draft.deadline_text) == (deadline, said)
    assert draft.target in (500_000_000, 600_000_000, 1_000_000_000, 2_000_000_000)


def test_a_past_absolute_year_or_month_is_refused_not_guessed():
    for text in ("đặt mục tiêu mua nhà 1 tỷ đến năm 2020", "đặt mục tiêu mua xe 500 triệu trước tháng 6 năm 2025"):
        result = parse_goal(text, NOW)
        assert result is not None and result.draft is None and "tương lai" in result.reply, text


def test_unaccented_nha_is_the_house_in_the_middle_and_the_particle_only_at_the_end():
    assert goal("dat muc tieu mua nha 1 ty trong 3 nam").name == "Mua nha"
    assert goal("dat muc tieu mua xe may 50 trieu 2 thang sau nha").name == "Mua xe may"


def test_the_word_nha_is_a_stopword_only_when_it_is_the_particle_not_the_house():
    # bỏ dấu thì "nhà" trùng "nha" (từ đệm cuối câu): chữ có dấu "nhà" là tên mục tiêu, "nha"/"nhé" không dấu hoặc có dấu đúng dạng thì là từ đệm
    assert goal("đặt mục tiêu mua nhà 500 triệu 2 tháng sau").name == "Mua nhà"
    assert goal("đặt mục tiêu mua xe máy 50 triệu 2 tháng sau nhé").name == "Mua xe máy"
    assert goal("dat muc tieu mua xe may 50 trieu 2 thang sau nha").name == "Mua xe may"
    assert goal("đặt mục tiêu mua nhà cho cô 500 triệu 2 tháng sau").name == "Mua nhà cô"


def test_no_deadline_is_allowed():
    draft = goal("tạo mục tiêu tiết kiệm 10 triệu")
    assert (draft.deadline, draft.deadline_text) == (None, None)


def test_spoken_numbers_work_for_amount_and_for_months():
    draft = goal("đặt mục tiêu vào hai tháng sau tôi sẽ mua xe máy khoảng năm mươi triệu")
    assert (draft.name, draft.target, day(draft)) == ("Mua xe máy", 50_000_000, "17/12/2026 23:59:59")


@pytest.mark.parametrize(
    ("text", "reply_has"),
    [
        ("đặt mục tiêu tiết kiệm", "bao nhiêu"),
        ("đặt mục tiêu tiết kiệm 20 tháng sau", "bao nhiêu"),  # 20 là hạn, không phải tiền
        ("đặt mục tiêu tiết kiệm hai mươi", "đơn vị"),  # số nhỏ không đơn vị: không đoán
        ("đặt mục tiêu tiết kiệm 10 triệu tháng trước", "tương lai"),
        ("đặt mục tiêu tiết kiệm 10 triệu hôm qua", "tương lai"),
        ("đặt mục tiêu tiết kiệm 10 triệu trước 1/10/2026", "tương lai"),
    ],
)
def test_unclear_goal_is_asked_back_not_guessed(text, reply_has):
    result = parse_goal(text, NOW)
    assert result.draft is None and reply_has in result.reply


@pytest.mark.parametrize(
    "text",
    [
        "muốn tiết kiệm bớt tiền ăn",  # cách nói yếu mà không có số tiền thì không phải đặt mục tiêu
        "chuyển hai triệu sang tiết kiệm",
        "gửi tiết kiệm năm triệu",
        "mua xe máy 50 triệu",
        "tháng sau tôi sẽ mua xe máy 50 triệu",
        "tháng này tôi tiết kiệm được bao nhiêu",
        "ăn phở 45k",
    ],
)
def test_ordinary_sentences_are_not_goals(text):
    assert parse_goal(text, NOW) is None


def test_the_chat_pipeline_returns_a_goal_not_a_transfer_even_when_classifier_says_transfer():
    nlu = {"intent": "transfer_money", "confidence": 0.99, "entities": {"amount": "hai mươi triệu"}}
    parsed = parse_chat("đặt mục tiêu vào tháng tiếp theo tôi sẽ tiết kiệm được hai mươi triệu", nlu, now=NOW)
    assert parsed.intent == "create_goal" and parsed.entries == () and parsed.entry is None
    assert (parsed.goal.target, day(parsed.goal)) == (20_000_000, "30/11/2026 23:59:59")


def test_a_goal_question_in_the_pipeline_asks_back_without_a_draft():
    parsed = parse_chat("đặt mục tiêu tiết kiệm", None, now=NOW)
    assert parsed.intent == "create_goal" and parsed.goal is None and "bao nhiêu" in parsed.reply_text


def test_goal_replies_and_default_name_come_from_the_active_vocabulary():
    """Câu hỏi lại và tên mặc định nằm trong intent_cues.json, nên bản admin kích hoạt đổi được chúng ngay mà không cần khởi động lại."""
    default = intent_cues.default_data()
    assert default["replies"]["goal_default_name"] == DEFAULT_NAME
    edited = {**default, "replies": {**default["replies"], "goal_default_name": "Quỹ của tôi", "goal_ask_amount": "Bao nhiêu vậy?",
                                       "goal_needs_unit": "Thiếu đơn vị.", "goal_past_deadline": "Hạn đã qua."}}
    active = intent_cues.current()
    intent_cues.apply(intent_cues.build(edited, "test"))
    try:
        assert goal("tạo mục tiêu tiết kiệm 10 triệu").name == "Quỹ của tôi"
        assert parse_goal("đặt mục tiêu tiết kiệm", NOW).reply == "Bao nhiêu vậy?"
        assert parse_goal("đặt mục tiêu tiết kiệm hai mươi", NOW).reply == "Thiếu đơn vị."
        assert parse_goal("đặt mục tiêu tiết kiệm 10 triệu trước hôm qua", NOW).reply == "Hạn đã qua."
    finally:
        intent_cues.apply(active)
