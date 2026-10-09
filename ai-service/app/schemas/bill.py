"""Request/response của ``POST /api/v1/bill/classify``.

Route này chỉ NHẬN DIỆN và PHÂN LOẠI, giống Rasa: nhận chữ đã lọc dữ liệu cá nhân (tên cửa hàng, các dòng hàng) và trả danh mục.
Không nhận ảnh, không nhận chữ gốc, không lưu gì. Người gọi (backend) giữ kết quả bóc tách và giao dịch.
"""

from __future__ import annotations

from pydantic import BaseModel, Field, field_validator

from app.db.models.bill import BILL_CHOICE_SLUGS


class BillClassifyRequest(BaseModel):
    user_id_hash: str = Field(min_length=64, max_length=64)
    merchant_name: str | None = Field(default=None, max_length=200)
    # Chữ ĐÃ lọc dữ liệu cá nhân (điện thoại, email, tên khách, tài khoản). Giới hạn để một request không ngốn bộ nhớ.
    lines: list[str] = Field(default_factory=list, max_length=200)


class BillClassifyResponse(BaseModel):
    # Slug backend hợp lệ, hoặc None khi không nhận ra: backend hỏi người dùng thay vì ghi "other" cho có.
    category_slug: str | None = None
    # Nhóm của bộ luật bill (9 nhóm: dining, groceries, utilities...). Giữ lại vì phân biệt "dining"/"groceries" mà backend gộp thành "food".
    local_category: str | None = None
    confidence: float = 0.0
    # memory | brand | keyword | product | model | none: nhận ra nhờ đâu (để gỡ lỗi và để backend biết độ đáng tin).
    source: str = "none"
    processing_ms: int | None = None


class BillLearnRequest(BaseModel):
    """Người dùng đã chọn ``category_slug`` cho cửa hàng này khi lưu một khoản chi từ hóa đơn: lần sau tự gợi ý đúng."""

    user_id_hash: str = Field(min_length=64, max_length=64)
    merchant_name: str = Field(min_length=1, max_length=200)
    category_slug: str

    @field_validator("category_slug")
    @classmethod
    def _expense_slug(cls, value: str) -> str:
        if value not in BILL_CHOICE_SLUGS:
            raise ValueError(f"category_slug phải là một trong {list(BILL_CHOICE_SLUGS)}")
        return value


class BillLearnResponse(BaseModel):
    # False khi tên cửa hàng không dùng được làm khóa (chỉ toàn khoảng trắng); không phải lỗi.
    learned: bool
