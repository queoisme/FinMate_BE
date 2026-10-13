"""Request/response của ``POST /api/v1/chat/parse``.

Route này chỉ HIỂU câu chat và trả quyết định; backend lưu giao dịch (bản nháp, xác nhận, hoàn tác, sửa danh mục) và tự tính tổng.
Số tiền là đồng VND (``amount_cents`` theo quy ước của repo), slug danh mục thuộc ``CATEGORY_SLUGS``.
"""

from __future__ import annotations

from datetime import datetime

from pydantic import BaseModel, Field


class ChatParseRequest(BaseModel):
    text: str = Field(max_length=500)
    user_id_hash: str = Field(min_length=64, max_length=64)


class ChatEntry(BaseModel):
    kind: str  # expense | income | transfer
    amount_cents: int
    description: str | None = None
    merchant: str | None = None
    # Chi tiêu: slug backend, None nếu chưa đoán được (backend hỏi người dùng). Thu nhập luôn "income". Chuyển tiền: None.
    category_slug: str | None = None
    # Loại thu nhập (salary | bonus | gift | side_income | refund | other_income) khi đoán được; backend chỉ có slug "income".
    income_type: str | None = None
    # Đủ chắc để ghi ngay không cần hỏi: chỉ với chi tiêu đã biết danh mục, dưới ngưỡng tiền, và Rasa (nếu có) chắc đó là chi tiêu.
    auto_save_ok: bool = False
    # "Chuyển cho người khác / trả nợ" đã được tính là chi tiêu (tiền rời khỏi người dùng).
    to_others: bool = False
    # Ngày nói trong câu ("hôm qua", "ngày 5/10"), giờ Việt Nam; None khi không nói (backend dùng giờ hiện tại).
    occurred_at: datetime | None = None


class ChatQuery(BaseModel):
    metric: str  # expense | income | balance
    period_key: str  # "thang nay", "hom nay"... (không dấu)
    period_label: str  # "tháng này"
    start: datetime
    end: datetime  # nửa mở: [start, end)
    category_slug: str | None = None
    income_type: str | None = None


class ChatNlu(BaseModel):
    source: str  # rasa | rules
    intent: str | None = None
    confidence: float | None = None


class ChatParseResponse(BaseModel):
    # add_expense | add_income | transfer_money | ask_spending | greet | goodbye | thanks | confirm | deny | cancel |
    # out_of_scope | needs_amount | empty
    intent: str
    reply_text: str | None = None
    entry: ChatEntry | None = None
    # Mọi khoản trong câu; ``entry`` luôn là phần tử đầu. Câu có nhiều khoản thì không khoản nào ``auto_save_ok``.
    entries: list[ChatEntry] = Field(default_factory=list)
    query: ChatQuery | None = None
    # Tổng người dùng nói trong câu ("tổng cộng 60k"), và có khác tổng các khoản đọc được không; để app/backend cảnh báo.
    total_amount: int | None = None
    total_mismatch: bool = False
    nlu: ChatNlu
    # low_confidence | disagree | not_understood: câu đáng để xem lại nếu người dùng đồng ý chia sẻ. Chưa lưu ở đâu.
    unclear_reason: str | None = None
    processing_ms: int | None = None
