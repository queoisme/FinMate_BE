"""FastAPI entry point."""

from fastapi import FastAPI

from app.api.v1 import feedback, health, pipeline
from app.core.logging import configure_logging

configure_logging()

app = FastAPI(title="FinMate AI Service")

app.include_router(health.router, prefix="/api/v1", tags=["health"])
app.include_router(pipeline.router, prefix="/api/v1", tags=["pipeline"])
app.include_router(feedback.router, prefix="/api/v1", tags=["feedback"])
