"""Request/response của ``POST /api/v1/bill/classify``.

Route này chỉ NHẬN DIỆN và PHÂN LOẠI, giống Rasa: nhận chữ đã lọc dữ liệu cá nhân (tên cửa hàng, các dòng hàng) và trả danh mục.
Không nhận ảnh, không nhận chữ gốc, không lưu gì. Người gọi (backend) giữ kết quả bóc tách và giao dịch.
"""

from __future__ import annotations

from typing import Literal

from pydantic import BaseModel, Field, field_validator, model_validator

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


class ClassifyItemIn(BaseModel):
    """Một món trên hóa đơn: chỉ tên (đã lọc) và món chính nó thuộc về (phụ kiện "+"). Không có số tiền."""

    name: str = Field(max_length=200)
    parent: int | None = None


class BillClassifyItemsRequest(BaseModel):
    """``POST /api/v1/bill/classify-items``: phân loại từng món; ``allow_ai`` là cờ đồng ý chia sẻ tên món với Gemini."""

    items: list[ClassifyItemIn] = Field(min_length=1, max_length=60)
    merchant_name: str | None = Field(default=None, max_length=200)
    user_id_hash: str = Field(min_length=64, max_length=64)
    allow_ai: bool = False

    @model_validator(mode="after")
    def _parent_points_back(self) -> "BillClassifyItemsRequest":
        # parent phải là món đứng TRƯỚC: thừa kế đi một chiều, không có vòng
        for index, item in enumerate(self.items):
            if item.parent is not None and not 0 <= item.parent < index:
                raise ValueError(f"items[{index}].parent phải trỏ tới một món đứng trước")
        return self


class ClassifyItemOut(BaseModel):
    index: int
    # Slug backend, hoặc None khi không nhận ra (backend gom vào nhóm cần xem lại).
    category_slug: str | None = None
    source: Literal["rule", "inherit", "merchant", "ai", "unknown"]


class BillClassifyItemsResponse(BaseModel):
    items: list[ClassifyItemOut]
    # True khi đã gọi Gemini và nó trả lời được.
    ai_used: bool
