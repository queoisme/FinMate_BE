"""Định dạng thông báo theo nhà cung cấp (migration 0025): seed, bộ khớp chỉ dùng bản active, cổng kích hoạt, nhân bản và ràng buộc, trên Postgres thật."""

import uuid

import pytest
from sqlalchemy import select, text
from sqlalchemy.exc import IntegrityError

from app.core.anonymizer import sha256_hex
from app.db.models.bill import BillRuleAudit
from app.db.models.provider_format import ProviderFormat, ProviderFormatField, ProviderFormatSample
from app.db.models.provider_pattern import ProviderPattern
from app.db.seed_patterns import seed
from app.services import provider_formats
from app.services.provider_formats import SYSTEM_ACTOR, FormatError
from app.utils.provider_patterns import PatternMatcher

MB = "com.mbmobile"
MB_QR = "TK 0123456789|GD: -75,000VND 09/09/2026 10:28|SD: 2,500,000VND|ND: TT QR HIGHLANDS COFFEE"
ADMIN = sha256_hex("admin-1")


async def one(session, stmt):
    return (await session.execute(stmt)).scalar_one()


async def mb_format(session, version: int = 1) -> ProviderFormat:
    return await one(session, select(ProviderFormat).where(ProviderFormat.package_name == MB, ProviderFormat.version == version))


async def rejected(session, sql: str, **params) -> bool:
    try:
        await session.execute(text(sql), params)
        await session.commit()
        return False
    except IntegrityError:
        await session.rollback()
        return True


async def test_seed_builds_one_active_format_per_package_with_fields_and_samples(seeded_session):
    session = seeded_session
    formats = (await session.execute(select(ProviderFormat))).scalars().all()
    assert len(formats) == 5 and {f.status for f in formats} == {"active"} and {f.version for f in formats} == {1} and {f.created_by for f in formats} == {SYSTEM_ACTOR}

    fields = {(f.field_key, f.maps_to, f.is_required) for f in (await session.execute(select(ProviderFormatField).where(ProviderFormatField.format_id == (await mb_format(session)).id))).scalars()}
    assert ("amount", "amount_cents", True) in fields and ("merchant", "merchant_name", False) in fields

    assert len((await session.execute(select(ProviderFormatSample))).scalars().all()) == 8  # mỗi pattern mặc định mang mẫu của chính nó
    for fmt in formats:
        assert await provider_formats.check_format(session, fmt.id) == [], fmt.package_name  # mọi định dạng mặc định đều qua cổng


async def test_seed_twice_changes_nothing(seeded_session):
    session = seeded_session
    assert await seed(session) == 0
    assert len((await session.execute(select(ProviderFormat))).scalars().all()) == 5


async def test_matcher_uses_only_active_formats_and_records_which_pattern_matched(seeded_session):
    session = seeded_session
    v1 = await mb_format(session)
    draft = await provider_formats.clone(session, MB, actor_hash=ADMIN)
    await session.commit()

    matched = PatternMatcher(await provider_formats.load_specs(session)).match(MB, MB_QR)
    pattern = await one(session, select(ProviderPattern).where(ProviderPattern.format_id == v1.id, ProviderPattern.pattern_name == "mb_qr_v1"))
    assert matched.pattern_id == pattern.id  # chỉ bản v1 active; bản draft cùng tên pattern không bị lẫn vào

    await provider_formats.archive(session, v1.id, actor_hash=ADMIN)
    await session.commit()
    assert PatternMatcher(await provider_formats.load_specs(session)).match(MB, MB_QR) is None  # không còn bản active: package quay về nhánh generic
    assert draft.status == "draft"


async def test_clone_copies_everything_into_the_next_draft_version(seeded_session):
    session = seeded_session
    v1 = await mb_format(session)

    draft = await provider_formats.clone(session, MB, actor_hash=ADMIN, reason="thêm mẫu mới")
    await session.commit()

    assert (draft.version, draft.status, draft.created_by) == (2, "draft", ADMIN)
    for model in (ProviderPattern, ProviderFormatField, ProviderFormatSample):
        before = len((await session.execute(select(model).where(model.format_id == v1.id))).scalars().all())
        after = len((await session.execute(select(model).where(model.format_id == draft.id))).scalars().all())
        assert before == after > 0
    audit = await one(session, select(BillRuleAudit).where(BillRuleAudit.resource == "provider_format", BillRuleAudit.action == "create"))
    assert (audit.resource_key, audit.actor_hash, audit.reason) == (str(draft.id), ADMIN, "thêm mẫu mới")


async def test_activation_passes_when_every_sample_matches_and_is_audited(seeded_session):
    session = seeded_session
    draft = await provider_formats.clone(session, MB, actor_hash=ADMIN)

    await provider_formats.activate(session, draft.id, actor_hash=ADMIN, reason="kiểm thử")
    await session.commit()

    assert (await mb_format(session, 2)).status == "active" and (await mb_format(session, 1)).status == "active"  # nhiều bản cùng active
    audit = await one(session, select(BillRuleAudit).where(BillRuleAudit.action == "activate"))
    assert (audit.before["status"], audit.after["status"], audit.actor_hash) == ("draft", "active", ADMIN)


async def test_activation_is_refused_with_every_reason_when_a_sample_no_longer_matches(seeded_session):
    session = seeded_session
    draft = await provider_formats.clone(session, MB, actor_hash=ADMIN)
    broken = await one(session, select(ProviderPattern).where(ProviderPattern.format_id == draft.id, ProviderPattern.pattern_name == "mb_qr_v1"))
    broken.regex = r"(?P<amount>ZZZ\d+)"  # sửa regex làm hỏng mẫu cũ: đúng lỗi mà cổng sinh ra để bắt
    wrong = (await session.execute(select(ProviderFormatSample).where(ProviderFormatSample.format_id == draft.id))).scalars().first()
    wrong.expected_fields = {**wrong.expected_fields, "amount_cents": 1}
    await session.flush()

    with pytest.raises(FormatError) as error:
        await provider_formats.activate(session, draft.id, actor_hash=ADMIN)

    assert any("không pattern nào khớp" in p or "mong đợi" in p for p in error.value.problems)
    await session.refresh(draft)
    assert draft.status == "draft"  # bị từ chối thì không đổi gì


async def test_activation_needs_samples_and_only_drafts_can_be_activated(seeded_session):
    session = seeded_session
    draft = await provider_formats.clone(session, MB, actor_hash=ADMIN)
    for sample in (await session.execute(select(ProviderFormatSample).where(ProviderFormatSample.format_id == draft.id))).scalars():
        await session.delete(sample)
    await session.flush()

    with pytest.raises(FormatError, match="chưa có mẫu"):
        await provider_formats.activate(session, draft.id, actor_hash=ADMIN)
    with pytest.raises(FormatError, match="chỉ định dạng draft"):
        await provider_formats.activate(session, (await mb_format(session)).id, actor_hash=ADMIN)  # v1 đã active


async def test_a_field_pointing_at_a_missing_group_blocks_activation(seeded_session):
    session = seeded_session
    draft = await provider_formats.clone(session, MB, actor_hash=ADMIN)
    session.add(ProviderFormatField(format_id=draft.id, field_key="ma_giao_dich", maps_to=None, data_type="text", source_kind="regex_group", source_ref="khong_ton_tai"))
    await session.flush()

    assert any("khong_ton_tai" in p for p in await provider_formats.check_format(session, draft.id))


async def test_a_new_format_with_renamed_groups_and_extra_fields_works_end_to_end(session):
    fmt = ProviderFormat(provider_key="test_bank", package_name="com.test.bank", version=1, status="draft", created_by=ADMIN)
    session.add(fmt)
    await session.flush()
    session.add(ProviderPattern(
        format_id=fmt.id, pattern_name="t1", transaction_type="debit",
        regex=r"GD (?P<tien>[\d,.]+)VND ma (?P<ref>[A-Z]+) tk (?P<acct>\d+)", sample_text="GD 75,000VND ma ABC tk 123456789012",
    ))
    session.add_all([
        ProviderFormatField(format_id=fmt.id, field_key="tien", maps_to="amount_cents", data_type="amount", is_required=True, source_kind="regex_group", source_ref="tien", normalizer="vnd_amount"),
        ProviderFormatField(format_id=fmt.id, field_key="ref_code", maps_to=None, data_type="text", source_kind="regex_group", source_ref="ref"),
        ProviderFormatField(format_id=fmt.id, field_key="channel", maps_to=None, data_type="text", source_kind="literal", source_ref="sms"),
        ProviderFormatSample(format_id=fmt.id, sample_text="GD 75,000VND ma ABC tk 123456789012", expected_fields={"amount_cents": 75000, "ref_code": "ABC", "channel": "sms"}),
    ])
    await session.flush()

    await provider_formats.activate(session, fmt.id, actor_hash=ADMIN)
    await session.commit()

    matched = PatternMatcher(await provider_formats.load_specs(session)).match("com.test.bank", "GD 75,000VND ma ABC tk 123456789012")
    assert matched.amount_cents == 75_000
    assert matched.extra_fields == {"ref_code": "ABC", "channel": "sms"}  # số tài khoản (group "acct") có trong regex nhưng không được khai báo nên không được lưu


async def test_the_analyze_log_records_the_pattern_and_extra_fields(seeded_session, client, auth_headers, user_hash, now):
    session = seeded_session
    request_id = uuid.uuid4()
    response = await client.post(
        "/api/v1/analyze",
        json={"backend_request_id": str(request_id), "user_id_hash": user_hash, "package_name": MB, "notification_title": "MB Bank", "notification_body": MB_QR, "received_at": now.isoformat()},
        headers=auth_headers,
    )
    assert response.status_code == 200

    row = (await session.execute(text("SELECT pattern_id, extra_fields FROM ops_analyze_log WHERE backend_request_id = :r"), {"r": request_id})).one()
    pattern = await one(session, select(ProviderPattern).where(ProviderPattern.pattern_name == "mb_qr_v1"))
    assert row.pattern_id == pattern.id and row.extra_fields is None  # định dạng mặc định không khai báo trường phụ: SQL NULL, không phải JSON null


async def test_table_constraints(seeded_session):
    session = seeded_session
    v1_id = (await mb_format(session)).id  # lấy id trước: rollback làm đối tượng hết hạn
    new_format = "INSERT INTO kb_provider_formats (provider_key, package_name, version, status, created_by) VALUES ('mb_bank', :p, :v, :s, :c)"
    assert await rejected(session, new_format, p=MB, v=1, s="draft", c=ADMIN)  # (package, version) duy nhất
    assert await rejected(session, new_format, p=MB, v=9, s="published", c=ADMIN)  # trạng thái lạ
    assert await rejected(session, new_format, p=MB, v=0, s="draft", c=ADMIN)
    assert await rejected(session, new_format, p=MB, v=9, s="draft", c="not-a-hash")
    assert not await rejected(session, new_format, p=MB, v=9, s="draft", c=ADMIN)

    field = "INSERT INTO kb_provider_format_fields (format_id, field_key, maps_to, data_type, source_kind, source_ref) VALUES (:f, :k, :m, :d, :s, 'x')"
    assert await rejected(session, field, f=v1_id, k="moi", m="amount", d="text", s="regex_group")  # maps_to không thuộc sáu trường hợp đồng
    assert await rejected(session, field, f=v1_id, k="amount", m=None, d="text", s="regex_group")  # field_key trùng trong một định dạng
    assert await rejected(session, field, f=v1_id, k="amount_2", m="amount_cents", d="amount", s="regex_group")  # hai trường cùng ánh xạ vào amount_cents
    assert await rejected(session, field, f=v1_id, k="moi", m=None, d="text", s="derived")
    assert not await rejected(session, field, f=v1_id, k="moi", m=None, d="text", s="regex_group")

    sample = "INSERT INTO kb_provider_format_samples (format_id, sample_text, expected_fields) VALUES (:f, 'x', CAST(:e AS jsonb))"
    assert await rejected(session, sample, f=v1_id, e='[1, 2]')  # expected_fields phải là object


async def test_a_format_that_still_has_patterns_cannot_be_deleted_but_children_follow_their_format(seeded_session):
    session = seeded_session
    v1 = await mb_format(session)
    assert await rejected(session, "DELETE FROM kb_provider_formats WHERE id = :f", f=v1.id)  # RESTRICT: còn pattern

    draft = await provider_formats.clone(session, MB, actor_hash=ADMIN)
    await session.commit()
    await session.execute(text("DELETE FROM kb_provider_patterns WHERE format_id = :f"), {"f": draft.id})
    await session.execute(text("DELETE FROM kb_provider_formats WHERE id = :f"), {"f": draft.id})
    await session.commit()

    left = (await session.execute(text("SELECT count(*) FROM kb_provider_format_fields WHERE format_id = :f"), {"f": draft.id})).scalar_one()
    assert left == 0  # trường và mẫu theo định dạng mà đi (CASCADE)


async def test_the_same_pattern_name_may_exist_in_two_versions_but_not_twice_in_one(seeded_session):
    session = seeded_session
    draft = await provider_formats.clone(session, MB, actor_hash=ADMIN)  # bản sao có lại mb_qr_v1
    await session.commit()
    dup = "INSERT INTO kb_provider_patterns (format_id, pattern_name, regex) VALUES (:f, 'mb_qr_v1', 'x')"
    assert await rejected(session, dup, f=draft.id)
