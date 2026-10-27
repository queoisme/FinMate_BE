"""Câu chat chưa rõ trên Postgres thật: lưu, trùng, xóa theo người dùng, dọn quá hạn, và nhận vào ``nlu_examples``."""

import uuid

from sqlalchemy import select, text

from app.db.models.nlu_candidate import NluCandidate
from app.db.models.nlu_example import NluExample
from app.services import nlu_candidates as service

USER, OTHER = "a" * 64, "b" * 64


async def add(session, sentence="mai tôi phải trả cho bác kia chưa nhỉ", user=USER, reason="not_understood"):
    return await service.add_pending(session, user_id_hash=user, text=sentence, predicted_intent="out_of_scope", predicted_confidence=0.6, unclear_reason=reason)


async def seed_intents(session):
    """``accept`` chỉ nhận ý định đã có trong nlu_examples."""
    session.add_all([NluExample(text="chào", text_key="chào", intent="greet", split="train", source="seed"),
                     NluExample(text="ăn phở 45k", text_key="ăn phở 45k", intent="add_expense", split="train", source="seed")])
    await session.commit()


async def rows(session):
    return list((await session.scalars(select(NluCandidate))).all())


async def test_a_sentence_is_stored_once_per_user_but_each_user_has_their_own_row(session):
    assert await add(session) is None
    assert await add(session) is None  # gửi lại: không tạo hàng thứ hai
    assert await add(session, user=OTHER) is None
    assert sorted(r.user_id_hash for r in await rows(session)) == [USER, OTHER]
    stored = (await rows(session))[0]
    assert (stored.predicted_intent, stored.status, stored.unclear_reason) == ("out_of_scope", "pending", "not_understood")


async def test_personal_data_and_cancelled_sentences_are_not_stored(session):
    assert await add(session, "gọi mình số 0912 345 678") == "personal_data"
    assert await add(session, reason="cancelled") == "not_learnable"
    assert await rows(session) == []


async def test_a_sentence_already_in_the_training_data_adds_nothing(session):
    await seed_intents(session)
    assert await add(session, "Ăn  phở 45k") == "already_known"  # khóa so trùng bỏ hoa/thường và khoảng trắng thừa
    assert await rows(session) == []


async def test_erase_removes_only_that_users_rows(session):
    await add(session)
    await add(session, user=OTHER)
    assert await service.delete_by_user_hash(session, USER) == 1
    assert [r.user_id_hash for r in await rows(session)] == [OTHER]


async def test_purge_drops_old_pending_and_rejected_rows_but_keeps_recent_ones(session):
    await add(session, "câu cũ chưa duyệt")
    await add(session, "câu cũ bị loại")
    await add(session, "câu mới")
    await session.execute(text("UPDATE nlu_candidates SET created_at = now() - interval '8 days' WHERE text <> 'câu mới'"))
    await session.execute(text("UPDATE nlu_candidates SET status = 'rejected' WHERE text = 'câu cũ bị loại'"))
    await session.commit()
    assert await service.purge_pending(session) == 2
    assert [r.text for r in await rows(session)] == ["câu mới"]


async def test_accept_moves_the_sentence_to_training_data_without_the_user_link(session):
    await seed_intents(session)
    await add(session)
    candidate = (await rows(session))[0]
    assert await service.accept(session, candidate.id, "add_expense", "mai trả cho bác kia [200k](amount) chưa nhỉ") == "accepted"
    assert await rows(session) == []  # không còn gắn với người dùng
    example = (await session.scalars(select(NluExample).where(NluExample.tag == "user_feedback"))).one()
    assert (example.intent, example.source, example.split in ("train", "test")) == ("add_expense", "user_feedback", True)
    assert example.text_key == "mai trả cho bác kia 200k chưa nhỉ" and "[200k](amount)" in example.text


async def test_accept_refuses_unknown_intents_bad_text_and_unknown_ids(session):
    await seed_intents(session)
    await add(session)
    candidate = (await rows(session))[0]
    assert (await service.accept(session, candidate.id, "add_expnse")).startswith("lỗi")  # gõ sai ý định không tự tạo ý định mới
    assert (await service.accept(session, candidate.id, "greet", "gọi 0912 345 678")).startswith("lỗi")
    assert (await service.accept(session, candidate.id, "greet", "mua [cà phê](foo)")).startswith("lỗi")
    assert len(await rows(session)) == 1  # lỗi thì ứng viên vẫn còn chờ
    assert (await service.accept(session, uuid.uuid4(), "greet")).startswith("lỗi")


async def test_reject_keeps_a_tombstone_until_purged_and_a_rejected_row_cannot_be_accepted(session):
    await seed_intents(session)
    await add(session)
    candidate = (await rows(session))[0]
    assert await service.reject(session, candidate.id) is True
    assert (await rows(session))[0].status == "rejected"
    assert (await service.accept(session, candidate.id, "greet")).startswith("lỗi")
    assert await add(session) is None  # gửi lại cùng câu: vẫn chỉ một hàng, vẫn là bị loại
    assert [r.status for r in await rows(session)] == ["rejected"]
