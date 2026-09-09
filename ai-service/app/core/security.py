"""Internal API key authentication for requests coming from the Backend."""

from fastapi import Header, HTTPException, status

from app.core.config import settings


async def verify_internal_api_key(authorization: str | None = Header(default=None)) -> None:
    """Verify the ``Authorization: Bearer {key}`` header against INTERNAL_API_KEY."""
    if not authorization or not authorization.startswith("Bearer "):
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Missing or malformed Authorization header",
        )

    token = authorization.removeprefix("Bearer ").strip()
    if token != settings.internal_api_key:
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Invalid internal API key",
        )
