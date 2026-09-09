"""GET /api/v1/health — dùng cho docker healthcheck, không cần auth."""

from fastapi import APIRouter

router = APIRouter()


@router.get("/health")
async def health() -> dict[str, str]:
    return {"status": "healthy"}
