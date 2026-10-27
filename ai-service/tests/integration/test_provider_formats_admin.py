"""API quản trị định dạng thông báo: từ draft trống tới kích hoạt, chỉ draft sửa được, lỗi đúng dạng, nhật ký."""

import pytest
from sqlalchemy import text

from app.core.anonymizer import sha256_hex

ACTOR = sha256_hex("admin-1")
BASE = "/api/v1/admin/provider-formats"
PACKAGE = "com.test.bank"
SAMPLE = "GD 75,000VND ma ABC"


@pytest.fixture
def headers(auth_headers):
    return {**auth_headers, "X-Actor-Hash": ACTOR}


async def new_draft(client, headers, **overrides) -> dict:
    response = await client.post(BASE, json={"provider_key": "test_bank", "package_name": PACKAGE, **overrides}, headers=headers)
    assert response.status_code == 201, response.text
    return response.json()


async def make_ready(client, headers) -> str:
    """Draft đủ điều kiện: một pattern group đặt tên khác mặc định, trường ánh xạ tới nó, một mẫu khớp."""
    fmt = await new_draft(client, headers)
    base = f"{BASE}/{fmt['id']}"
    pattern = await client.post(f"{base}/patterns", json={"pattern_name": "t1", "regex": r"GD (?P<tien>[\d,.]+)VND ma (?P<ref>[A-Z]+)", "transaction_type": "debit"}, headers=headers)
    assert pattern.status_code == 201, pattern.text
    for body in (
        {"field_key": "tien", "maps_to": "amount_cents", "data_type": "amount", "is_required": True, "source_ref": "tien", "normalizer": "vnd_amount"},
        {"field_key": "ref_code", "source_ref": "ref"},
    ):
        assert (await client.post(f"{base}/fields", json=body, headers=headers)).status_code == 201
    sample = await client.post(f"{base}/samples", json={"sample_text": SAMPLE, "expected_fields": {"amount_cents": 75000, "ref_code": "ABC"}}, headers=headers)
    assert sample.status_code == 201
    return fmt["id"]


async def test_the_seeded_formats_are_listed_and_readable(seeded_session, client, headers):
    listed = (await client.get(BASE, params={"status": "active"}, headers=headers)).json()
    assert listed["total"] >= 1 and all(i["status"] == "active" and i["patterns"] >= 1 for i in listed["items"])

    detail = (await client.get(f"{BASE}/{listed['items'][0]['id']}", headers=headers)).json()
    assert detail["patterns"] and detail["fields"] and detail["samples"]
    assert (await client.get(BASE, params={"status": "bogus"}, headers=headers)).status_code == 422


async def test_a_blank_draft_goes_through_edit_check_try_and_activation(client, headers, session):
    format_id = await make_ready(client, headers)
    base = f"{BASE}/{format_id}"

    check = (await client.get(f"{base}/check", headers=headers)).json()
    assert check == {"ready": True, "problems": []}
    tried = (await client.post(f"{base}/try", json={"text": SAMPLE}, headers=headers)).json()
    assert tried["matched"] and tried["pattern_name"] == "t1" and tried["fields"]["amount_cents"] == 75000 and tried["fields"]["ref_code"] == "ABC"
    assert not (await client.post(f"{base}/try", json={"text": "không liên quan"}, headers=headers)).json()["matched"]

    activated = await client.post(f"{base}/activate", json={"reason": "thử"}, headers=headers)
    assert activated.status_code == 200 and activated.json()["status"] == "active"
    actions = (await session.execute(text("SELECT action FROM kb_audit_log WHERE resource = 'provider_format' AND resource_key = :k"), {"k": format_id})).scalars().all()
    # không kiểm thứ tự: created_at là now() của giao dịch, các yêu cầu chung một giao dịch (GET không commit) có thể trùng giờ
    assert sorted(actions) == ["activate", "create", "update", "update", "update", "update"]  # 1 pattern + 2 trường + 1 mẫu


async def test_only_a_draft_can_be_edited(seeded_session, client, headers):
    active = (await client.get(BASE, params={"status": "active"}, headers=headers)).json()["items"][0]
    base = f"{BASE}/{active['id']}"

    for method, path, body in (
        ("patch", base, {"priority": 1}),
        ("post", f"{base}/patterns", {"pattern_name": "x", "regex": "x"}),
        ("post", f"{base}/fields", {"field_key": "x", "source_ref": "x"}),
        ("post", f"{base}/samples", {"sample_text": "x", "expected_fields": {}}),
    ):
        response = await getattr(client, method)(path, json=body, headers=headers)
        assert response.status_code == 409 and response.json()["error"]["code"] == "format_not_draft", path


async def test_clone_makes_an_editable_copy_and_archive_ends_it(seeded_session, client, headers):
    cloned = await client.post(f"{BASE}/clone", json={"package_name": "com.mbmobile"}, headers=headers)
    assert cloned.status_code == 201
    draft = cloned.json()
    assert draft["status"] == "draft" and draft["version"] >= 2 and draft["patterns"] and draft["fields"] and draft["samples"]

    patched = await client.patch(f"{BASE}/{draft['id']}", json={"priority": 7, "notes": "bản sao"}, headers=headers)
    assert patched.status_code == 200 and (patched.json()["priority"], patched.json()["notes"]) == (7, "bản sao")

    archived = await client.post(f"{BASE}/{draft['id']}/archive", json={}, headers=headers)
    assert archived.status_code == 200 and archived.json()["status"] == "archived"
    again = await client.post(f"{BASE}/{draft['id']}/archive", json={}, headers=headers)
    assert again.status_code == 409 and again.json()["error"]["code"] == "already_archived"
    assert (await client.post(f"{BASE}/clone", json={"package_name": "com.khong.co"}, headers=headers)).status_code == 404


async def test_bad_edits_are_refused_with_the_reason(client, headers):
    fmt = await new_draft(client, headers)
    base = f"{BASE}/{fmt['id']}"

    bad_regex = await client.post(f"{base}/patterns", json={"pattern_name": "x", "regex": "(chua dong"}, headers=headers)
    assert bad_regex.status_code == 422 and bad_regex.json()["error"]["code"] == "invalid_regex"
    assert (await client.post(f"{base}/patterns", json={"pattern_name": "a", "regex": "a"}, headers=headers)).status_code == 201
    dup = await client.post(f"{base}/patterns", json={"pattern_name": "a", "regex": "b"}, headers=headers)
    assert dup.status_code == 409 and dup.json()["error"]["code"] == "conflict"

    sensitive = await client.post(f"{base}/fields", json={"field_key": "account", "source_ref": "acct", "is_sensitive": True}, headers=headers)
    assert sensitive.status_code == 422 and sensitive.json()["error"]["code"] == "sensitive_field_unmapped"
    assert (await client.post(f"{base}/fields", json={"field_key": "Bad Key", "source_ref": "x"}, headers=headers)).status_code == 422
    assert (await client.post(f"{base}/fields", json={"field_key": "k", "source_ref": "x", "maps_to": "bogus"}, headers=headers)).status_code == 422
    assert (await client.post(f"{base}/fields", json={"field_key": "amt", "maps_to": "amount_cents", "source_ref": "x"}, headers=headers)).status_code == 201
    twice = await client.post(f"{base}/fields", json={"field_key": "amt2", "maps_to": "amount_cents", "source_ref": "y"}, headers=headers)
    assert twice.status_code == 409  # hai trường cùng ánh xạ vào một trường hợp đồng


async def test_patterns_fields_and_samples_can_be_changed_and_removed(client, headers):
    fmt = await new_draft(client, headers)
    base = f"{BASE}/{fmt['id']}"
    pattern = (await client.post(f"{base}/patterns", json={"pattern_name": "a", "regex": "a", "transaction_type": "debit"}, headers=headers)).json()
    field = (await client.post(f"{base}/fields", json={"field_key": "k", "source_ref": "x"}, headers=headers)).json()
    sample = (await client.post(f"{base}/samples", json={"sample_text": "s", "expected_fields": {}}, headers=headers)).json()

    changed = (await client.patch(f"{base}/patterns/{pattern['id']}", json={"regex": "b", "transaction_type": None, "is_active": False}, headers=headers)).json()
    assert (changed["regex"], changed["transaction_type"], changed["is_active"], changed["pattern_name"]) == ("b", None, False, "a")
    renamed = (await client.patch(f"{base}/fields/{field['id']}", json={"source_ref": "y", "is_required": True}, headers=headers)).json()
    assert (renamed["source_ref"], renamed["is_required"], renamed["field_key"]) == ("y", True, "k")
    assert (await client.patch(f"{base}/fields/{field['id']}", json={"is_sensitive": True}, headers=headers)).status_code == 422  # trường phụ không nhạy cảm được

    for kind, row in (("patterns", pattern), ("fields", field), ("samples", sample)):
        assert (await client.delete(f"{base}/{kind}/{row['id']}", headers=headers)).status_code == 204
        assert (await client.delete(f"{base}/{kind}/{row['id']}", headers=headers)).status_code == 404
    detail = (await client.get(base, headers=headers)).json()
    assert (detail["patterns"], detail["fields"], detail["samples"]) == ([], [], [])


async def test_activation_lists_every_reason_it_is_refused(client, headers):
    fmt = await new_draft(client, headers)

    refused = await client.post(f"{BASE}/{fmt['id']}/activate", json={}, headers=headers)

    assert refused.status_code == 409
    error = refused.json()["error"]
    assert error["code"] == "format_not_ready" and any("pattern" in d for d in error["details"]) and any("mẫu" in d for d in error["details"])
    assert (await client.get(f"{BASE}/{fmt['id']}/check", headers=headers)).json()["ready"] is False


async def test_a_sample_that_no_longer_matches_blocks_activation(client, headers):
    format_id = await make_ready(client, headers)
    base = f"{BASE}/{format_id}"
    pattern_id = (await client.get(base, headers=headers)).json()["patterns"][0]["id"]
    await client.patch(f"{base}/patterns/{pattern_id}", json={"regex": r"KHAC (?P<tien>[\d,.]+)"}, headers=headers)

    refused = await client.post(f"{base}/activate", json={}, headers=headers)

    assert refused.status_code == 409 and any("không pattern nào khớp" in d for d in refused.json()["error"]["details"])


async def test_provider_key_must_stay_the_same_across_versions_of_a_package(client, headers):
    await new_draft(client, headers)

    other = await client.post(BASE, json={"provider_key": "khac", "package_name": PACKAGE}, headers=headers)

    assert other.status_code == 422 and other.json()["error"]["code"] == "provider_key_mismatch"
    second = await new_draft(client, headers)
    assert second["version"] == 2


async def test_unknown_ids_and_missing_credentials(client, headers, auth_headers):
    missing = "00000000-0000-0000-0000-000000000000"

    gone = await client.get(f"{BASE}/{missing}", headers=headers)
    assert gone.status_code == 404 and gone.json()["error"]["code"] == "not_found"
    assert (await client.get(f"{BASE}/khong-phai-uuid", headers=headers)).status_code == 422
    assert (await client.post(BASE, json={"provider_key": "a", "package_name": "p"}, headers=auth_headers)).status_code == 422  # thiếu người thực hiện
    assert (await client.get(BASE)).status_code in (401, 403)
