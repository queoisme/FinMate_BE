"""Thân request của API quản trị tài liệu cấu hình (``/api/v1/admin/config``). Phản hồi là dict thuần (xem ``app.services.config_documents``)."""

from typing import Annotated, Any

from pydantic import BaseModel, Field


class DraftCreate(BaseModel):
    content: dict[str, Any]  # toàn bộ tài liệu sau khi sửa
    note: Annotated[str, Field(min_length=3, max_length=500)]  # vì sao đổi: hiện trong lịch sử


class ActivateBody(BaseModel):
    expected_active_version: Annotated[int, Field(ge=0)]  # bản đang chạy mà người sửa nhìn thấy (0 = bản mặc định); khác thực tế thì bị từ chối
    force: bool = False  # bỏ qua cổng đo khi xem trước cho thấy tụt độ chính xác (chỉ khi người duyệt chấp nhận)
