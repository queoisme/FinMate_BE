"""SHA-256 hashing helpers for user_id and body anonymization (Phase 9 pipeline)."""

import hashlib


def hash_user_id(user_id: str) -> str:
    return hashlib.sha256(user_id.encode("utf-8")).hexdigest()
