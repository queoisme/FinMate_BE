"""API lưu trữ dành cho ocr-service (``/api/v1/ocr-store``) trên Postgres thật: mỗi nhóm endpoint làm đúng việc mà SQL trực tiếp của ocr-service từng làm."""

import httpx
from sqlalchemy import text

from app.core.anonymizer import sha256_hex
from app.main import app

BASE = "/api/v1/ocr-store"
USER = sha256_hex("user-1")
OTHER = sha256_hex("user-2")


def digest(label: str) -> str:
    return sha256_hex(label)


def sample(label: str = "bill-1", **overrides) -> dict:
    return {"user_id_hash": USER, "content_hash": digest(label), "merchant": "Highlands Coffee", "lines": ["Ca phe sua", "Tong cong 45.000"],
            "total_amount": 45000, "category_slug": "dining", "label_source": "user", **overrides}


async def count(session, sql: str, **params) -> int:
    return (await session.execute(text(sql), params)).scalar_one()


# ------------------------------------------------------------------ kiến thức
async def test_seed_adds_only_what_is_missing_and_never_overwrites(client, auth_headers, session):
    await session.execute(text("INSERT INTO kb_brands (brand_key, category_slug, display_name, is_active, source) VALUES ('phuc long', 'shopping', 'Phúc Long', FALSE, 'review')"))
    await session.execute(text("INSERT INTO kb_keywords (kind, category_slug, keyword, category_priority, is_active, source) VALUES ('merchant', 'shopping', 'nha sach', 10, TRUE, 'review')"))
    await session.commit()
    body = {"brands": [{"brand_key": "phuc long", "category_slug": "dining", "display_name": "Phúc Long"}, {"brand_key": "highlands coffee", "category_slug": "dining", "display_name": "Highlands Coffee"}],
            "keywords": [{"kind": "merchant", "category_slug": "dining", "keyword": "nha sach"}, {"kind": "merchant", "category_slug": "dining", "keyword": "quan cafe", "category_priority": 20}]}

    first = await client.post(f"{BASE}/knowledge/seed", json=body, headers=auth_headers)
    again = await client.post(f"{BASE}/knowledge/seed", json=body, headers=auth_headers)

    assert first.json() == {"brands": 1, "keywords": 1} and again.json() == {"brands": 0, "keywords": 0}
    brand = (await session.execute(text("SELECT category_slug, is_active FROM kb_brands WHERE brand_key = 'phuc long'"))).one()
    assert tuple(brand) == ("shopping", False)  # dòng đã có (đã chỉnh, đã tắt) giữ nguyên
    assert await count(session, "SELECT count(*) FROM kb_keywords WHERE keyword = 'nha sach'") == 1  # từ khóa đã chuyển nhóm không bị thêm lại ở nhóm cũ


async def test_knowledge_lists_active_rows_by_default_and_everything_on_request(client, auth_headers, session):
    await session.execute(text("INSERT INTO kb_brands (brand_key, category_slug, display_name, is_active, source) VALUES ('a brand', 'dining', 'A', TRUE, 'seed'), ('b brand', 'dining', 'B', FALSE, 'seed')"))
    await session.execute(text("INSERT INTO kb_keywords (kind, category_slug, keyword, category_priority, is_active) VALUES ('merchant', 'dining', 'quan', 20, TRUE), ('product', 'shopping', 'ao', 10, TRUE)"))
    await session.commit()

    active = (await client.get(f"{BASE}/knowledge", headers=auth_headers)).json()
    everything = (await client.get(f"{BASE}/knowledge", params={"include_inactive": True}, headers=auth_headers)).json()

    assert [b["brand_key"] for b in active["brands"]] == ["a brand"] and [b["brand_key"] for b in everything["brands"]] == ["a brand", "b brand"]
    assert [k["keyword"] for k in active["keywords"]] == ["ao", "quan"]  # theo category_priority


async def test_seed_refuses_an_unknown_category(client, auth_headers):
    response = await client.post(f"{BASE}/knowledge/seed", json={"brands": [{"brand_key": "x brand", "category_slug": "khong_co", "display_name": "X"}]}, headers=auth_headers)

    assert response.status_code == 422 and response.json()["error"]["code"] == "unknown_category"


async def test_a_voted_brand_waits_for_review_and_only_voted_rows_can_be_activated(client, auth_headers, session):
    vote = {"brand_key": "tiem banh", "category_slug": "dining", "display_name": "Tiệm Bánh", "whole_word": False, "is_active": False}
    await session.execute(text("INSERT INTO kb_brands (brand_key, category_slug, display_name, is_active, source) VALUES ('seeded', 'dining', 'S', FALSE, 'seed')"))
    await session.commit()

    assert (await client.post(f"{BASE}/brands/vote", json=vote, headers=auth_headers)).json() == {"created": True}
    assert (await client.post(f"{BASE}/brands/vote", json=vote, headers=auth_headers)).json() == {"created": False}  # không ghi đè
    assert (await client.post(f"{BASE}/brands/activate-vote", json={"merchant_key": "seeded"}, headers=auth_headers)).json() == {"activated": False}  # không phải dòng do phiếu bầu thêm
    assert (await client.post(f"{BASE}/brands/activate-vote", json={"merchant_key": "tiem banh"}, headers=auth_headers)).json() == {"activated": True}
    assert (await client.post(f"{BASE}/brands/activate-vote", json={"merchant_key": "tiem banh"}, headers=auth_headers)).json() == {"activated": False}  # đã bật rồi
    assert await count(session, "SELECT count(*) FROM kb_brands WHERE brand_key = 'seeded' AND NOT is_active") == 1


async def test_merchant_consensus_counts_one_vote_per_user_from_contributed_bills(client, auth_headers, session):
    for number in range(3):
        await session.execute(
            text("INSERT INTO ml_training_samples (scan_id, user_id_hash, merchant_key, lines, final_slug, labeled_at) VALUES (gen_random_uuid(), :u, 'tiem banh', 'x', 'food', now())"),
            {"u": sha256_hex(f"voter-{number}")},
        )
    await session.commit()

    items = (await client.get(f"{BASE}/consensus/merchants", headers=auth_headers)).json()["items"]

    assert items == [{"merchant_key": "tiem banh", "category_slug": "food", "users": 3}]


# ------------------------------------------------------------------ cửa hàng người dùng đã chốt
async def test_merchant_memory_is_per_user_and_only_takes_backend_expense_slugs(client, auth_headers):
    put = {"user_id_hash": USER, "merchant_key": "Highlands Coffee", "display_name": "Highlands Coffee", "category_slug": "food"}

    assert (await client.put(f"{BASE}/merchant-memory", json=put, headers=auth_headers)).json() == {"ok": True}
    await client.put(f"{BASE}/merchant-memory", json={**put, "category_slug": "shopping", "display_name": None}, headers=auth_headers)  # đổi ý: danh mục mới, giữ tên đẹp
    mine = (await client.get(f"{BASE}/merchant-memory", params={"user_id_hash": USER}, headers=auth_headers)).json()["items"]
    theirs = (await client.get(f"{BASE}/merchant-memory", params={"user_id_hash": OTHER}, headers=auth_headers)).json()["items"]

    assert mine == [{"merchant_key": "highlands coffee", "category_slug": "shopping", "display_name": "Highlands Coffee"}] and theirs == []
    for bad in ("income", "dining", "khong_co"):  # thu nhập, nhóm cũ của bill, slug lạ
        response = await client.put(f"{BASE}/merchant-memory", json={**put, "category_slug": bad}, headers=auth_headers)
        assert response.status_code == 422, bad


# ------------------------------------------------------------------ bill đã gắn nhãn
async def test_a_labeled_bill_is_stored_as_a_receipt_sample_with_a_label_and_one_event(client, auth_headers, session):
    assert (await client.post(f"{BASE}/labeled-samples", json=sample(), headers=auth_headers)).json() == {"ok": True}

    raw = (await session.execute(text("SELECT source, package_name, notification_title, notification_body, user_id_hash FROM ml_raw_samples"))).one()
    label = (await session.execute(text("SELECT transaction_type, amount_cents, category_slug, labeled_by, is_gold FROM ml_labeled_samples"))).one()
    assert tuple(raw) == ("receipt", "bill_scan", "Highlands Coffee", "Ca phe sua\nTong cong 45.000", USER)
    assert tuple(label) == ("debit", 45000, "dining", "user", False)
    assert await count(session, "SELECT count(*) FROM ml_label_events WHERE source = 'user'") == 1


async def test_relabeling_the_same_bill_updates_the_label_adds_an_event_and_keeps_gold(client, auth_headers, session):
    await client.post(f"{BASE}/labeled-samples", json=sample(label_source="gold", is_gold=True), headers=auth_headers)

    await client.post(f"{BASE}/labeled-samples", json=sample(category_slug="shopping", label_source="user"), headers=auth_headers)

    assert await count(session, "SELECT count(*) FROM ml_raw_samples") == 1 and await count(session, "SELECT count(*) FROM ml_labeled_samples") == 1
    label = (await session.execute(text("SELECT category_slug, is_gold FROM ml_labeled_samples"))).one()
    assert tuple(label) == ("shopping", True)  # nhãn mới nhất thắng; đã là gold thì giữ gold
    assert await count(session, "SELECT count(*) FROM ml_label_events") == 2  # mỗi lần đặt nhãn là một sự kiện mới


async def test_labeled_bills_are_listed_for_training_but_only_the_ocr_service_ones(client, auth_headers, session):
    await client.post(f"{BASE}/labeled-samples", json=sample("a", merchant="Quán A", category_slug="dining"), headers=auth_headers)
    await client.post(f"{BASE}/labeled-samples", json=sample("b", merchant="Quán B", category_slug="groceries", total_amount=None), headers=auth_headers)
    # văn bản OCR của POST /ocr (cùng source 'receipt' nhưng package khác) có nhãn nhóm backend: không được lẫn vào dữ liệu huấn luyện 9 nhóm
    await session.execute(text("INSERT INTO ml_raw_samples (id, source, package_name, notification_body, received_at, content_hash) VALUES ('00000000-0000-0000-0000-0000000000f1', 'receipt', 'receipt_ocr', 'x', now(), :h)"), {"h": digest("ocr")})
    await session.execute(text("INSERT INTO ml_labeled_samples (raw_sample_id, is_financial, category_slug, labeled_by) VALUES ('00000000-0000-0000-0000-0000000000f1', true, 'food', 'user')"))
    await session.commit()

    page = (await client.get(f"{BASE}/labeled-samples", params={"limit": 1, "offset": 0}, headers=auth_headers)).json()
    everything = (await client.get(f"{BASE}/labeled-samples", headers=auth_headers)).json()

    assert page["total"] == 2 and len(page["items"]) == 1
    assert {i["merchant"]: (i["category"], i["total_amount"], i["lines"]) for i in everything["items"]} == {
        "Quán A": ("dining", 45000, ["Ca phe sua", "Tong cong 45.000"]), "Quán B": ("groceries", None, ["Ca phe sua", "Tong cong 45.000"])}
    counted = await client.get(f"{BASE}/labeled-samples/count", params={"label_source": "user"}, headers=auth_headers)
    assert counted.json() == {"count": 3}  # đếm theo nguồn nhãn trên cả bảng, kể cả bill của /ocr


async def test_a_labeled_bill_needs_a_hash_and_a_known_category(client, auth_headers):
    assert (await client.post(f"{BASE}/labeled-samples", json=sample(user_id_hash="local"), headers=auth_headers)).status_code == 422
    assert (await client.post(f"{BASE}/labeled-samples", json=sample(content_hash="abc"), headers=auth_headers)).status_code == 422
    assert (await client.post(f"{BASE}/labeled-samples", json=sample(label_source="robot"), headers=auth_headers)).status_code == 422
    unknown = await client.post(f"{BASE}/labeled-samples", json=sample(category_slug="khong_co"), headers=auth_headers)
    assert unknown.status_code == 422 and unknown.json()["error"]["code"] == "unknown_category"


# ------------------------------------------------------------------ bộ chuẩn (gold)
async def test_gold_claim_marks_exactly_one_matching_imported_bill(client, auth_headers, session):
    await client.post(f"{BASE}/labeled-samples", json=sample("g1", total_amount=52000, category_slug="healthcare", label_source="imported"), headers=auth_headers)
    await client.post(f"{BASE}/labeled-samples", json=sample("g2", total_amount=99000, category_slug="healthcare", label_source="imported"), headers=auth_headers)
    await client.post(f"{BASE}/labeled-samples", json=sample("g3", total_amount=99000, category_slug="healthcare", label_source="imported"), headers=auth_headers)

    async def claim(total, **extra):
        return (await client.post(f"{BASE}/gold/claim", json={"total": total, "category_slug": "healthcare", **extra}, headers=auth_headers)).json()["result"]

    assert await claim(52000, dry_run=True) == "marked" and await count(session, "SELECT count(*) FROM ml_labeled_samples WHERE is_gold") == 0  # xem trước không ghi
    assert await claim(52000) == "marked" and await count(session, "SELECT count(*) FROM ml_labeled_samples WHERE is_gold") == 1
    assert await claim(52000) == "already"
    assert await claim(99000) == "missing"  # hai bill trùng tổng tiền và danh mục: không đoán
    assert await claim(1) == "missing"


async def test_gold_names_can_be_listed_and_renamed(client, auth_headers, session):
    await client.post(f"{BASE}/labeled-samples", json=sample("n1", merchant="Nhà thuốc Long Châu", label_source="user"), headers=auth_headers)
    await client.post(f"{BASE}/labeled-samples", json=sample("n2", merchant="longchau", label_source="gold", is_gold=True), headers=auth_headers)

    names = (await client.get(f"{BASE}/gold/merchant-names", headers=auth_headers)).json()
    renamed = await client.post(f"{BASE}/gold/rename", json={"old": "longchau", "new": "Nhà thuốc Long Châu"}, headers=auth_headers)

    assert names == {"known": ["Nhà thuốc Long Châu"], "gold": ["longchau"]}
    assert renamed.json() == {"updated": 1}
    title = (await session.execute(text("SELECT notification_title FROM ml_raw_samples r JOIN ml_labeled_samples l ON l.raw_sample_id = r.id WHERE l.labeled_by = 'gold'"))).scalar_one()
    assert title == "Nhà thuốc Long Châu"


# ------------------------------------------------------------------ bill lạ
async def test_an_unknown_bill_is_counted_again_when_seen_again_and_closed_when_the_user_chooses(client, auth_headers, session):
    scan = {"user_id_hash": USER, "content_hash": digest("unknown"), "merchant_read": "Quán Lạ", "merchant_key": "quan la", "predicted_category": "dining",
            "predicted_source": "keyword", "confidence": 0.5, "total_amount": 1000, "lines": ["x", "y"]}

    await client.post(f"{BASE}/unknown-scans", json=scan, headers=auth_headers)
    await client.post(f"{BASE}/unknown-scans", json=scan, headers=auth_headers)
    row = (await session.execute(text("SELECT seen_count, status FROM bill_unknown_scans"))).one()
    closed = await client.post(f"{BASE}/unknown-scans/label", json={"merchant_key": "quan la"}, headers=auth_headers)
    await session.rollback()
    status = (await session.execute(text("SELECT status FROM bill_unknown_scans"))).scalar_one()

    assert tuple(row) == (2, "new") and closed.json() == {"updated": 1} and status == "labeled"


# ------------------------------------------------------------------ lịch sử huấn luyện
def finish(version: str, trained: bool = True, accuracy: float | None = 0.9) -> dict:
    return {"version": version, "artifact_path": f"models/{version}.joblib", "trained": trained, "n_records": 100, "cv_accuracy": accuracy, "macro_f1": 0.8,
            "classes": {"food": 60, "shopping": 40}, "reason": None if trained else "chưa đủ dữ liệu",
            "per_class": {"food": [0.9, 0.9, 0.9, 60], "shopping": [0.8, 0.8, 0.8, 40]}, "min_cv_accuracy": 0.7, "min_records": 50}


async def test_a_training_run_is_recorded_and_the_newest_good_version_replaces_the_old_one(client, auth_headers, session):
    first = (await client.post(f"{BASE}/training/jobs", json={"params": {"seed": 1}}, headers=auth_headers)).json()["job_id"]
    await client.post(f"{BASE}/training/jobs/{first}/finish", json=finish("v1"), headers=auth_headers)
    second = (await client.post(f"{BASE}/training/jobs", json={}, headers=auth_headers)).json()["job_id"]
    done = await client.post(f"{BASE}/training/jobs/{second}/finish", json=finish("v2"), headers=auth_headers)
    third = (await client.post(f"{BASE}/training/jobs", json={}, headers=auth_headers)).json()["job_id"]
    await client.post(f"{BASE}/training/jobs/{third}/finish", json=finish("v3", trained=False, accuracy=0.4), headers=auth_headers)

    assert done.status_code == 200 and "model_version_id" in done.json()
    versions = (await session.execute(text("SELECT v.version, v.status, v.artifact_path FROM ml_model_versions v JOIN ml_models m ON m.id = v.model_id WHERE m.code = 'bill_category' ORDER BY v.version"))).all()
    assert [tuple(v) for v in versions] == [("v1", "retired", "models/v1.joblib"), ("v2", "active", "models/v2.joblib"), ("v3", "rejected", "")]
    assert await count(session, "SELECT count(*) FROM ml_training_jobs WHERE status = 'succeeded'") == 3
    assert await count(session, "SELECT count(*) FROM ml_evaluation_runs WHERE split = 'cv'") == 3
    assert await count(session, "SELECT count(*) FROM ml_evaluation_category_metrics") == 6


async def test_a_failed_run_is_recorded_and_unknown_runs_or_models_are_refused(client, auth_headers, session):
    job = (await client.post(f"{BASE}/training/jobs", json={}, headers=auth_headers)).json()["job_id"]

    failed = await client.post(f"{BASE}/training/jobs/{job}/fail", json={"error": "hết bộ nhớ"}, headers=auth_headers)

    assert failed.json() == {"ok": True}
    assert tuple((await session.execute(text("SELECT status, error_message FROM ml_training_jobs"))).one()) == ("failed", "hết bộ nhớ")
    ghost = "00000000-0000-0000-0000-000000000000"
    assert (await client.post(f"{BASE}/training/jobs/{ghost}/fail", json={"error": "x"}, headers=auth_headers)).status_code == 404
    assert (await client.post(f"{BASE}/training/jobs/{ghost}/finish", json=finish("v1"), headers=auth_headers)).status_code == 404
    no_model = await client.post(f"{BASE}/training/jobs", json={"stage": "khong_co"}, headers=auth_headers)
    assert no_model.status_code == 422 and no_model.json()["error"]["code"] == "unknown_model"


async def test_every_endpoint_needs_the_internal_key():
    async with httpx.AsyncClient(transport=httpx.ASGITransport(app=app), base_url="http://ai-test") as anonymous:
        assert (await anonymous.get(f"{BASE}/knowledge")).status_code == 401
        assert (await anonymous.put(f"{BASE}/merchant-memory", json={})).status_code == 401
        assert (await anonymous.post(f"{BASE}/labeled-samples", json={})).status_code == 401
