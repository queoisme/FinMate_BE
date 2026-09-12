"""FastAPI entry point."""

from contextlib import asynccontextmanager

import structlog
from fastapi import FastAPI

from app.api.v1.feedback import router as feedback_router
from app.api.v1.health import router as health_router
from app.api.v1.pipeline import router as pipeline_router
from app.api.v1.stats import router as stats_router
from app.core.config import settings
from app.core.logging import configure_logging
from app.db.seed_patterns import seed
from app.db.session import AsyncSessionLocal
from app.pipeline.orchestrator import pipeline

configure_logging()
logger = structlog.get_logger(__name__)


@asynccontextmanager
async def lifespan(app: FastAPI):
    """Seed pattern rồi nạp sẵn pattern + model trước khi nhận request đầu tiên.

    Lỗi ở đây KHÔNG được làm sập tiến trình: pipeline.refresh() sẽ tự thử lại ở request
    đầu tiên, còn /health cần sống để orchestrator biết container đã lên. Sập ở lifespan
    chỉ đổi một sự cố DB tạm thời thành một vòng lặp restart.
    """
    try:
        async with AsyncSessionLocal() as session:
            if settings.seed_provider_patterns:
                await seed(session)
            await pipeline.refresh(session, force=True)
    except Exception as error:  # noqa: BLE001 - cố ý bắt rộng, xem docstring
        logger.error("startup_warmup_failed", error=str(error))
    yield


app = FastAPI(title="FinMate AI Service", lifespan=lifespan)

app.include_router(health_router, prefix="/api/v1", tags=["health"])
app.include_router(pipeline_router, prefix="/api/v1", tags=["pipeline"])
app.include_router(feedback_router, prefix="/api/v1", tags=["feedback"])
app.include_router(stats_router, prefix="/api/v1", tags=["stats"])
