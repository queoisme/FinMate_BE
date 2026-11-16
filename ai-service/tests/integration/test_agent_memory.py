"""Bộ nhớ dài hạn của agent (Task 9a): sở thích ngắn của người dùng, lưu theo SHA-256 id, chỉ người dùng đó xem/sửa/xóa được. Backend giữ danh sách khóa cho phép; ai-service giữ dạng khóa, độ dài và cô lập theo hash."""

import hashlib

import pytest

ME = hashlib.sha256(b"me").hexdigest()
OTHER = hashlib.sha256(b"other").hexdigest()


async def put(client, headers, user_hash, key, value):
    return await client.put(f"/api/v1/agent/memory/{key}", json={"user_id_hash": user_hash, "value": value}, headers=headers)


async def listing(client, headers, user_hash):
    response = await client.get("/api/v1/agent/memory", params={"user_id_hash": user_hash}, headers=headers)
    assert response.status_code == 200
    return {item["key"]: item["value"] for item in response.json()}


async def test_a_new_user_has_an_empty_memory(client, auth_headers):
    assert await listing(client, auth_headers, ME) == {}


async def test_put_then_list_returns_the_value_and_a_second_put_replaces_it(client, auth_headers):
    assert (await put(client, auth_headers, ME, "preferred_method", "phong bì")).status_code == 200
    assert (await put(client, auth_headers, ME, "preferred_method", "50/30/20")).status_code == 200

    assert await listing(client, auth_headers, ME) == {"preferred_method": "50/30/20"}


async def test_one_user_never_sees_or_overwrites_anothers_memory(client, auth_headers):
    await put(client, auth_headers, ME, "preferred_method", "của tôi")
    await put(client, auth_headers, OTHER, "preferred_method", "của người khác")

    assert await listing(client, auth_headers, ME) == {"preferred_method": "của tôi"}
    assert await listing(client, auth_headers, OTHER) == {"preferred_method": "của người khác"}

    await client.delete("/api/v1/agent/memory", params={"user_id_hash": ME}, headers=auth_headers)
    assert await listing(client, auth_headers, OTHER) == {"preferred_method": "của người khác"}


async def test_delete_one_key_and_delete_everything(client, auth_headers):
    await put(client, auth_headers, ME, "preferred_method", "a")
    await put(client, auth_headers, ME, "communication_tone", "b")

    one = await client.delete("/api/v1/agent/memory/preferred_method", params={"user_id_hash": ME}, headers=auth_headers)
    assert one.json() == {"deleted": 1}
    assert await listing(client, auth_headers, ME) == {"communication_tone": "b"}

    everything = await client.delete("/api/v1/agent/memory", params={"user_id_hash": ME}, headers=auth_headers)
    assert everything.json() == {"deleted": 1}
    assert await listing(client, auth_headers, ME) == {}


async def test_deleting_what_is_not_there_is_not_an_error(client, auth_headers):
    response = await client.delete("/api/v1/agent/memory/preferred_method", params={"user_id_hash": ME}, headers=auth_headers)

    assert response.status_code == 200 and response.json() == {"deleted": 0}


@pytest.mark.parametrize("key", ["Preferred", "1abc", "ab", "co dau cach", "x" * 49])
async def test_a_malformed_key_is_422(client, auth_headers, key):
    assert (await put(client, auth_headers, ME, key, "v")).status_code in (404, 422)


async def test_values_are_limited_to_200_characters_and_must_not_be_blank(client, auth_headers):
    assert (await put(client, auth_headers, ME, "preferred_method", "x" * 201)).status_code == 422
    assert (await put(client, auth_headers, ME, "preferred_method", "   ")).status_code == 422
    assert (await put(client, auth_headers, ME, "preferred_method", "x" * 200)).status_code == 200


async def test_a_real_user_id_instead_of_a_hash_is_rejected(client, auth_headers):
    assert (await put(client, auth_headers, "3f2504e0-4f89-11d3-9a0c-0305e82c3301", "preferred_method", "v")).status_code == 422
    assert (await client.get("/api/v1/agent/memory", params={"user_id_hash": "3f2504e0-4f89-11d3-9a0c-0305e82c3301"}, headers=auth_headers)).status_code == 422


async def test_memory_routes_require_the_internal_api_key(client):
    assert (await client.get("/api/v1/agent/memory", params={"user_id_hash": ME})).status_code in (401, 403)
    assert (await client.put("/api/v1/agent/memory/preferred_method", json={"user_id_hash": ME, "value": "v"})).status_code in (401, 403)
