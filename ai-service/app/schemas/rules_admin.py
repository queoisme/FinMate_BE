"""Thân request của API quản trị luật (``/api/v1/admin/rules``). Phản hồi là dict thuần (xem ``app.bill.rules_admin``)."""

from typing import Annotated

from pydantic import BaseModel, Field

Reason = Annotated[str | None, Field(default=None, max_length=500)]  # vì sao đổi: hiện trong lịch sử
Short = Annotated[str, Field(max_length=200)]


class BrandUpsert(BaseModel):
    category_slug: str
    display_name: Annotated[str, Field(max_length=200)] = ""
    whole_word: bool = False
    reason: Reason = None


class ActiveBody(BaseModel):
    is_active: bool
    reason: Reason = None


class KeywordCreate(BaseModel):
    kind: str
    category_slug: str
    keyword: Short
    reason: Reason = None


class ItemCachePut(BaseModel):
    name: Short
    category_slug: str
    reason: Reason = None


class InstructionCreate(BaseModel):
    instruction_text: Annotated[str, Field(max_length=30_000)]  # trần thật (20.000) do dịch vụ kiểm; đây chỉ chặn thân request quá khổ
    reason: Reason = None


class RevertBody(BaseModel):
    reason: Reason = None


class ProposedBrand(BaseModel):
    brand_key: Short
    category_slug: str
    display_name: Short = ""
    whole_word: bool = False
    active: bool = True


class ProposedKeyword(BaseModel):
    kind: str
    category_slug: str
    keyword: Short
    active: bool = True


class PreviewRequest(BaseModel):
    merchant_name: Annotated[str | None, Field(max_length=200)] = None
    lines: Annotated[list[Short], Field(max_length=200)] = []
    item_name: Annotated[str | None, Field(max_length=200)] = None
    proposed_brands: Annotated[list[ProposedBrand], Field(max_length=50)] = []
    proposed_keywords: Annotated[list[ProposedKeyword], Field(max_length=50)] = []
