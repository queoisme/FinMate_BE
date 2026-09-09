"""POST /api/v1/analyze (implemented in Phase 9)."""

from fastapi import APIRouter, Depends

from app.core.security import verify_internal_api_key

router = APIRouter(dependencies=[Depends(verify_internal_api_key)])
