"""Shared FastAPI dependencies (DB session, internal API key auth)."""

from app.core.security import verify_internal_api_key
from app.db.session import get_db

__all__ = ["get_db", "verify_internal_api_key"]
