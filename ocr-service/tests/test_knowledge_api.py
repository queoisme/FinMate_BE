"""Kiến thức nhận diện (thương hiệu, từ khóa) lấy từ AI Service, và ghi bill lạ qua AI Service. Chạy bằng AI Service giả (xem fake_ai_api.py)."""
import io
import pathlib
import sys

import pytest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))

from classification import brands  # noqa: E402
from parsing import extract  # noqa: E402
from storage import knowledge_api  # noqa: E402
import main  # noqa: E402
from fastapi.testclient import TestClient  # noqa: E402
from storage.ai_api import AiApiError  # noqa: E402
from storage.knowledge_api import ApiUnknownScans, NoUnknownScans  # noqa: E402
from PIL import Image  # noqa: E402
from tests.conftest import boxes_patch  # noqa: E402
from tests.fake_ai_api import FakeAiApi  # noqa: E402

client = TestClient(main.app)


@pytest.fixture
def api():
    return FakeAiApi()


def _pending(api):
    """Bill lạ đang chờ xem (status = 'new'), thấy nhiều lần nhất trước. Đọc thẳng trạng thái của AI Service giả: ocr-service chỉ ghi, người duyệt xem hàng đợi ở AI Service."""
    return api.pending_unknown()


@pytest.fixture(autouse=True)
def restore_knowledge():
    """reload/apply sửa các danh sách toàn cục tại chỗ: trả lại nguyên trạng sau mỗi test."""
    snap = (dict(brands.BRAND_CATEGORIES), dict(brands.BRAND_DISPLAY), set(brands.WHOLE_WORD_BRANDS),
            dict(extract._CATEGORY_KEYWORDS), dict(extract._PRODUCT_KEYWORDS))
    yield
    for target, saved in zip((brands.BRAND_CATEGORIES, brands.BRAND_DISPLAY, extract._CATEGORY_KEYWORDS, extract._PRODUCT_KEYWORDS),
                             (snap[0], snap[1], snap[3], snap[4])):
        target.clear()
        target.update(saved)
    brands.WHOLE_WORD_BRANDS.clear()
    brands.WHOLE_WORD_BRANDS.update(snap[2])


def test_seed_then_load_reproduces_exactly_what_is_in_code(api):
    added = knowledge_api.seed(api)
    assert added["brands"] == len(brands.BRAND_CATEGORIES) and added["keywords"] > 100
    loaded = knowledge_api.load(api)
    assert {k: v[0] for k, v in loaded["brands"].items()} == brands.BRAND_CATEGORIES
    assert {k for k, v in loaded["brands"].items() if v[2]} == brands.WHOLE_WORD_BRANDS
    assert list(loaded["merchant"]) == list(extract._CATEGORY_KEYWORDS)  # thứ tự ưu tiên giữa các nhóm được giữ
    assert {c: set(w) for c, w in loaded["merchant"].items()} == {c: set(w) for c, w in extract._CATEGORY_KEYWORDS.items()}
    assert {c: set(w) for c, w in loaded["product"].items()} == {c: set(w) for c, w in extract._PRODUCT_KEYWORDS.items()}


def test_loading_from_db_does_not_change_any_classification(api):
    samples = [("Highlands Coffee", ["Bac xiu 45.000"]), ("Nike", []), ("Cua hang Linh", ["Quan jean nam 500.000", "Tai nghe 300.000"]),
               ("Nha tro Hoa Binh", ["Tien dien 300.000"]), (None, ["Gao ST25 5kg", "Dau an 1L"])]
    before = [extract.classify_category_detailed(m, l) for m, l in samples]
    knowledge_api.seed(api)
    assert knowledge_api.reload(api)
    assert [extract.classify_category_detailed(m, l) for m, l in samples] == before


def test_editing_the_database_changes_recognition_without_touching_code(api):
    knowledge_api.seed(api)
    assert extract.classify_category_detailed("Tiem Xyz Moi", [])[0] == "other"
    api.brands["tiem xyz moi"] = {"category_slug": "shopping", "display_name": "Tiệm Xyz Mới", "whole_word": False, "is_active": True, "source": "review"}
    api.brands["highlands coffee"]["is_active"] = False
    api.keywords.append({"kind": "product", "category_slug": "dining", "keyword": "mon an moi la", "category_priority": 20, "is_active": True})
    knowledge_api.reload(api)
    assert extract.classify_category_detailed("Tiem Xyz Moi", [])[:2] == ("shopping", "brand")
    assert brands.BRAND_DISPLAY["tiem xyz moi"] == "Tiệm Xyz Mới"
    assert "highlands coffee" not in brands.BRAND_CATEGORIES  # đã tắt trong DB
    assert extract.classify_category_detailed(None, ["Mon an moi la 50.000"])[0] == "dining"


def test_seeding_again_never_overwrites_edits_made_in_the_database(api):
    knowledge_api.seed(api)
    api.brands["highlands coffee"]["category_slug"] = "shopping"
    assert knowledge_api.seed(api) == {"brands": 0, "keywords": 0}  # không thêm bản trùng
    assert knowledge_api.load(api)["brands"]["highlands coffee"][0] == "shopping"


def test_seed_copies_the_default_files_even_when_the_live_tables_were_changed(api):
    # Bảng trong bộ nhớ có thể đã bị DB thay (apply); seed phải luôn lấy từ data/*.json, nếu không bản "mặc định" trôi theo DB.
    del brands.BRAND_CATEGORIES["highlands coffee"]
    brands.BRAND_CATEGORIES["thuong hieu chi co trong bo nho"] = "dining"
    knowledge_api.seed(api)
    loaded = knowledge_api.load(api)["brands"]
    assert "highlands coffee" in loaded and "thuong hieu chi co trong bo nho" not in loaded


def test_a_new_default_entry_reaches_a_database_seeded_earlier(api):
    knowledge_api.seed(api)
    assert knowledge_api.missing(api) == {"brands": [], "keywords": []}
    del api.brands["vexere"]  # AI Service dựng từ trước khi file có mục này
    api.keywords[:] = [k for k in api.keywords if not (k["kind"] == "product" and k["keyword"] == "bun bo")]
    plan = knowledge_api.missing(api)
    assert plan["brands"] == ["vexere"] and ("product", "dining", "bun bo") in plan["keywords"]
    assert knowledge_api.seed(api) == {"brands": 1, "keywords": 1}
    assert knowledge_api.missing(api) == {"brands": [], "keywords": []}


def test_an_entry_switched_off_in_the_database_is_not_brought_back_by_the_seed(api):
    knowledge_api.seed(api)
    api.brands["highlands coffee"]["is_active"] = False
    assert knowledge_api.missing(api) == {"brands": [], "keywords": []}  # tắt = AI Service đã quyết, không phải thiếu
    assert knowledge_api.seed(api) == {"brands": 0, "keywords": 0}
    knowledge_api.reload(api)
    assert "highlands coffee" not in brands.BRAND_CATEGORIES


def test_a_keyword_moved_to_another_category_in_the_database_is_not_added_back(api):
    # Migration 0008/0012 của ai-service chuyển từ khóa giữa các nhóm: thêm lại dòng cũ sẽ khiến một từ nằm ở hai nhóm.
    knowledge_api.seed(api)
    for k in api.keywords:
        if k["kind"] == "product" and k["category_slug"] == "dining" and k["keyword"] == "bun bo":
            k["category_slug"] = "other"
    assert knowledge_api.missing(api) == {"brands": [], "keywords": []}
    assert knowledge_api.seed(api) == {"brands": 0, "keywords": 0}
    assert knowledge_api.load(api)["product"]["dining"].count("bun bo") == 0


def test_empty_database_keeps_the_defaults_in_code(api):
    before = dict(brands.BRAND_CATEGORIES)
    assert knowledge_api.reload(api) is False
    assert brands.BRAND_CATEGORIES == before and extract._CATEGORY_KEYWORDS


def test_unknown_scans_dedupe_count_and_label_status(api):
    scans = ApiUnknownScans(api, "u1")
    scans.record("Tiệm Lạ Hoắc", ["Tiệm Lạ Hoắc", "Mon A 10.000"], 10000, "other", "none", 0.0)
    scans.record("Tiệm Lạ Hoắc", ["Tiệm Lạ Hoắc", "Mon A 10.000"], 10000, "other", "none", 0.0)  # cùng bill quét lại
    scans.record("Quán Mới", ["Quán Mới", "Mon B 20.000"], 20000, "dining", "keyword", 0.75)
    pending = _pending(api)
    assert [p["merchant"] for p in pending] == ["Tiệm Lạ Hoắc", "Quán Mới"] and pending[0]["seen"] == 2
    scans.mark_labeled("Tiệm Lạ Hoắc")  # người dùng đã chốt danh mục
    assert [p["merchant"] for p in _pending(api)] == ["Quán Mới"]


def test_no_database_means_nothing_is_recorded():
    none = NoUnknownScans()
    none.record("X", ["X"], 1, "other", "none", 0.0)
    none.mark_labeled("X")  # không có DB: không ghi gì và không lỗi


# --- qua API ---
def _png() -> bytes:
    img = Image.new("RGB", (60, 60), "white")
    img.paste((0, 0, 0), (5, 5, 55, 9))
    buf = io.BytesIO()
    img.save(buf, format="PNG")
    return buf.getvalue()


def _bill(header: str):
    rows = [header, "HOA DON BAN LE", "Mon A 1 50.000 50.000", "Cam on quy khach", "TONG CONG 50.000", "Tien khach tra 50.000"]
    return lambda _: [(t, 0.95) for t in rows]


def _scan(share: bool):
    return client.post("/analyze-bill", data={"share_data": "true" if share else "false"}, files={"image": ("b.png", _png(), "image/png")})


def test_unknown_bill_is_recorded_only_when_the_user_agreed_to_share(api, monkeypatch):
    scans = ApiUnknownScans(api, "u1")
    monkeypatch.setattr(main, "unknown_scans", scans)
    monkeypatch.setattr(main, "read_boxes", boxes_patch(_bill("TIEM LA HOAC 99")))
    assert _scan(share=False).status_code == 200
    assert _pending(api) == []  # không đồng ý thì không ghi gì
    assert _scan(share=True).status_code == 200
    assert [p["merchant"] for p in _pending(api)] == ["TIEM LA HOAC 99"]


def test_known_brand_is_not_recorded_as_unknown(api, monkeypatch):
    scans = ApiUnknownScans(api, "u1")
    monkeypatch.setattr(main, "unknown_scans", scans)
    monkeypatch.setattr(main, "read_boxes", boxes_patch(_bill("HIGHLANDS COFFEE")))
    assert _scan(share=True).status_code == 200
    assert _pending(api) == []


def test_saving_a_label_closes_the_matching_unknown_scans(api, monkeypatch):
    scans = ApiUnknownScans(api, "u1")
    monkeypatch.setattr(main, "unknown_scans", scans)
    monkeypatch.setattr(main, "read_boxes", boxes_patch(_bill("TIEM LA HOAC 99")))
    _scan(share=True)
    assert len(_pending(api)) == 1
    r = client.post("/merchant-category", json={"merchant": "TIEM LA HOAC 99", "category": "shopping"})
    assert r.status_code == 200
    assert _pending(api) == []


def test_an_ai_service_failure_does_not_break_the_scan(monkeypatch):
    class Broken:
        def record(self, *a, **k):
            raise RuntimeError("DB down")

        def mark_labeled(self, *a):
            pass

    monkeypatch.setattr(main, "unknown_scans", Broken())
    monkeypatch.setattr(main, "read_boxes", boxes_patch(_bill("TIEM LA HOAC 99")))
    assert _scan(share=True).status_code == 200  # người dùng vẫn nhận kết quả quét


def test_reload_endpoint_is_disabled_without_a_token_and_checks_it(monkeypatch):
    monkeypatch.delenv("FINMATE_ADMIN_TOKEN", raising=False)
    assert client.post("/admin/reload-knowledge").status_code == 403
    monkeypatch.setenv("FINMATE_ADMIN_TOKEN", "s3cret")
    monkeypatch.setattr(main, "reload_knowledge", lambda: True)
    assert client.post("/admin/reload-knowledge", headers={"X-Admin-Token": "sai"}).status_code == 403
    r = client.post("/admin/reload-knowledge", headers={"X-Admin-Token": "s3cret"})
    assert r.status_code == 200 and r.json() == {"reloaded": True}


# --- mã người dùng theo từng yêu cầu (header X-User-Hash) ---
def test_each_request_uses_its_own_users_memory_and_unknown_scans(api, monkeypatch):
    from storage.storage_api import ApiDataset, ApiMerchantMemory
    monkeypatch.setattr(main, "memory", ApiMerchantMemory(api, "default" + "0" * 10))
    monkeypatch.setattr(main, "dataset", ApiDataset(api, "default" + "0" * 10))
    monkeypatch.setattr(main, "unknown_scans", ApiUnknownScans(api, "default" + "0" * 10))
    a, b = "a" * 64, "b" * 64
    body = {"merchant": "Tiệm Cô Ba", "category": "dining"}
    assert client.post("/merchant-category", json=body, headers={"X-User-Hash": a}).status_code == 200
    assert main.memory.get("Tiệm Cô Ba") is None  # mã mặc định không thấy dữ liệu của người dùng a
    assert {user for user, _ in api.memory} == {a}
    client.post("/merchant-category", json=body, headers={"X-User-Hash": b})
    assert {user for user, _ in api.memory} == {a, b}


def test_bad_user_hash_header_is_rejected(monkeypatch):
    r = client.post("/merchant-category", json={"merchant": "X Shop", "category": "dining"}, headers={"X-User-Hash": "khong-hop-le!"})
    assert r.status_code == 400


def test_api_startup_survives_an_ai_service_that_is_not_up_yet(monkeypatch):
    class Down(FakeAiApi):
        def knowledge(self, include_inactive=False):
            raise AiApiError(0, "unreachable", "AI Service chưa lên")

    monkeypatch.setattr(knowledge_api, "api_from_env", lambda: Down())
    scans, reload_fn = knowledge_api.open_knowledge()  # không được ném lỗi: dùng bản trong code
    assert isinstance(scans, ApiUnknownScans) and callable(reload_fn)


def test_without_an_api_nothing_is_loaded_or_recorded(monkeypatch):
    monkeypatch.setattr(knowledge_api, "api_from_env", lambda: None)
    scans, reload_fn = knowledge_api.open_knowledge()
    assert isinstance(scans, NoUnknownScans) and reload_fn() is False
