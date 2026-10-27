"""Câu chat chưa rõ, chờ người duyệt gán nhãn rồi mới thành câu huấn luyện Rasa (``nlu_examples``).

Luồng: backend (khi người dùng đồng ý chia sẻ) gửi câu + dự đoán của máy -> ``add_pending`` từ chối câu có dữ liệu cá nhân -> người duyệt chạy
``scripts/nlu_candidates.py`` và ``accept`` (gán ý định, được sửa lại câu để bỏ tên người) hoặc ``reject``. Nhãn KHÔNG bao giờ lấy từ dự đoán của máy.
Câu được nhận chuyển sang ``nlu_examples`` (``source=user_feedback``, không còn gắn với người dùng) rồi xóa khỏi bảng chờ; câu chưa duyệt hoặc bị loại bị dọn sau
``PENDING_RETENTION_DAYS`` ngày. Không bao giờ ghi nội dung câu vào log.
"""

from __future__ import annotations

import asyncio
import re
import uuid

import structlog
from sqlalchemy import delete, func, select
from sqlalchemy.dialects.postgresql import insert
from sqlalchemy.ext.asyncio import AsyncSession

from app.bill.samples import has_long_digit_run
from app.core.enums import LabelSource, NluCandidateStatus
from app.db.models.nlu_candidate import NluCandidate
from app.db.models.nlu_example import MAX_TEXT_CHARS, NluExample, annotation_problem, default_split, plain_text, text_key

logger = structlog.get_logger(__name__)

PENDING_RETENTION_DAYS = 7  # cùng mức với mẫu huấn luyện hóa đơn chưa nhãn
FEEDBACK_TAG = "user_feedback"  # nhóm của câu đã duyệt trong nlu_examples
NOT_LEARNABLE = frozenset({"cancelled"})  # unclear_reason của câu không đáng học: người dùng tự hủy khoản vừa nói
_EMAIL = re.compile(r"[\w.+-]+@[\w-]+(?:\.[\w-]+)+")
_URL = re.compile(r"https?://|www\.", re.IGNORECASE)


def reject_reason(text: str, unclear_reason: str | None) -> str | None:
    """Vì sao câu này không được lưu (None = được). Từ chối thay vì che: một câu đã dính số điện thoại thì phần còn lại cũng đáng ngờ, và mất một ứng viên rẻ hơn lộ dữ liệu."""
    if unclear_reason in NOT_LEARNABLE:
        return "not_learnable"
    value = text.strip()
    if not value:
        return "empty"
    if len(value) > MAX_TEXT_CHARS or "\n" in value:
        return "too_long"
    if "[" in value or "]" in value:
        return "brackets"  # cú pháp chú thích thực thể của Rasa: câu người dùng không được mang theo
    if has_long_digit_run([value]) or _EMAIL.search(value) or _URL.search(value):
        return "personal_data"
    return None


async def add_pending(
    session: AsyncSession, *, user_id_hash: str, text: str, predicted_intent: str | None, predicted_confidence: float | None, unclear_reason: str
) -> str | None:
    """Lưu một ứng viên. Trả None khi đã lưu (hoặc người này đã gửi đúng câu đó), ngược lại là mã lý do không lưu."""
    reason = reject_reason(text, unclear_reason)
    if reason:
        return reason
    key = text_key(text)
    if await session.scalar(select(NluExample.id).where(NluExample.text_key == key)):
        return "already_known"  # câu đã là dữ liệu huấn luyện/đo: không có gì để học thêm
    await session.execute(
        insert(NluCandidate)
        .values(
            user_id_hash=user_id_hash, text=text.strip(), text_key=key, predicted_intent=predicted_intent,
            predicted_confidence=predicted_confidence, unclear_reason=unclear_reason,
        )
        .on_conflict_do_nothing(index_elements=["user_id_hash", "text_key"])
    )
    await session.commit()
    return None


async def delete_by_user_hash(session: AsyncSession, user_id_hash: str) -> int:
    """Xóa mọi ứng viên của một người dùng (rút đồng ý, xóa tài khoản). Câu đã được nhận không còn gắn với người dùng nên không nằm trong đây."""
    result = await session.execute(delete(NluCandidate).where(NluCandidate.user_id_hash == user_id_hash))
    await session.commit()
    return result.rowcount or 0


async def purge_pending(session: AsyncSession, days: int = PENDING_RETENTION_DAYS) -> int:
    """Xóa ứng viên (đang chờ hoặc đã bị loại) cũ hơn ``days`` ngày."""
    result = await session.execute(delete(NluCandidate).where(NluCandidate.created_at < func.now() - func.make_interval(0, 0, 0, days)))
    await session.commit()
    return result.rowcount or 0


async def purge_loop(factory, interval_seconds: float = 3600) -> None:
    """Dọn ứng viên quá hạn định kỳ (ai-service không có scheduler ngoài). Một lượt lỗi chỉ ghi log; hủy task để dừng."""
    while True:
        try:
            async with factory() as session:
                purged = await purge_pending(session)
            if purged:
                logger.info("nlu_candidates_purged", count=purged)
        except Exception as error:  # noqa: BLE001 - một lượt dọn lỗi không được giết vòng lặp
            logger.warning("nlu_candidates_purge_failed", error_type=type(error).__name__)
        await asyncio.sleep(interval_seconds)


async def list_candidates(session: AsyncSession, status: str = NluCandidateStatus.PENDING.value, limit: int = 50) -> list[NluCandidate]:
    query = select(NluCandidate).where(NluCandidate.status == status).order_by(NluCandidate.created_at).limit(limit)
    return list((await session.scalars(query)).all())


async def accept(session: AsyncSession, candidate_id: uuid.UUID, intent: str, text: str | None = None) -> str:
    """Nhận một ứng viên làm câu huấn luyện/đo. Trả "accepted" hoặc ``"lỗi: ..."``. ``text`` cho người duyệt sửa lại câu (bỏ tên người, địa chỉ).

    Ý định phải là một ý định ĐÃ CÓ trong ``nlu_examples`` (gõ sai tên thì bị từ chối, không tự tạo ý định mới). Tập train/test chia tất định bằng băm của câu."""
    row = await session.get(NluCandidate, candidate_id)
    if row is None or row.status != NluCandidateStatus.PENDING.value:
        return "lỗi: không có ứng viên đang chờ với id này"
    final = (text if text is not None else row.text).strip()
    if reason := (annotation_problem(final) or reject_reason(plain_text(final), None)):  # người duyệt được thêm chú thích thực thể; phần còn lại vẫn qua bộ lọc dữ liệu cá nhân
        return f"lỗi: câu không dùng được ({reason})"
    known = set((await session.scalars(select(NluExample.intent).distinct())).all())
    if intent not in known:
        return f"lỗi: ý định {intent!r} chưa có trong nlu_examples; các ý định có: {sorted(known)}"
    key = text_key(final)
    if await session.scalar(select(NluExample.id).where(NluExample.text_key == key)):
        await session.delete(row)
        await session.commit()
        return "lỗi: câu này đã có trong nlu_examples (đã bỏ ứng viên)"
    session.add(NluExample(text=final, text_key=key, intent=intent, split=default_split(key), tag=FEEDBACK_TAG, source=LabelSource.USER_FEEDBACK.value))
    await session.delete(row)  # câu đã vào dữ liệu huấn luyện, không còn gắn với người dùng
    await session.commit()
    return "accepted"


async def reject(session: AsyncSession, candidate_id: uuid.UUID) -> bool:
    row = await session.get(NluCandidate, candidate_id)
    if row is None:
        return False
    row.status = NluCandidateStatus.REJECTED.value
    await session.commit()
    return True
