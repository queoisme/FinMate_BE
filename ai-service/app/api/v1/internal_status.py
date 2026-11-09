"""GET /api/v1/internal/status: trạng thái nhà cung cấp cho backend giám sát. Cần khóa nội bộ. Không có khóa, nội dung gọi hay phản hồi."""
from fastapi import APIRouter, Depends

from app.api.deps import verify_internal_api_key
from app.api.v1.bill import get_gemini_client
from app.bill.provider_health import backup_health, gemini_health

router = APIRouter(dependencies=[Depends(verify_internal_api_key)])


@router.get("/internal/status")
async def internal_status() -> dict:
    client = get_gemini_client()
    return {
        "gemini": gemini_health.snapshot(client.primary_configured),
        "gemini_backup": backup_health.snapshot(client.backup_configured),
    }
