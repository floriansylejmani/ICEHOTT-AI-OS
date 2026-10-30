import hashlib
import hmac
import json
import time
from uuid import uuid4

import httpx
import pytest
from fastapi.testclient import TestClient

from app.main import DemoProvider, GeneratePayload, OpenAiProvider, app, choose_provider


@pytest.fixture
def client(monkeypatch):
    monkeypatch.setenv("ICEHOTT_AI_SHARED_SECRET", "phase3-test-only-32-byte-shared-secret-not-live")
    monkeypatch.setenv("ICEHOTT_AI_ENABLED", "true")
    monkeypatch.setenv("ICEHOTT_AI_PROVIDER", "demo")
    monkeypatch.delenv("ICEHOTT_AI_ALLOW_PAID", raising=False)
    monkeypatch.delenv("OPENAI_API_KEY", raising=False)
    with TestClient(app) as session:
        yield session


def signed(payload: dict, secret="phase3-test-only-32-byte-shared-secret-not-live", age=0):
    raw = json.dumps(payload).encode()
    timestamp = str(int(time.time()) + age)
    digest = hashlib.sha256(raw).hexdigest()
    signature = hmac.new(secret.encode(), f"{timestamp}.{digest}".encode(), hashlib.sha256).hexdigest()
    return raw, {"x-icehott-timestamp": timestamp, "x-icehott-signature": signature,
                 "content-type": "application/json"}


def sample():
    return {"prompt": "Explain tenant isolation", "context": [], "max_output_tokens": 128, "trace_id": str(uuid4())}


def test_no_anonymous_or_forged_access(client):
    assert client.post("/internal/generate", json=sample()).status_code == 401
    raw, headers = signed(sample(), secret="fake-but-32-bytes-long-as-secret-xxx")
    assert client.post("/internal/generate", content=raw, headers=headers).status_code == 401
    raw, headers = signed(sample(), age=-90)
    assert client.post("/internal/generate", content=raw, headers=headers).status_code == 401
    raw, headers = signed(sample())
    changed = raw.replace(b"tenant", b"other!")
    assert client.post("/internal/generate", content=changed, headers=headers).status_code == 401


def test_offline_demo_is_explicit_and_does_not_use_external_provider(client, monkeypatch):
    raw, headers = signed(sample())
    result = client.post("/internal/generate", content=raw, headers=headers)
    assert result.status_code == 200
    assert result.json()["provider"] == "demo-offline"
    assert "DEMO MODE" in result.json()["answer"]
    assert result.json()["used_tokens"] > 0
    monkeypatch.setenv("ICEHOTT_AI_ENABLED", "false")
    assert client.post("/internal/generate", content=raw, headers=headers).status_code == 503


def test_invalid_payloads_fail_closed(client):
    bad = [
        {**sample(), "prompt": "x" * 4001},
        {**sample(), "context": ["x" * 1001]},
        {**sample(), "context": ["a"] * 7},
        {**sample(), "max_output_tokens": 513},
        {**sample(), "trace_id": "not-valid"},
        {**sample(), "unknown": True},
    ]
    for input_data in bad:
        raw, headers = signed(input_data)
        assert client.post("/internal/generate", content=raw, headers=headers).status_code == 422


def test_external_paid_provider_requires_explicit_opt_in(client, monkeypatch):
    monkeypatch.setenv("ICEHOTT_AI_PROVIDER", "openai")
    raw, headers = signed(sample())
    result = client.post("/internal/generate", content=raw, headers=headers)
    assert result.status_code == 503
    monkeypatch.setenv("ICEHOTT_AI_ALLOW_PAID", "true")
    assert client.post("/internal/generate", content=raw, headers=headers).status_code == 503
    monkeypatch.setenv("ICEHOTT_AI_PROVIDER", "unrecognized")
    assert client.post("/internal/generate", content=raw, headers=headers).status_code == 503


def test_provider_contract_uses_fixed_url_no_tools_and_no_storage():
    captured = {}

    def handler(request: httpx.Request):
        captured["url"] = str(request.url)
        captured["body"] = json.loads(request.content)
        captured["auth"] = request.headers["Authorization"]
        return httpx.Response(200, json={"choices": [{"message": {"content": "Concise answer"}}],
                                         "usage": {"total_tokens": 42}})

    import asyncio
    async def exercise():
        async with httpx.AsyncClient(transport=httpx.MockTransport(handler)) as http:
            provider = OpenAiProvider("test-only-token", client=http)
            return await provider.generate(GeneratePayload(**sample()))
    output = asyncio.run(exercise())
    assert output.answer == "Concise answer"
    assert output.used_tokens == 42
    assert output.provider == "openai"
    assert captured["url"] == "https://api.openai.com/v1/chat/completions"
    assert captured["body"]["store"] is False
    assert captured["body"]["max_completion_tokens"] == 128
    assert "tools" not in captured["body"]
    assert captured["auth"] == "Bearer test-only-token"
