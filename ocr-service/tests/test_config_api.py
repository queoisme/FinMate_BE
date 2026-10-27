"""Từ vựng và ngưỡng đọc hóa đơn do admin kích hoạt (tài liệu cấu hình của AI Service) được gắn vào dịch vụ đang chạy mà không cần build lại. Chạy bằng AI Service giả (xem fake_ai_api.py)."""
import copy
import pathlib
import sys

import pytest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))

from classification import data_files  # noqa: E402
from parsing import extract, items, totals  # noqa: E402
from storage import config_api  # noqa: E402
from tests.fake_ai_api import FakeAiApi  # noqa: E402

LINE_CONFIDENCE = "parsing.extract._MIN_LINE_CONFIDENCE"
MIN_AMOUNT = "parsing.extract._MIN_AMOUNT"
RECEIPT_VOCAB = "parsing.extract._RECEIPT_VOCAB"
DIGITS = "parsing.extract._DIGITS"


@pytest.fixture
def api():
    return FakeAiApi()


@pytest.fixture(autouse=True)
def back_to_defaults():
    """Trạng thái đã gắn là toàn cục của tiến trình: mỗi ca bắt đầu và kết thúc ở bản mặc định trong data/."""
    config_api.apply(None, None)
    config_api._applied.clear()
    config_api._failed = None
    yield
    config_api.apply(None, None)
    config_api._applied.clear()
    config_api._failed = None


def vocab() -> dict:
    return copy.deepcopy(data_files.load("parsing_vocab.json"))


def thresholds() -> dict:
    return copy.deepcopy(data_files.load("thresholds.json"))


def activate(api, name, version, content):
    api.documents[name] = (version, content)


def deactivate_all(api):
    api.documents.clear()


def test_no_document_in_the_database_changes_nothing(api):
    before = (extract._MIN_AMOUNT, extract._RECEIPT_VOCAB, extract._MIN_LINE_CONFIDENCE)
    assert config_api.reload(api) == {}
    assert (extract._MIN_AMOUNT, extract._RECEIPT_VOCAB, extract._MIN_LINE_CONFIDENCE) == before


def test_an_active_threshold_document_reaches_every_module_that_uses_it(api):
    content = thresholds()
    content[MIN_AMOUNT]["value"] = 5000
    content[MIN_AMOUNT]["samples"], content[MIN_AMOUNT]["source"] = 40, "đo lại trên 40 ảnh"
    content[LINE_CONFIDENCE]["value"] = 0.55
    activate(api, "thresholds", 3, content)
    assert config_api.reload(api) == {"thresholds": 3}  # chỉ tài liệu thực sự đổi được báo; từ vựng vẫn là bản mặc định
    assert extract._MIN_AMOUNT == 5000 and extract._MIN_LINE_CONFIDENCE == 0.55
    # hai mô-đun khác từng import thẳng _MIN_AMOUNT: giờ đọc qua extract nên thấy giá trị mới
    assert items.extract._MIN_AMOUNT == 5000 and totals.extract._MIN_AMOUNT == 5000


def test_a_new_threshold_changes_what_the_pipeline_decides(api):
    lines = ["Tong 3,000", "Tien 3,000"]  # hai dòng có số tiền, mỗi số 3.000
    before = extract.receipt_score(lines)
    content = thresholds()
    content[MIN_AMOUNT]["value"] = 5000
    content[MIN_AMOUNT]["samples"], content[MIN_AMOUNT]["source"] = 40, "đo lại trên 40 ảnh"
    activate(api, "thresholds", 1, content)
    config_api.reload(api)
    assert extract.receipt_score(lines) < before  # 3.000 không còn là một số tiền đáng kể nên điểm "giống hóa đơn" tụt


def test_a_vocabulary_entry_added_by_the_admin_is_used_straight_away(api):
    lines = ["tuvungmoia tuvungmoib"]
    before = extract.receipt_score(lines)
    content = vocab()
    content[RECEIPT_VOCAB]["value"] = content[RECEIPT_VOCAB]["value"] + ["tuvungmoia", "tuvungmoib"]
    activate(api, "parsing_vocab", 1, content)
    assert "parsing_vocab" in config_api.reload(api)
    assert "tuvungmoia" in extract._RECEIPT_VOCAB and extract.receipt_score(lines) > before
    assert isinstance(extract._RECEIPT_VOCAB, tuple)  # đúng KIỂU như bản viết cứng trước đây


def test_a_value_derived_from_a_table_follows_the_new_table(api):
    content = vocab()
    content[DIGITS]["value"] = {**content[DIGITS]["value"], "motx": 1}
    activate(api, "parsing_vocab", 1, content)
    config_api.reload(api)
    assert "motx" in extract._number_words()  # tập từ chỉ số được dựng lại, không giữ bản cũ


def test_a_broken_document_is_skipped_and_the_running_values_are_kept(api):
    content = vocab()
    content[RECEIPT_VOCAB]["type"] = "khong_biet"
    activate(api, "parsing_vocab", 1, content)
    before = extract._RECEIPT_VOCAB
    assert config_api.reload(api) == {}
    assert extract._RECEIPT_VOCAB is before
    assert config_api._failed == {"parsing_vocab": 1, "thresholds": 0, "app_screens": 0}
    assert config_api.reload(api) == {}  # cùng phiên bản hỏng thì không thử và không ghi lỗi lại


def test_an_unknown_key_cancels_the_whole_load_instead_of_applying_half(api):
    content = thresholds()
    content[MIN_AMOUNT]["value"] = 5000
    content["parsing.extract._KHONG_CO"] = {"value": 1, "doc": "", "samples": None, "source": "chưa ghi nguồn đo"}
    activate(api, "thresholds", 1, content)
    assert config_api.reload(api) == {}
    assert extract._MIN_AMOUNT != 5000  # giá trị hợp lệ cùng tài liệu cũng không được áp dụng nửa chừng


def test_a_key_missing_from_the_database_document_falls_back_to_the_default(api):
    content = vocab()
    removed = next(k for k in content if k != RECEIPT_VOCAB)
    default_value = data_files.build_table(removed, content[removed])
    del content[removed]
    activate(api, "parsing_vocab", 1, content)
    config_api.reload(api)
    module_name, attribute = removed.rsplit(".", 1)
    assert getattr(sys.modules[module_name], attribute) == default_value


def test_removing_the_active_document_returns_the_service_to_the_defaults(api):
    content = thresholds()
    content[MIN_AMOUNT]["value"] = 5000
    content[MIN_AMOUNT]["samples"], content[MIN_AMOUNT]["source"] = 40, "đo lại trên 40 ảnh"
    activate(api, "thresholds", 1, content)
    config_api.reload(api)
    assert extract._MIN_AMOUNT == 5000
    deactivate_all(api)
    assert config_api.reload(api) == {"thresholds": 0}  # tài liệu ngưỡng quay về mặc định (version 0)
    assert extract._MIN_AMOUNT == data_files.load("thresholds.json")[MIN_AMOUNT]["value"]
    assert data_files.threshold(MIN_AMOUNT) == extract._MIN_AMOUNT  # cả nơi nạp về sau cũng thấy bản mặc định


def test_the_reload_config_endpoint_needs_the_admin_token_and_applies_immediately(api, monkeypatch):
    import main
    from fastapi.testclient import TestClient

    client = TestClient(main.app)
    monkeypatch.delenv("FINMATE_ADMIN_TOKEN", raising=False)
    assert client.post("/admin/reload-config", headers={"x-admin-token": "x"}).status_code == 403  # chưa đặt khóa thì tắt hẳn
    monkeypatch.setenv("FINMATE_ADMIN_TOKEN", "k" * 32)
    assert client.post("/admin/reload-config").status_code == 403
    assert client.post("/admin/reload-config", headers={"x-admin-token": "sai"}).status_code == 403

    content = thresholds()
    content[MIN_AMOUNT]["value"] = 5000
    content[MIN_AMOUNT]["samples"], content[MIN_AMOUNT]["source"] = 40, "đo lại trên 40 ảnh"
    activate(api, "thresholds", 1, content)
    monkeypatch.setattr(main, "config_api_client", api)
    response = client.post("/admin/reload-config", headers={"x-admin-token": "k" * 32})
    assert response.status_code == 200 and response.json() == {"applied": {"thresholds": 1}}
    assert extract._MIN_AMOUNT == 5000


RIDE = ["16:55", "Mã chuyến đi", "Total", "32.000đ"]


def screens() -> dict:
    return copy.deepcopy(data_files.load("app_screens.json"))


def test_an_activated_app_screens_document_changes_what_parse_recognises_without_a_restart(api):
    from parsing import app_screens

    assert app_screens.parse(RIDE)["screen_type"] == "ride"
    content = screens()
    content["ride"]["anchor_phrase"] = "ma cuoc di moi"  # app đổi chữ "Mã chuyến đi" thành "Mã cuốc đi mới"
    activate(api, "app_screens", 4, content)
    assert config_api.reload(api) == {"app_screens": 4}
    assert app_screens.parse(RIDE) is None  # nhãn cũ không còn được nhận
    assert app_screens.parse(["16:55", "Mã cuốc đi mới", "Total", "32.000đ"])["screen_type"] == "ride"
    assert data_files.screens_document()["ride"]["anchor_phrase"] == "ma cuoc di moi"  # nơi nạp về sau cũng thấy bản mới


def test_removing_the_active_app_screens_document_returns_to_the_shipped_wording(api):
    from parsing import app_screens

    content = screens()
    content["ride"]["anchor_phrase"] = "ma cuoc di moi"
    activate(api, "app_screens", 1, content)
    config_api.reload(api)
    assert app_screens.parse(RIDE) is None
    deactivate_all(api)
    assert config_api.reload(api) == {"app_screens": 0}
    assert app_screens.parse(RIDE)["screen_type"] == "ride"


def test_a_screen_missing_from_the_database_document_falls_back_to_the_shipped_one(api):
    from parsing import app_screens

    content = screens()
    del content["order_list"]  # ai-service chặn bản như vậy trước khi lưu; ở đây kiểm tra lớp dự phòng của OCR (giống các tài liệu khác)
    activate(api, "app_screens", 1, content)
    config_api.reload(api)
    assert app_screens.parse(RIDE)["screen_type"] == "ride"
    assert app_screens._SPEC["order_list"]["completed_prefix"] == "hoan thanh"


def test_a_second_reload_with_nothing_new_does_nothing(api):
    content = thresholds()
    content[MIN_AMOUNT]["value"] = 5000
    content[MIN_AMOUNT]["samples"], content[MIN_AMOUNT]["source"] = 40, "đo lại trên 40 ảnh"
    activate(api, "thresholds", 1, content)
    assert config_api.reload(api)
    assert config_api.reload(api) == {}
