"""Đường NỘI BỘ để một dịch vụ khác lấy giá trị key nó phải dùng (OCR Service lấy key Google Cloud Vision). Khác hẳn ``/admin/secrets``: đó là đường cho trang quản trị và KHÔNG BAO GIỜ trả giá trị.

Ba lớp chặn: (1) khóa nội bộ giữa các dịch vụ như mọi route nội bộ; (2) chỉ key khai ``shared_with`` mới trả, key chỉ AI Service dùng (Gemini, OpenRouter) trả 403; (3) ``consumer`` phải khớp
danh sách của key đó. Phản hồi ``no-store`` và giá trị không bao giờ vào log. Đường này không mở ra ngoài Docker network: backend không có route nào chuyển tiếp tới đây."""

from fastapi import APIRouter, Depends, Response

from app.api.deps import verify_internal_api_key
from app.services import secret_store

router = APIRouter(prefix="/internal/secrets", tags=["internal-secrets"], dependencies=[Depends(verify_internal_api_key)])


@router.get("/{name}")
async def get_shared_secret(name: str, consumer: str, response: Response) -> dict:
    """``{"value": "..." | null}``: null nghĩa là chưa có key nào (cả ở kho lẫn biến môi trường) nên người dùng key tự rơi về cách dự phòng của nó."""
    response.headers["Cache-Control"] = "no-store"
    return {"value": secret_store.shared_value(name, consumer)}
