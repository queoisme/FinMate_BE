"""FastAPI entry point."""

import asyncio
from contextlib import asynccontextmanager

import structlog
from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse

from app.api.v1.bill import router as bill_router
from app.bill import slug_model
from app.bill.rules_admin import RuleError
from app.bill.seed_defaults import seed_bill_knowledge
from app.bill.samples import purge_loop
from app.api.v1.chat import router as chat_router
from app.api.v1.feedback import router as feedback_router
from app.api.v1.health import router as health_router
from app.api.v1.ocr import router as ocr_router
from app.api.v1.pipeline import router as pipeline_router
from app.api.v1.rules_admin import router as rules_admin_router
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
    try:  # thương hiệu/từ khóa mặc định trong code -> DB (chỉ thêm, DB thắng); lỗi thì DB giữ dữ liệu cũ, hệ thống vẫn chạy
        if settings.seed_bill_knowledge:
            async with AsyncSessionLocal() as session:
                logger.info("bill_knowledge_seeded", **await seed_bill_knowledge(session))
    except Exception as error:  # noqa: BLE001
        logger.error("bill_knowledge_seed_failed", error_type=type(error).__name__)
    try:  # mô hình nhóm hóa đơn đã duyệt (nếu có); không có hoặc lỗi thì hệ thống chạy bằng luật như cũ
        async with AsyncSessionLocal() as session:
            slug_model.set_active(await slug_model.load_active(session))
        logger.info("slug_model_loaded", active=slug_model.active() is not None)
    except Exception as error:  # noqa: BLE001
        logger.error("slug_model_startup_failed", error_type=type(error).__name__)
    purge_task = asyncio.create_task(purge_loop(AsyncSessionLocal))  # mẫu huấn luyện chưa nhãn quá 7 ngày bị xóa định kỳ
    yield
    purge_task.cancel()


app = FastAPI(title="FinMate AI Service", lifespan=lifespan)

app.include_router(health_router, prefix="/api/v1", tags=["health"])
app.include_router(pipeline_router, prefix="/api/v1", tags=["pipeline"])
app.include_router(feedback_router, prefix="/api/v1", tags=["feedback"])
app.include_router(stats_router, prefix="/api/v1", tags=["stats"])
app.include_router(ocr_router, prefix="/api/v1", tags=["ocr"])
app.include_router(chat_router, prefix="/api/v1", tags=["chat"])
app.include_router(bill_router, prefix="/api/v1", tags=["bill"])
app.include_router(rules_admin_router, prefix="/api/v1")


@app.exception_handler(RuleError)
async def rule_error_handler(_request: Request, error: RuleError) -> JSONResponse:
    """Lỗi nghiệp vụ của API quản trị luật: JSON ``{"error": {"code", "message"}}`` cùng mã HTTP (404/409/422) để backend ánh xạ lại."""
    return JSONResponse(status_code=error.status, content={"error": {"code": error.code, "message": error.message}})
