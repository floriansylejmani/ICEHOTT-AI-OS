# ICEHOTT AI OS V2 — Phase 3 AI Core

**Implementation:** Multi-tenant AI conversation persistence, authenticated internal runtime, audited quota admission, provider abstraction, safe browser UI and evaluation tests. **Offline demo is the default provider. No external paid calls are enabled by this work.**

## Architecture

Browser /dashboard → same-origin Next.js session BFF → authenticated .NET API → PostgreSQL workspace membership and quota reservation → signed HMAC call to loopback FastAPI → provider abstraction (demo default, optional explicit OpenAI adapter). The browser never receives provider secrets and cannot call the internal AI service directly.

1. AI endpoints are **absent** unless the operator explicitly sets `Ai:Enabled=true` on .NET. It requires `Auth:Enabled=true`, 32+ byte shared secret, bounded quotas, an approved runtime URL. In Development, runtime URL must be HTTP loopback; outside Development, it must be HTTPS.
2. FastAPI requires `ICEHOTT_AI_ENABLED=true`, `ICEHOTT_AI_SHARED_SECRET` of at least 32 bytes, HMAC-SHA256 signature over timestamp + SHA256(raw body), ±30-second freshness, bounded Pydantic validation before calling a provider. FastAPI binds only 127.0.0.1 in the documented local workflow. Do not expose the internal endpoint publicly.
3. `ICEHOTT_AI_PROVIDER=demo` is the default and produces an honest **DEMO MODE** reply, never pretending to have generated real AI analysis. `ICEHOTT_AI_PROVIDER=openai` is only available when the operator **also** sets `ICEHOTT_AI_ALLOW_PAID=true` and `OPENAI_API_KEY`. All three conditions must be explicit; nothing in this PR sets these values. The external adapter uses a fixed HTTPS OpenAI endpoint, no browser-selected URL, no tools, no connectors, no automatic storage and response/timeout bounds. Do not enable paid calls until security, replay and budget review.
4. `AiConversationService` verifies actor via active JWT and PostgreSQL membership. Viewer+ may list conversations/messages/usage. Member+ may create chats and send; no browser-provided actor/user ID or API key is used.
5. Each user turn supplies unique `requestId` (UUID) for server-side idempotency. DB uniqueness on (workspace_id, request_id). Under a workspace row lock, API reserves conservative token units including prompt, bounded prior context and maximal output, and checks the per-UTC-day request/token quota. The returned provider usage cannot exceed that reservation. It commits a pending ledger attempt and **only then** calls FastAPI. Completed retries return the same stored response without another provider call. In-flight or failed attempt retries return HTTP 409.
6. On success, user+assistant message records, used tokens, provider label and trace ID are committed as one transaction with a completion audit action. On runtime failure, the attempt is marked Failed, provider errors are sanitized (503), no conversation turn is persisted, and the reservation **still counts**. This avoids uncontrolled free retry-spend loops. Crash recovery of indefinitely reserved attempts is a Phase 6/operational acceptance issue; they do not automatically call the provider again.
7. PostgreSQL composite (workspace_id, conversation_id) foreign keys enforce consistent tenant association for AI messages and attempts. Conversation / attempt / audit tables are append-only in core flows. Per-workspace max 100 conversations, last 500 visible messages, 6 context entries capped at 1000 characters each; prompt max 4000, assistant output max 4096. Defaults: 5 requests per rolling minute, 30 requests and 30,000 reserved units / workspace / UTC day. Quota values are server configuration, not browser controlled.
8. AI prompts are private workspace content and not logged in the audit trail. Audit stores action/actor/workspace/time only; trace ID remains available in request output and internal ledger. Provider instructions explicitly say no tools, no unverified claims, untrusted workspace context.
9. The UI shows AI Chat, conversations, stored messages, request allowance, provider identifier and an unmistakable disabled/demo distinction. Viewer roles cannot create/send. Workspace switch remounts and clears old tenant chat state; BFF allowlists only AI routes and methods.

## HTTP API (requires enabled feature and JWT)

- GET/POST `/api/workspaces/{id}/ai/conversations`
- GET `/api/workspaces/{id}/ai/conversations/{conversationId}/messages`
- POST `/api/workspaces/{id}/ai/conversations/{conversationId}/send` with `{requestId, message}`
- GET `/api/workspaces/{id}/ai/usage`

Response: `{conversationId, requestId, traceId, answer, usedTokens, provider}`. Server returns 400 invalid input, 401 session missing, 403 tenant/role deny, 404 unknown chat, 409 reused/active/failed request, 429 quota, 503 model failure. Server intentionally does not expose provider exceptions.

## Configuration (all secrets from external environment, never commit)

API .NET:
- `Ai__Enabled=true`
- `Ai__RuntimeUrl=http://127.0.0.1:8000` (Development loopback only)
- `Ai__SharedSecret=<random 32+ byte private value>`
- `Ai__PerMinuteRequestLimit=5`, `Ai__DailyRequestLimit=30`, `Ai__DailyTokenLimit=30000`
- Requires previously configured Auth and PostgreSQL connection.

FastAPI:
- `ICEHOTT_AI_ENABLED=true`
- `ICEHOTT_AI_SHARED_SECRET=<the same value as Ai__SharedSecret>`
- `ICEHOTT_AI_PROVIDER=demo`
- `ICEHOTT_AI_ALLOW_PAID=false` (must remain OFF until explicitly approved).

Run local FastAPI with a bound loopback interface: `python -m uvicorn app.main:app --host 127.0.0.1 --port 8000`. Then start API and Next according to README Phase 1 and 2; the AI endpoints are deliberately disabled without the flags.

## Test and acceptance

- FastAPI tests include invalid/forged/expired HMAC, invalid payload, demo disclosure, paid adapter opt-in guards and mocked provider contract ensuring the fixed HTTPS endpoint, no tools and no `store` enabled.
- .NET PostgreSQL tests include JWT + tenant enforcement, viewer permissions, conversation creation, message storage, request replay without re-calling provider, daily quota, provider error cleanup, malformed payload and trace/usage contract; separate HTTP client test validates exact HMAC and token-count wire format.
- Frontend Vitest validates read-only Viewer UI, generated request ID on send, provider/demo label and honest runtime-disabled state; lint/Next build required.
- Real disposable local smoke `scripts/phase3-local-smoke.ps1` runs Phase 1/2 complete flow first and then signed FastAPI demo, HTTP routing, message persistence, idempotency, usage, tenant denial and no external provider calls.
- GitHub Actions API/PostgreSQL + AI + web must all be green before merge.

## Release limitations

This is **Phase 3 implementation**, not production approval or billing. No external paid model run occurred in acceptance testing. Signed-request replay defense is presently short timestamp freshness plus network isolation; multi-replica nonce storage/rotation, provider-cost admission, independent security review, prompt retention policy, high-scale telemetry and detailed evaluations must be verified before any production paid provider activation. Provider usage is informational; quota uses conservative reserved units, not chargeable invoice-grade metering. Tools/RAG remain Phase 4–5.
