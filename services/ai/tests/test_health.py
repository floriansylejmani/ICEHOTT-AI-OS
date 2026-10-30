import hashlib
import hmac
import json
import os
import asyncio
import time
from uuid import uuid4

import httpx
import pytest
from fastapi.testclient import TestClient
from app.main import (
    app, ContextItem, GeneratePayload, DemoProvider, OpenAiProvider
)

client = TestClient(app)
SECRET = "development-test-only-shared-secret-1234567890"


def signed(data: dict, secret: str = SECRET, seconds_ago: int = 0) -> tuple[bytes, dict]:
    raw = json.dumps(data, separators=(",", ":")).encode()
    timestamp = str(int(time.time()) - seconds_ago)
    digest = hashlib.sha256(raw).hexdigest()
    sig = hmac.new(secret.encode(), f"{timestamp}.{digest}".encode(), hashlib.sha256).hexdigest()
    return raw, {"x-icehott-timestamp": timestamp, "x-icehott-signature": sig,
                 "content-type": "application/json"}


def payload() -> dict:
    return {"prompt": "Tell me something", "context": [
        {"role": "user", "content": "Hello"},
        {"role": "assistant", "content": "Hi"}
    ], "max_output_tokens": 128, "trace_id": str(uuid4())}


def test_health_and_no_public_ai_routes():
    assert client.get("/health/live").json() == {"status": "healthy", "service": "ai", "phase": "Phase 3"}
    for route in ("/chat", "/v1/chat/completions", "/tools/execute", "/docs"):
        assert client.post(route).status_code == 404


def test_disabled_by_default(monkeypatch):
    monkeypatch.setenv("ICEHOTT_AI_SHARED_SECRET", SECRET)
    monkeypatch.delenv("ICEHOTT_AI_ENABLED", raising=False)
    raw, headers = signed(payload())
    response = client.post("/internal/generate", content=raw, headers=headers)
    assert response.status_code == 503


def test_authentication_rejects_untrusted_calls(monkeypatch):
    monkeypatch.setenv("ICEHOTT_AI_SHARED_SECRET", SECRET)
    monkeypatch.setenv("ICEHOTT_AI_ENABLED", "true")
    raw, headers = signed(payload())
    assert client.post("/internal/generate", content=raw).status_code == 401
    assert client.post("/internal/generate", content=raw, headers={**headers, "x-icehott-signature": "0" * 64}).status_code == 401
    expired, expired_headers = signed(payload(), seconds_ago=90)
    assert client.post("/internal/generate", content=expired, headers=expired_headers).status_code == 401


def test_explicit_demo_and_input_validation(monkeypatch):
    monkeypatch.setenv("ICEHOTT_AI_SHARED_SECRET", SECRET)
    monkeypatch.setenv("ICEHOTT_AI_ENABLED", "true")
    monkeypatch.delenv("ICEHOTT_AI_PROVIDER", raising=False)
    raw, headers = signed(payload())
    response = client.post("/internal/generate", content=raw, headers=headers)
    assert response.status_code == 200
    result = response.json()
    assert result["provider"] == "demo-offline"
    assert result["used_tokens"] == 0
    assert "No AI model has been called" in result["answer"]
    invalid, invalid_headers = signed({**payload(), "prompt": "   "})
    assert client.post("/internal/generate", content=invalid, headers=invalid_headers).status_code == 422
    huge, huge_headers = signed({**payload(), "prompt": "a" * 5000})
    assert client.post("/internal/generate", content=huge, headers=huge_headers).status_code == 422


def test_paid_provider_needs_separate_optin(monkeypatch):
    monkeypatch.setenv("ICEHOTT_AI_SHARED_SECRET", SECRET)
    monkeypatch.setenv("ICEHOTT_AI_ENABLED", "true")
    monkeypatch.setenv("ICEHOTT_AI_PROVIDER", "openai")
    monkeypatch.delenv("ICEHOTT_AI_ALLOW_PAID", raising=False)
    raw, headers = signed(payload())
    result = client.post("/internal/generate", content=raw, headers=headers)
    assert result.status_code == 503
    assert "Paid provider" in result.text


def test_openai_adapter_contract_mocked():
    asyncio.run(_openai_adapter_contract_mocked())


async def _openai_adapter_contract_mocked():
    seen = {}
    def handler(request: httpx.Request) -> httpx.Response:
        seen["url"] = str(request.url)
        seen["request"] = json.loads(request.content)
        return httpx.Response(200, json={"choices": [{"message": {"content": "A safe answer"}}],
                                         "usage": {"total_tokens": 42}})
    transport = httpx.MockTransport(handler)
    async with httpx.AsyncClient(transport=transport) as transport_client:
        adapter = OpenAiProvider("fake-unit-test-key", transport_client)
        result = await adapter.generate(GeneratePayload(
            prompt="User prompt", context=[ContextItem(role="user", content="Old input")],
            max_output_tokens=100, trace_id=uuid4()))
    assert result.provider == "openai"
    assert result.used_tokens == 42
    assert seen["url"] == "https://api.openai.com/v1/chat/completions"
    assert seen["request"]["store"] is False
    assert seen["request"]["max_completion_tokens"] == 100
    assert seen["request"]["messages"][-1] == {"role": "user", "content": "User prompt"}


def test_openai_adapter_rejects_missing_usage():
    asyncio.run(_openai_adapter_rejects_missing_usage())


async def _openai_adapter_rejects_missing_usage():
    async with httpx.AsyncClient(transport=httpx.MockTransport(
        lambda request: httpx.Response(200, json={
            "choices": [{"message": {"content": "Answer"}}]}))) as mock:
        with pytest.raises(ValueError):
            await OpenAiProvider("dummy", mock).generate(GeneratePayload(
                prompt="Hi", context=[], max_output_tokens=20, trace_id=uuid4()))
