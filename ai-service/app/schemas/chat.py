"""Request/response của ``POST /api/v1/chat/parse``.

Route này chỉ HIỂU câu chat và trả quyết định; backend lưu giao dịch (bản nháp, xác nhận, hoàn tác, sửa danh mục) và tự tính tổng.
Số tiền là đồng VND (``amount_cents`` theo quy ước của repo), slug danh mục thuộc ``CATEGORY_SLUGS``.
"""

from __future__ import annotations

from datetime import datetime
from typing import Literal

from pydantic import BaseModel, Field


class ChatParseRequest(BaseModel):
    text: str = Field(max_length=500)
    user_id_hash: str = Field(min_length=64, max_length=64)
    # Người dùng đã đồng ý gửi cụm sản phẩm cho AI bên thứ ba (backend đọc từ DB mỗi lần). Mặc định tắt: không gửi gì ra ngoài.
    allow_ai: bool = False


class ChatUnclearRequest(BaseModel):
    """Câu chat mà bộ hiểu câu chưa chắc, gửi để NGƯỜI duyệt gán nhãn. Backend chỉ gọi khi người dùng đồng ý chia sẻ dữ liệu huấn luyện (mỗi lần đọc lại từ DB)."""

    text: str = Field(min_length=1, max_length=500)
    user_id_hash: str = Field(min_length=64, max_length=64)
    # Chính ``unclear_reason`` của /chat/parse. "cancelled" (người dùng tự hủy khoản vừa nói) không đáng học: ai-service từ chối nên backend khỏi phải lọc.
    unclear_reason: Literal["low_confidence", "disagree", "not_understood", "cancelled"]
    # Dự đoán của máy, chỉ để người duyệt tham khảo; KHÔNG được coi là nhãn.
    predicted_intent: str | None = Field(default=None, max_length=32)
    predicted_confidence: float | None = Field(default=None, ge=0, le=1)


class ChatUnclearResponse(BaseModel):
    stored: bool
    # Vì sao không lưu: not_learnable | empty | too_long | brackets | personal_data | already_known | error. None khi đã lưu.
    reason: str | None = None


class ChatUnclearEraseRequest(BaseModel):
    user_id_hash: str = Field(min_length=64, max_length=64)


class ChatUnclearEraseResponse(BaseModel):
    deleted: int


class ChatEntry(BaseModel):
    kind: str  # expense | income | transfer
    amount_cents: int
    description: str | None = None
    merchant: str | None = None
    # Chi tiêu: slug backend, None nếu chưa đoán được (backend hỏi người dùng). Thu nhập luôn "income". Chuyển tiền: None.
    category_slug: str | None = None
    # rule | cache | None: "rule" = từ khóa chung quyết định; "cache" = chỉ là gợi ý từ bảng nhớ tên món; None = chưa biết.
    category_source: str | None = None
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
    # Hỏi về HAI danh mục trở lên ("giải trí và mua sắm"): mọi slug được gọi tên (phần tử đầu = ``category_slug``). Rỗng = chỉ xét ``category_slug``.
    category_slugs: list[str] = Field(default_factory=list)
    income_type: str | None = None


class ChatGoal(BaseModel):
    """Bản nháp mục tiêu tiết kiệm (intent ``create_goal``). Không bao giờ tự tạo: app hỏi xác nhận rồi gọi ``POST /saving-goals``."""

    name: str
    # Người dùng không nói tên nên ``name`` là tên mặc định; app cho sửa ngay trên thẻ.
    name_is_default: bool = False
    target_cents: int  # đồng VND
    # Hạn chốt 23:59:59 giờ Việt Nam; None = không nói hạn (hợp lệ).
    deadline: datetime | None = None
    # Cụm người dùng nói ("2 tháng sau"): app hiện kèm ngày cụ thể để người dùng soát.
    deadline_text: str | None = None


class ChatNlu(BaseModel):
    source: str  # rasa | rules
    intent: str | None = None
    confidence: float | None = None


class ChatParseResponse(BaseModel):
    # add_expense | add_income | transfer_money | ask_spending | greet | goodbye | thanks | confirm | deny | cancel |
    # out_of_scope | needs_amount | create_goal | empty   (create_goal không có ``goal`` thì ``reply_text`` là câu hỏi lại)
    intent: str
    reply_text: str | None = None
    entry: ChatEntry | None = None
    # Mọi khoản trong câu; ``entry`` luôn là phần tử đầu. Câu có nhiều khoản thì không khoản nào ``auto_save_ok``.
    entries: list[ChatEntry] = Field(default_factory=list)
    query: ChatQuery | None = None
    # Tổng người dùng nói trong câu ("tổng cộng 60k"), và có khác tổng các khoản đọc được không; để app/backend cảnh báo.
    total_amount: int | None = None
    total_mismatch: bool = False
    # Vế không có số tiền trong câu nhiều khoản ("... và cà phê"): không thành khoản, để app hỏi lại.
    skipped: list[str] = Field(default_factory=list)
    goal: ChatGoal | None = None
    nlu: ChatNlu
    # low_confidence | disagree | not_understood: câu đáng để xem lại nếu người dùng đồng ý chia sẻ. Chưa lưu ở đâu.
    unclear_reason: str | None = None
    processing_ms: int | None = None
