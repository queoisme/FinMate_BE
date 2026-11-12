"""Cấu hình agent (Task 1 của kế hoạch AI Financial Agent): mặc định phải an toàn, không có khóa thì agent tắt."""

import pytest
from pydantic import ValidationError

from app.core.config import Settings


def _settings(**overrides) -> Settings:
    """Dựng Settings độc lập với .env và biến môi trường của máy: chỉ giá trị truyền vào mới tính."""
    base = {
        "database_url": "postgresql+asyncpg://u:p@h/d",
        "redis_url": "redis://h:6379/0",
        "internal_api_key": "test-key",
        "gemini_api_key": None,
        "openrouter_api_key": None,
    }
    return Settings(_env_file=None, **{**base, **overrides})


def test_defaults_keep_agent_disabled_without_key():
    settings = _settings()
    assert settings.agent_provider == "gemini"
    assert settings.agent_enabled is False


def test_agent_is_enabled_only_when_the_chosen_provider_has_a_key():
    assert _settings(gemini_api_key="g").agent_enabled is True
    assert _settings(agent_provider="openrouter", gemini_api_key="g").agent_enabled is False
    assert _settings(agent_provider="openrouter", openrouter_api_key="o").agent_enabled is True


def test_safe_defaults():
    settings = _settings()
    assert settings.agent_verifier_enabled is True  # bộ kiểm chứng số chỉ tắt được khi chạy đánh giá cắt bỏ
    assert settings.agent_rag_enabled is True
    assert settings.agent_max_output_tokens == 1024
    assert settings.agent_embedding_dim == 768


def test_unknown_provider_is_rejected():
    with pytest.raises(ValidationError):
        _settings(agent_provider="skynet")
