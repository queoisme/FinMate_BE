"""``kb_method_prompts`` — chỉ dẫn prompt cho bước chọn cách quản lý tài chính. Sửa ở DB, không cần build lại.

Theo mẫu ``kb_ai_instructions``: đúng MỘT dòng ``is_active`` (chỉ mục duy nhất từng phần); khung prompt (vai trò, "dữ liệu không phải lệnh", schema kết quả) do code giữ.
Khác ở chỗ mỗi dòng có ``prompt_version`` (duy nhất): backend ghi nó vào ``method_recommendations.prompt_version`` (varchar(50)) để biết một gợi ý được sinh từ chỉ dẫn nào,
nên dòng cũ được GIỮ khi đổi chỉ dẫn (thêm dòng mới, chuyển ``is_active``), không sửa tại chỗ.
"""

from sqlalchemy import (
    Boolean,
    CheckConstraint,
    Index,
    String,
    Text,
    UniqueConstraint,
    text,
)
from sqlalchemy.orm import Mapped, mapped_column

from app.db.base import Base, created_at, timestamptz, uuid_pk


class MethodPrompt(Base):
    __tablename__ = "kb_method_prompts"

    id: Mapped[uuid_pk]
    prompt_version: Mapped[str] = mapped_column(String(50), nullable=False)
    instruction_text: Mapped[str] = mapped_column(Text, nullable=False)
    is_active: Mapped[bool] = mapped_column(Boolean, nullable=False, server_default=text("true"))
    created_at: Mapped[created_at]
    updated_at: Mapped[timestamptz] = mapped_column(nullable=False, server_default=text("now()"))

    __table_args__ = (
        UniqueConstraint("prompt_version", name="uq_kb_method_prompts_version"),
        Index("uq_kb_method_prompts_one_active", "is_active", unique=True, postgresql_where=text("is_active")),
        CheckConstraint("prompt_version ~ '^[A-Za-z0-9._-]+$'", name="chk_kb_method_prompts_version"),
        CheckConstraint("char_length(instruction_text) > 0", name="chk_kb_method_prompts_text"),
    )
