"""Request/response của ``POST /api/v1/bill/classify``.

Route này chỉ NHẬN DIỆN và PHÂN LOẠI, giống Rasa: nhận chữ đã lọc dữ liệu cá nhân (tên cửa hàng, các dòng hàng) và trả danh mục.
Không nhận ảnh, không nhận chữ gốc, không lưu gì. Người gọi (backend) giữ kết quả bóc tách và giao dịch.
"""

from __future__ import annotations

import uuid
from typing import Literal

from pydantic import BaseModel, Field, field_validator, model_validator

from app.services import category_registry


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
        if not category_registry.is_spend(value):
            raise ValueError(f"category_slug phải là một trong {category_registry.spend_list()}")
        return value


class BillLearnResponse(BaseModel):
    # False khi tên cửa hàng không dùng được làm khóa (chỉ toàn khoảng trắng); không phải lỗi.
    learned: bool


class BillSampleRequest(BaseModel):
    """``POST /api/v1/bill/samples``: chữ ĐÃ lọc của một lần quét, gửi CHỈ khi người dùng đồng ý đóng góp dữ liệu (backend kiểm tra cờ)."""

    scan_id: uuid.UUID
    user_id_hash: str = Field(min_length=64, max_length=64)
    merchant_name: str | None = Field(default=None, max_length=200)
    lines: list[str] = Field(min_length=1, max_length=200)
    total_amount: int | None = Field(default=None, ge=0)  # backend vẫn gửi; không còn lưu (không nơi nào dùng)
    predicted_slug: str | None = None
    predicted_source: str | None = Field(default=None, max_length=16)

    @field_validator("predicted_slug")
    @classmethod
    def _known_slug(cls, value: str | None) -> str | None:
        if value is not None and not category_registry.is_spend(value):
            raise ValueError(f"predicted_slug phải là một trong {category_registry.spend_list()}")
        return value


class BillSampleResponse(BaseModel):
    # False khi mẫu bị từ chối (chữ còn số dài như số điện thoại/tài khoản) hoặc mã quét đã thuộc người khác; không phải lỗi.
    stored: bool


class BillSampleLabelRequest(BaseModel):
    """``POST /api/v1/bill/samples/{scan_id}/label``: nhãn cuối người dùng đã chọn khi lưu/sửa khoản chi từ hóa đơn."""

    user_id_hash: str = Field(min_length=64, max_length=64)
    final_slug: str

    @field_validator("final_slug")
    @classmethod
    def _expense_slug(cls, value: str) -> str:
        if not category_registry.is_spend(value):
            raise ValueError(f"final_slug phải là một trong {category_registry.spend_list()}")
        return value


class BillSampleLabelResponse(BaseModel):
    labeled: bool


class BillSampleDiscardRequest(BaseModel):
    """``POST /api/v1/bill/samples/{scan_id}/discard``: người dùng chọn danh mục tự tạo/thu nhập nên mẫu của lần quét này không còn nhãn đáng học."""

    user_id_hash: str = Field(min_length=64, max_length=64)


class BillSampleDiscardResponse(BaseModel):
    discarded: bool


class BillItemVote(BaseModel):
    name: str = Field(min_length=1, max_length=200)  # tên món ĐÃ lọc (không giá, không dữ liệu cá nhân)
    category_slug: str

    @field_validator("category_slug")
    @classmethod
    def _expense_slug(cls, value: str) -> str:
        if not category_registry.is_spend(value):
            raise ValueError(f"category_slug phải là một trong {category_registry.spend_list()}")
        return value


class BillItemFeedbackRequest(BaseModel):
    """``POST /api/v1/bill/items/feedback``: danh mục người dùng đã chọn cho từng món khi lưu hóa đơn. Gửi CHỈ khi người dùng đã đồng ý đóng góp dữ liệu (backend kiểm tra cờ)."""

    user_id_hash: str = Field(min_length=64, max_length=64)
    items: list[BillItemVote] = Field(min_length=1, max_length=50)


class BillItemFeedbackResponse(BaseModel):
    # Số phiếu đã ghi. Ít hơn số món gửi khi có món tên không dùng được (giống mã hàng, quá ngắn/dài, còn số dài); không phải lỗi.
    recorded: int


class BillSampleEraseRequest(BaseModel):
    """``POST /api/v1/bill/samples/erase``: xóa MỌI mẫu huấn luyện VÀ phiếu món của một người dùng (rút đồng ý đóng góp dữ liệu, hoặc xóa tài khoản)."""

    user_id_hash: str = Field(min_length=64, max_length=64)


class BillSampleEraseResponse(BaseModel):
    deleted: int  # số mẫu huấn luyện đã xóa
    item_votes: int = 0  # số phiếu món đã xóa


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
    source: Literal["rule", "cache", "inherit", "merchant", "ai", "unknown"]


class BillClassifyItemsResponse(BaseModel):
    items: list[ClassifyItemOut]
    # True khi đã gọi Gemini và nó trả lời được.
    ai_used: bool
