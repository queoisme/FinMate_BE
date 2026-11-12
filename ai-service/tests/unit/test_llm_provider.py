"""Cổng LLM dùng chung của agent (Task 6): trả JSON + số token, đổi nhà cung cấp bằng cấu hình, không bao giờ log nội dung lời nhắc."""

import json

import httpx
import pytest
import structlog

from app.agent.llm.gemini_provider import GeminiProvider
from app.agent.llm.openrouter_provider import OpenRouterProvider
from app.agent.llm.provider import ChainProvider, LlmFailed, LlmResult, build_provider
from app.core.config import Settings

SCHEMA = {
    "type": "object",
    "properties": {"intent": {"type": "string", "enum": ["forecast_goal", "other"]}},
    "required": ["intent"],
    "additionalProperties": False,
}
SECRET_PROMPT = "SECRET-PROMPT-bi-mat-123"


def gemini_ok(payload, prompt_tokens=120, output_tokens=30) -> httpx.Response:
    return httpx.Response(
        200,
        json={
            "candidates": [{"content": {"parts": [{"text": json.dumps(payload)}]}}],
            "usageMetadata": {"promptTokenCount": prompt_tokens, "candidatesTokenCount": output_tokens},
        },
    )


def openrouter_ok(payload, prompt_tokens=90, output_tokens=20) -> httpx.Response:
    return httpx.Response(
        200,
        json={"choices": [{"message": {"content": json.dumps(payload)}}], "usage": {"prompt_tokens": prompt_tokens, "completion_tokens": output_tokens}},
    )


def gemini(transport, **overrides) -> GeminiProvider:
    return GeminiProvider(api_key=overrides.pop("api_key", "g-key"), model="gemini-test", timeout=5.0, transport=transport, retry_delay=0.0, **overrides)


def openrouter(transport, model="deepseek/deepseek-v3.2") -> OpenRouterProvider:
    return OpenRouterProvider(api_key="o-key", model=model, timeout=5.0, transport=transport)


async def test_gemini_returns_parsed_json_and_usage():
    seen = {}

    def handler(request: httpx.Request) -> httpx.Response:
        seen["url"] = str(request.url)
        seen["key"] = request.headers["x-goog-api-key"]
        seen["body"] = json.loads(request.content)
        return gemini_ok({"intent": "forecast_goal"})

    result = await gemini(httpx.MockTransport(handler)).generate_json("SYSTEM", "USER", SCHEMA, max_output_tokens=256)

    assert result == LlmResult(data={"intent": "forecast_goal"}, input_tokens=120, output_tokens=30, provider="gemini", model="gemini-test")
    assert "gemini-test" in seen["url"] and seen["key"] == "g-key"
    body = seen["body"]
    assert body["systemInstruction"]["parts"][0]["text"] == "SYSTEM"
    assert body["contents"][0]["parts"][0]["text"] == "USER"
    config = body["generationConfig"]
    assert config["maxOutputTokens"] == 256  # trần token đầu ra được tôn trọng
    assert config["temperature"] == 0
    assert config["responseSchema"]["type"] == "OBJECT"  # Gemini dùng kiểu viết hoa
    assert config["responseSchema"]["properties"]["intent"]["type"] == "STRING"
    assert "additionalProperties" not in config["responseSchema"]  # Gemini không nhận khóa này


async def test_gemini_retries_once_on_503_then_succeeds():
    calls = []

    def handler(request: httpx.Request) -> httpx.Response:
        calls.append(1)
        return httpx.Response(503) if len(calls) == 1 else gemini_ok({"intent": "other"})

    result = await gemini(httpx.MockTransport(handler)).generate_json("s", "u", SCHEMA, 64)

    assert result.data == {"intent": "other"}
    assert len(calls) == 2


async def test_gemini_raises_llm_failed_on_invalid_json():
    transport = httpx.MockTransport(lambda r: httpx.Response(200, json={"candidates": [{"content": {"parts": [{"text": "đây không phải JSON"}]}}]}))

    with pytest.raises(LlmFailed) as error:
        await gemini(transport).generate_json("s", "u", SCHEMA, 64)

    assert error.value.args[0] == "bad_json"


async def test_gemini_rejects_json_that_is_not_an_object():
    transport = httpx.MockTransport(lambda r: gemini_ok([1, 2, 3]))

    with pytest.raises(LlmFailed) as error:
        await gemini(transport).generate_json("s", "u", SCHEMA, 64)

    assert error.value.args[0] == "not_object"


async def test_http_error_becomes_a_fixed_code():
    transport = httpx.MockTransport(lambda r: httpx.Response(400, text="lỗi chứa nội dung lời nhắc"))

    with pytest.raises(LlmFailed) as error:
        await gemini(transport).generate_json("s", "u", SCHEMA, 64)

    assert error.value.args[0] == "http_400"  # mã cố định, không mang thân phản hồi


async def test_gemini_without_a_key_is_unavailable_and_makes_no_call():
    def handler(request):
        raise AssertionError("không được gọi mạng khi thiếu khóa")

    with pytest.raises(LlmFailed) as error:
        await gemini(httpx.MockTransport(handler), api_key=None).generate_json("s", "u", SCHEMA, 64)

    assert error.value.args[0] == "unavailable"


async def test_openrouter_parses_content_usage_and_denies_data_collection():
    seen = {}

    def handler(request: httpx.Request) -> httpx.Response:
        seen["auth"] = request.headers["authorization"]
        seen["body"] = json.loads(request.content)
        return openrouter_ok({"intent": "forecast_goal"})

    result = await openrouter(httpx.MockTransport(handler)).generate_json("SYSTEM", "USER", SCHEMA, max_output_tokens=300)

    assert result == LlmResult(data={"intent": "forecast_goal"}, input_tokens=90, output_tokens=20, provider="openrouter", model="deepseek/deepseek-v3.2")
    body = seen["body"]
    assert seen["auth"] == "Bearer o-key"
    assert body["max_tokens"] == 300
    assert body["messages"][0] == {"role": "system", "content": "SYSTEM"}
    assert body["messages"][1] == {"role": "user", "content": "USER"}
    assert body["response_format"]["json_schema"]["schema"] == SCHEMA  # JSON Schema nguyên bản, không đổi kiểu
    assert body["provider"]["data_collection"] == "deny"  # dữ liệu người dùng không được phép bị bên cung cấp lưu hay dùng huấn luyện


async def test_chain_falls_back_to_backup_provider_on_503():
    primary = gemini(httpx.MockTransport(lambda r: httpx.Response(503)))
    backup = openrouter(httpx.MockTransport(lambda r: openrouter_ok({"intent": "other"})))

    result = await ChainProvider([primary, backup]).generate_json("s", "u", SCHEMA, 64)

    assert result.provider == "openrouter"
    assert result.data == {"intent": "other"}


async def test_chain_raises_the_last_error_when_every_provider_fails():
    primary = gemini(httpx.MockTransport(lambda r: httpx.Response(503)))
    backup = openrouter(httpx.MockTransport(lambda r: httpx.Response(429)))

    with pytest.raises(LlmFailed) as error:
        await ChainProvider([primary, backup]).generate_json("s", "u", SCHEMA, 64)

    assert error.value.args[0] == "http_429"


async def test_never_logs_prompt_text():
    transport = httpx.MockTransport(lambda r: httpx.Response(500, text=SECRET_PROMPT))
    with structlog.testing.capture_logs() as logs:
        with pytest.raises(LlmFailed):
            await gemini(transport).generate_json(SECRET_PROMPT, SECRET_PROMPT, SCHEMA, 64)

    assert SECRET_PROMPT not in json.dumps(logs, default=str, ensure_ascii=False)


def _settings(**overrides) -> Settings:
    base = {"database_url": "postgresql+asyncpg://u:p@h/d", "redis_url": "redis://h", "internal_api_key": "k", "gemini_api_key": "g", "openrouter_api_key": "o"}
    return Settings(_env_file=None, **{**base, **overrides})


def test_build_provider_follows_the_setting():
    getter = lambda name: (lambda: {"gemini_api_key": "g", "openrouter_api_key": "o"}[name])  # noqa: E731

    assert isinstance(build_provider(_settings(), getter), GeminiProvider)
    assert isinstance(build_provider(_settings(agent_provider="openrouter", agent_model="deepseek/deepseek-v3.2"), getter), OpenRouterProvider)


def test_build_provider_adds_a_backup_only_when_one_is_configured():
    getter = lambda name: (lambda: "k")  # noqa: E731

    chain = build_provider(_settings(agent_backup_model="deepseek/deepseek-v3.2"), getter)

    assert isinstance(chain, ChainProvider)
    assert [type(p) for p in chain.providers] == [GeminiProvider, OpenRouterProvider]
