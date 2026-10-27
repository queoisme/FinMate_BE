"""Lớp lưu trữ qua API của AI Service, chạy bằng AI Service giả (xem fake_ai_api.py): kiểm tra phần việc của ocr-service (đổi nhóm <-> slug backend, băm mã người dùng, băm nội dung bill,
phân trang). Hành vi phía AI Service (SQL, ràng buộc DB) được test thật ở ai-service: tests/integration/test_ocr_store.py."""
import pathlib
import sys

import pytest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))

from storage.dataset import Dataset  # noqa: E402
from storage.memory import MerchantMemory  # noqa: E402
from storage.ai_api import who  # noqa: E402
from storage.storage_api import ApiDataset, ApiMerchantMemory, open_stores  # noqa: E402
from tests.fake_ai_api import FakeAiApi  # noqa: E402

@pytest.fixture
def api():
    return FakeAiApi()


def test_memory_roundtrip_fuzzy_match_and_keeps_the_pretty_name_on_later_updates(api):
    mem = ApiMerchantMemory(api, "u1")
    mem.set("FPTAvtail", "healthcare", name="Nhà thuốc Long Châu")
    assert mem.get("FPTAvtail") == "healthcare" and mem.display_name("FPTAvtail") == "Nhà thuốc Long Châu"
    assert mem.get("FPTRvtail") == "healthcare"  # luật khớp mờ giống bản file
    mem.set("FPTAvtail", "shopping")  # đổi danh mục, không gửi tên: giữ tên đẹp cũ
    assert mem.get("FPTAvtail") == "shopping" and mem.display_name("FPTAvtail") == "Nhà thuốc Long Châu"
    assert mem.find_in_lines(["PHIEU THU", "FPTAvtail", "Tong 52.000"]) == "FPTAvtail"


def test_memory_is_private_per_user_and_rejects_bad_input(api):
    ApiMerchantMemory(api, "u1").set("Highlands Coffee", "dining")
    assert ApiMerchantMemory(api, "u2").get("Highlands Coffee") is None
    with pytest.raises(ValueError):
        ApiMerchantMemory(api, "u1").set("X Shop", "khong-co-danh-muc")
    with pytest.raises(ValueError):
        ApiMerchantMemory(api, "u1").set("   ", "dining")


def test_dataset_append_read_all_roundtrip_and_converts_to_training_rows(api):
    ds = ApiDataset(api, "u1")
    ds.append({"merchant": "Kisnac", "category": "groceries", "label_source": "user", "total_amount": 570500, "lines": ["Kisnac", "TONG 570.500"]})
    rows = ds.read_all()
    assert len(rows) == 1
    r = rows[0]
    assert (r["merchant"], r["category"], r["label_source"], r["total_amount"], r["lines"]) == ("Kisnac", "groceries", "user", 570500, ["Kisnac", "TONG 570.500"])


def test_same_bill_is_stored_once_and_the_latest_label_wins(api):
    ds = ApiDataset(api, "u1")
    bill = {"merchant": "Tiệm Cô Lan", "total_amount": 38500, "lines": ["Tiệm Cô Lan", "Cà chua 1kg", "TONG 38.500"], "label_source": "user"}
    ds.append({**bill, "category": "other"})
    ds.append({**bill, "category": "groceries"})  # gửi lại (đồng bộ lại hoặc người dùng sửa nhãn)
    rows = ds.read_all()
    assert len(rows) == 1 and rows[0]["category"] == "groceries"


def test_every_label_write_adds_an_event_and_never_edits_an_old_one(api):
    ds = ApiDataset(api, "u1")
    bill = {"merchant": "Tiệm Cô Lan", "total_amount": 38500, "lines": ["Tiệm Cô Lan", "Cà chua 1kg", "TONG 38.500"]}
    ds.append({**bill, "category": "other", "label_source": "user"})
    ds.append({**bill, "category": "groceries", "label_source": "gold"})  # đổi nhãn: thêm sự kiện mới, sự kiện cũ giữ nguyên
    assert api.events == [("other", "user"), ("groceries", "gold")]


def test_read_all_collects_every_users_bills_for_shared_training(api):
    ApiDataset(api, "u1").append({"merchant": "A", "category": "dining", "lines": ["A", "pho"], "total_amount": 1000})
    ApiDataset(api, "u2").append({"merchant": "B", "category": "transport", "lines": ["B", "xang"], "total_amount": 2000})
    assert sorted(r["merchant"] for r in ApiDataset(api, "u9").read_all()) == ["A", "B"]


def test_invalid_category_is_rejected_and_missing_total_is_kept_null(api):
    ds = ApiDataset(api, "u1")
    with pytest.raises(ValueError):
        ds.append({"merchant": "A", "category": "nope", "lines": ["A"]})
    ds.append({"merchant": "Nhãn vận chuyển", "category": "shopping", "lines": [], "total_amount": None})
    assert ds.read_all()[0]["total_amount"] is None and ds.read_all()[0]["lines"] == []


def test_a_rejected_write_sends_nothing(api):
    ds = ApiDataset(api, "u1")
    with pytest.raises(ValueError):
        ds.append({"merchant": "A", "category": "nope", "lines": ["A"]})
    assert ds.read_all() == [] and api.samples == {}


def test_open_stores_uses_files_unless_the_ai_service_is_configured(tmp_path, monkeypatch):
    monkeypatch.delenv("AI_SERVICE_URL", raising=False)
    monkeypatch.delenv("AI_SERVICE_API_KEY", raising=False)
    mem, ds = open_stores(tmp_path)
    assert isinstance(mem, MerchantMemory) and not isinstance(mem, ApiMerchantMemory) and isinstance(ds, Dataset)
    monkeypatch.setenv("AI_SERVICE_URL", "http://ai-service:8000")  # thiếu khóa thì vẫn là file: không gọi nhầm một dịch vụ không xác thực được
    assert not isinstance(open_stores(tmp_path)[0], ApiMerchantMemory)
    monkeypatch.setenv("AI_SERVICE_API_KEY", "k" * 32)
    mem, ds = open_stores(tmp_path)
    assert isinstance(mem, ApiMerchantMemory) and isinstance(ds, ApiDataset)


def test_import_handles_old_memory_entries_that_are_just_a_category_string(tmp_path, monkeypatch, api):
    import json

    import import_files
    (tmp_path / "merchant_memory.json").write_text(json.dumps({"kisnac": "groceries", "long chau": {"category": "healthcare", "name": "Long Châu"}}), encoding="utf-8")
    (tmp_path / "dataset.jsonl").write_text(json.dumps({"merchant": "Kisnac", "category": "groceries", "label_source": "user", "total_amount": 5000, "lines": ["Kisnac"]}) + "\n", encoding="utf-8")
    monkeypatch.setenv("AI_SERVICE_URL", "http://khong-dung-toi")
    monkeypatch.setenv("AI_SERVICE_API_KEY", "k" * 32)
    monkeypatch.setattr(import_files, "open_stores", lambda base: (ApiMerchantMemory(api, "u1"), ApiDataset(api, "u1")))
    assert import_files.do_import(tmp_path, write=True) == 0
    mem = ApiMerchantMemory(api, "u1")
    # DB lưu slug backend: groceries và dining cùng là food, đọc lại ra nhóm đứng đầu (dining). Sự mất phân biệt này là cái giá của việc dùng chung user_merchant_memory.
    assert mem.get("kisnac") == "dining" and mem.get("long chau") == "healthcare" and mem.display_name("long chau") == "Long Châu"
    assert sorted((key, v["category_slug"]) for (_, key), v in api.memory.items()) == [("kisnac", "food"), ("long chau", "health")]
    assert len(ApiDataset(api, "u1").read_all()) == 1


# --- bộ chuẩn (gold) và bill giả ---
def test_gold_marks_existing_imported_bills_adds_missing_ones_and_is_idempotent(api):
    from storage import datasets_api
    store = ApiDataset(api, "u1")
    store.append({"merchant": "Kisnac", "category": "groceries", "label_source": "imported", "total_amount": 570500, "lines": ["Kisnac", "TONG 570.500"]})
    store.append({"merchant": "Khác", "category": "dining", "label_source": "user", "total_amount": 70000, "lines": ["Pho"]})  # nhãn người dùng: không phải gold
    expected = {"a.jpg": {"merchant": "kisnac", "total": 570500, "category": "groceries"},
                "b.jpg": {"merchant": ["longchau", "nhathuoc"], "total": 52000, "category": "healthcare"},
                "c.jpg": {"merchant": None, "total": 873000, "category": "groceries"}}
    asked = []
    lines = lambda name: asked.append(name) or [f"dong cua {name}"]  # noqa: E731
    dry = datasets_api.import_gold(api, expected, lines, write=False)
    assert dry == {"marked": 1, "added": 2, "already": 0} and asked == []  # xem trước không chạy OCR và không ghi
    r = datasets_api.import_gold(api, expected, lines, write=True)
    assert r == {"marked": 1, "added": 2, "already": 0} and sorted(asked) == ["b.jpg", "c.jpg"]  # chỉ OCR ảnh chưa có
    gold = sorted((s for s in api.samples.values() if s["is_gold"]), key=lambda s: s["total_amount"])
    assert [(s["merchant"], s["label_source"]) for s in gold] == [("longchau", "gold"), ("Kisnac", "imported"), ("", "gold")]
    again = datasets_api.import_gold(api, expected, lines, write=True)
    assert again == {"marked": 0, "added": 0, "already": 3}  # chạy lại không đổi gì
    assert len(api.samples) == 4


def test_synthetic_bills_are_tagged_and_import_is_idempotent(api, tmp_path):
    from storage import datasets_api
    import json
    rows = [{"merchant": f"Tiệm {i}", "category": "dining", "total_amount": 1000 * i, "lines": [f"Tiệm {i}", "Phở"], "label_source": "synthetic_ocr"} for i in range(1, 4)]
    path = tmp_path / "syn.jsonl"
    path.write_text("\n".join(json.dumps(r, ensure_ascii=False) for r in rows) + "\n", encoding="utf-8")
    assert datasets_api.import_synthetic(api, path, write=False) == {"in_file": 3, "in_db": 0}
    assert datasets_api.import_synthetic(api, path, write=True) == {"in_file": 3, "in_db": 3}
    assert datasets_api.import_synthetic(api, path, write=True) == {"in_file": 3, "in_db": 3}
    assert {r["label_source"] for r in ApiDataset(api, "x").read_all()} == {"synthetic_ocr"}


def test_group_key_treats_spacing_accents_and_punctuation_as_the_same_shop():
    from classification.category_model import _group_key
    assert len({_group_key({"merchant": m}) for m in ("Bách Hóa Xanh", "bach hoa xanh", "BACHHOAXANH", "Bách-Hóa  Xanh.")}) == 1
    assert _group_key({"merchant": "Kisnac"}) != _group_key({"merchant": "Kisnac Mart"})


def test_gold_names_are_normalised_to_the_display_name_of_the_same_shop(api):
    from storage import datasets_api
    store = ApiDataset(api, "u1")
    store.append({"merchant": "Nhà thuốc Long Châu", "category": "healthcare", "label_source": "user", "total_amount": 1000, "lines": ["a"]})
    store.append({"merchant": "Minh Phát", "category": "groceries", "label_source": "user", "total_amount": 2000, "lines": ["b"]})
    store.append({"merchant": "Thực Phẩm Sạch An Nhiên", "category": "groceries", "label_source": "user", "total_amount": 2100, "lines": ["b2"]})
    store.append({"merchant": "Tm-dv Thực Phẩm Bảo Nam", "category": "groceries", "label_source": "user", "total_amount": 2200, "lines": ["b3"]})
    for name, total, cat in (("longchau", 3000, "healthcare"), ("minh", 4000, "groceries"), ("bachhoaxanh", 5000, "groceries"), ("chuacoten", 6000, "other"), ("thucpham", 7000, "groceries")):
        store.append({"merchant": name, "category": cat, "label_source": "gold", "is_gold": True, "total_amount": total, "lines": [name]})
    brands = {"bach hoa xanh": "Bách Hóa Xanh"}
    dry = datasets_api.normalize_gold_names(api, brands, write=False)
    assert sorted(dry) == [("bachhoaxanh", "Bách Hóa Xanh"), ("longchau", "Nhà thuốc Long Châu")]  # "minh" quá ngắn để nối bừa; không khớp thì giữ
    names = lambda: sorted(r["merchant"] for r in store.read_all() if r["label_source"] == "gold")  # noqa: E731
    assert names() == ["bachhoaxanh", "chuacoten", "longchau", "minh", "thucpham"]  # xem trước không ghi; "thucpham" khớp hai tên nên bỏ qua
    datasets_api.normalize_gold_names(api, brands, write=True)
    assert names() == ["Bách Hóa Xanh", "Nhà thuốc Long Châu", "chuacoten", "minh", "thucpham"]
    assert datasets_api.normalize_gold_names(api, brands, write=True) == []  # chạy lại không đổi gì
    assert not any(s["merchant"] == "longchau" for s in api.samples.values())  # tên mẫu thô cũng được đổi theo
