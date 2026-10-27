"""Hợp đồng đồng bộ danh mục từ backend sang AI Service (``PUT /api/v1/admin/categories/sync``)."""

from typing import Literal

from pydantic import BaseModel, Field, model_validator


class CategoryIn(BaseModel):
    slug: str = Field(pattern=r"^[a-z_]{1,32}$")  # cùng quy tắc với CHECK của kb_categories
    label: str = Field(min_length=1, max_length=100)
    kind: Literal["expense", "income"]  # "legacy" là của 9 nhóm cũ, không đặt được qua API
    is_active: bool = True


class CategorySyncRequest(BaseModel):
    """Danh sách là CỘNG THÊM: danh mục không có trong danh sách giữ nguyên; muốn tắt một danh mục thì gửi nó với ``is_active = false`` (không bao giờ xóa)."""

    categories: list[CategoryIn] = Field(min_length=1, max_length=200)
    taxonomy_version: int | None = Field(default=None, ge=1)
    reason: str | None = Field(default=None, max_length=500)

    @model_validator(mode="after")
    def _unique_slugs(self) -> "CategorySyncRequest":
        slugs = [c.slug for c in self.categories]
        if len(slugs) != len(set(slugs)):
            raise ValueError("slug bị lặp trong danh sách")
        return self


class CategorySyncResponse(BaseModel):
    created: int
    updated: int
    unchanged: int
