"""Lớp lưu trữ PostgreSQL, chạy bằng SQLite làm giả lập (cùng câu SQL, chỉ đổi %s thành ?): kiểm tra logic, không cần server.
Chưa kiểm chứng trên PostgreSQL thật: cần chạy migrations/0006_*.sql rồi thử với DB phát triển trước khi dùng thật."""
import pathlib
import sys

import pytest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))

from storage.dataset import Dataset  # noqa: E402
from storage.memory import MerchantMemory  # noqa: E402
from storage.storage_pg import PgDataset, PgMerchantMemory, open_stores  # noqa: E402
from storage.storage_pg import _cursor as knowledge_cursor  # noqa: E402
from tests.pg_fake import make_connect  # noqa: E402

@pytest.fixture
def connect(tmp_path):
    return make_connect(tmp_path / "pg.sqlite")


def test_memory_roundtrip_fuzzy_match_and_keeps_the_pretty_name_on_later_updates(connect):
    mem = PgMerchantMemory(connect, "u1")
    mem.set("FPTAvtail", "healthcare", name="Nhà thuốc Long Châu")
    assert mem.get("FPTAvtail") == "healthcare" and mem.display_name("FPTAvtail") == "Nhà thuốc Long Châu"
    assert mem.get("FPTRvtail") == "healthcare"  # luật khớp mờ giống bản file
    mem.set("FPTAvtail", "shopping")  # đổi danh mục, không gửi tên: giữ tên đẹp cũ
    assert mem.get("FPTAvtail") == "shopping" and mem.display_name("FPTAvtail") == "Nhà thuốc Long Châu"
    assert mem.find_in_lines(["PHIEU THU", "FPTAvtail", "Tong 52.000"]) == "FPTAvtail"


def test_memory_is_private_per_user_and_rejects_bad_input(connect):
    PgMerchantMemory(connect, "u1").set("Highlands Coffee", "dining")
    assert PgMerchantMemory(connect, "u2").get("Highlands Coffee") is None
    with pytest.raises(ValueError):
        PgMerchantMemory(connect, "u1").set("X Shop", "khong-co-danh-muc")
    with pytest.raises(ValueError):
        PgMerchantMemory(connect, "u1").set("   ", "dining")


def test_dataset_append_read_all_roundtrip_and_converts_to_training_rows(connect):
    ds = PgDataset(connect, "u1")
    ds.append({"merchant": "Kisnac", "category": "groceries", "label_source": "user", "total_amount": 570500, "lines": ["Kisnac", "TONG 570.500"]})
    rows = ds.read_all()
    assert len(rows) == 1
    r = rows[0]
    assert (r["merchant"], r["category"], r["label_source"], r["total_amount"], r["lines"]) == ("Kisnac", "groceries", "user", 570500, ["Kisnac", "TONG 570.500"])


def test_same_bill_is_stored_once_and_the_latest_label_wins(connect):
    ds = PgDataset(connect, "u1")
    bill = {"merchant": "Tiệm Cô Lan", "total_amount": 38500, "lines": ["Tiệm Cô Lan", "Cà chua 1kg", "TONG 38.500"], "label_source": "user"}
    ds.append({**bill, "category": "other"})
    ds.append({**bill, "category": "groceries"})  # gửi lại (đồng bộ lại hoặc người dùng sửa nhãn)
    rows = ds.read_all()
    assert len(rows) == 1 and rows[0]["category"] == "groceries"


def test_read_all_collects_every_users_bills_for_shared_training(connect):
    PgDataset(connect, "u1").append({"merchant": "A", "category": "dining", "lines": ["A", "pho"], "total_amount": 1000})
    PgDataset(connect, "u2").append({"merchant": "B", "category": "transport", "lines": ["B", "xang"], "total_amount": 2000})
    assert sorted(r["merchant"] for r in PgDataset(connect, "u9").read_all()) == ["A", "B"]


def test_invalid_category_is_rejected_and_missing_total_is_kept_null(connect):
    ds = PgDataset(connect, "u1")
    with pytest.raises(ValueError):
        ds.append({"merchant": "A", "category": "nope", "lines": ["A"]})
    ds.append({"merchant": "Nhãn vận chuyển", "category": "shopping", "lines": [], "total_amount": None})
    assert ds.read_all()[0]["total_amount"] is None and ds.read_all()[0]["lines"] == []


def test_failed_write_is_rolled_back(connect):
    ds = PgDataset(connect, "u1")
    with pytest.raises(ValueError):
        ds.append({"merchant": "A", "category": "nope", "lines": ["A"]})
    assert ds.read_all() == []


def test_open_stores_uses_files_unless_a_database_url_is_set(tmp_path, monkeypatch):
    monkeypatch.delenv("FINMATE_DATABASE_URL", raising=False)
    mem, ds = open_stores(tmp_path)
    assert isinstance(mem, MerchantMemory) and not isinstance(mem, PgMerchantMemory) and isinstance(ds, Dataset)


def test_import_handles_old_memory_entries_that_are_just_a_category_string(tmp_path, monkeypatch, connect):
    import json

    import import_files_to_pg
    (tmp_path / "merchant_memory.json").write_text(json.dumps({"kisnac": "groceries", "long chau": {"category": "healthcare", "name": "Long Châu"}}), encoding="utf-8")
    (tmp_path / "dataset.jsonl").write_text(json.dumps({"merchant": "Kisnac", "category": "groceries", "label_source": "user", "total_amount": 5000, "lines": ["Kisnac"]}) + "\n", encoding="utf-8")
    monkeypatch.setenv("FINMATE_DATABASE_URL", "postgresql://khong-dung-toi")
    monkeypatch.setattr(import_files_to_pg, "open_stores", lambda base: (PgMerchantMemory(connect, "u1"), PgDataset(connect, "u1")))
    assert import_files_to_pg.do_import(tmp_path, write=True) == 0
    mem = PgMerchantMemory(connect, "u1")
    assert mem.get("kisnac") == "groceries" and mem.display_name("long chau") == "Long Châu"
    assert len(PgDataset(connect, "u1").read_all()) == 1


# --- bộ chuẩn (gold) và bill giả ---
def test_gold_marks_existing_imported_bills_adds_missing_ones_and_is_idempotent(connect):
    from storage import datasets_pg
    store = PgDataset(connect, "u1")
    store.append({"merchant": "Kisnac", "category": "groceries", "label_source": "imported", "total_amount": 570500, "lines": ["Kisnac", "TONG 570.500"]})
    store.append({"merchant": "Khác", "category": "dining", "label_source": "user", "total_amount": 70000, "lines": ["Pho"]})  # nhãn người dùng: không phải gold
    expected = {"a.jpg": {"merchant": "kisnac", "total": 570500, "category": "groceries"},
                "b.jpg": {"merchant": ["longchau", "nhathuoc"], "total": 52000, "category": "healthcare"},
                "c.jpg": {"merchant": None, "total": 873000, "category": "groceries"}}
    asked = []
    lines = lambda name: asked.append(name) or [f"dong cua {name}"]  # noqa: E731
    dry = datasets_pg.import_gold(connect, expected, lines, write=False)
    assert dry == {"marked": 1, "added": 2, "already": 0} and asked == []  # xem trước không chạy OCR và không ghi
    r = datasets_pg.import_gold(connect, expected, lines, write=True)
    assert r == {"marked": 1, "added": 2, "already": 0} and sorted(asked) == ["b.jpg", "c.jpg"]  # chỉ OCR ảnh chưa có
    with knowledge_cursor(connect) as cur:
        cur.execute("SELECT merchant_name, labeled_by, is_gold FROM bill_labeled_samples WHERE is_gold ORDER BY amount_cents")
        rows = cur.fetchall()
    assert [(m, by) for m, by, _ in rows] == [("longchau", "gold"), ("Kisnac", "imported"), ("", "gold")]
    again = datasets_pg.import_gold(connect, expected, lines, write=True)
    assert again == {"marked": 0, "added": 0, "already": 3}  # chạy lại không đổi gì
    with knowledge_cursor(connect) as cur:
        cur.execute("SELECT count(*) FROM bill_labeled_samples")
        assert cur.fetchone()[0] == 4


def test_synthetic_bills_are_tagged_and_import_is_idempotent(connect, tmp_path):
    from storage import datasets_pg
    import json
    rows = [{"merchant": f"Tiệm {i}", "category": "dining", "total_amount": 1000 * i, "lines": [f"Tiệm {i}", "Phở"], "label_source": "synthetic_ocr"} for i in range(1, 4)]
    path = tmp_path / "syn.jsonl"
    path.write_text("\n".join(json.dumps(r, ensure_ascii=False) for r in rows) + "\n", encoding="utf-8")
    assert datasets_pg.import_synthetic(connect, path, write=False) == {"in_file": 3, "in_db": 0}
    assert datasets_pg.import_synthetic(connect, path, write=True) == {"in_file": 3, "in_db": 3}
    assert datasets_pg.import_synthetic(connect, path, write=True) == {"in_file": 3, "in_db": 3}
    assert {r["label_source"] for r in PgDataset(connect, "x").read_all()} == {"synthetic_ocr"}


def test_group_key_treats_spacing_accents_and_punctuation_as_the_same_shop():
    from classification.category_model import _group_key
    assert len({_group_key({"merchant": m}) for m in ("Bách Hóa Xanh", "bach hoa xanh", "BACHHOAXANH", "Bách-Hóa  Xanh.")}) == 1
    assert _group_key({"merchant": "Kisnac"}) != _group_key({"merchant": "Kisnac Mart"})


def test_gold_names_are_normalised_to_the_display_name_of_the_same_shop(connect):
    from storage import datasets_pg
    store = PgDataset(connect, "u1")
    store.append({"merchant": "Nhà thuốc Long Châu", "category": "healthcare", "label_source": "user", "total_amount": 1000, "lines": ["a"]})
    store.append({"merchant": "Minh Phát", "category": "groceries", "label_source": "user", "total_amount": 2000, "lines": ["b"]})
    store.append({"merchant": "Thực Phẩm Sạch An Nhiên", "category": "groceries", "label_source": "user", "total_amount": 2100, "lines": ["b2"]})
    store.append({"merchant": "Tm-dv Thực Phẩm Bảo Nam", "category": "groceries", "label_source": "user", "total_amount": 2200, "lines": ["b3"]})
    for name, total, cat in (("longchau", 3000, "healthcare"), ("minh", 4000, "groceries"), ("bachhoaxanh", 5000, "groceries"), ("chuacoten", 6000, "other"), ("thucpham", 7000, "groceries")):
        store.append({"merchant": name, "category": cat, "label_source": "gold", "is_gold": True, "total_amount": total, "lines": [name]})
    brands = {"bach hoa xanh": "Bách Hóa Xanh"}
    dry = datasets_pg.normalize_gold_names(connect, brands, write=False)
    assert sorted(dry) == [("bachhoaxanh", "Bách Hóa Xanh"), ("longchau", "Nhà thuốc Long Châu")]  # "minh" quá ngắn để nối bừa; không khớp thì giữ
    names = lambda: sorted(r["merchant"] for r in store.read_all() if r["label_source"] == "gold")  # noqa: E731
    assert names() == ["bachhoaxanh", "chuacoten", "longchau", "minh", "thucpham"]  # xem trước không ghi; "thucpham" khớp hai tên nên bỏ qua
    datasets_pg.normalize_gold_names(connect, brands, write=True)
    assert names() == ["Bách Hóa Xanh", "Nhà thuốc Long Châu", "chuacoten", "minh", "thucpham"]
    assert datasets_pg.normalize_gold_names(connect, brands, write=True) == []  # chạy lại không đổi gì
    with knowledge_cursor(connect) as cur:
        cur.execute("SELECT count(*) FROM bill_raw_samples WHERE notification_title = 'longchau'")
        assert cur.fetchone()[0] == 0  # tiêu đề của mẫu thô cũng được đổi theo
