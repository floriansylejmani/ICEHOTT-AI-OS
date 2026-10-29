# ICEHOTT AI OS V2 — Master Development Plan
**Status:** Greenfield restart, 28 October 2026. Existing ICEHOTT-AI-Workspace is archived in place and not modified.
**Product goal:** A multi-tenant AI workspace SaaS for individuals and teams, with projects, AI agents, knowledge/RAG, controlled tools, workflows, artifacts and optional paid tiers.
**Rule:** Build product foundations first. Do not enable billing, tool execution, paid provider calls, production deployments or public registration until each relevant safety gate passes.

## Technology and architecture
- **Web:** Next.js 16 / React / TypeScript, accessible design system, typed API client.
- **API (authority):** ASP.NET Core 9, modular Clean Architecture: Domain → Application → Infrastructure → API. Authoritative auth, tenant membership, billing, plan limits, audit and tool policies live here.
- **AI:** Python FastAPI worker/gateway; only receives authenticated API-to-AI requests; AI never independently decides workspace entitlements or secrets.
- **Data:** PostgreSQL + pgvector, EF Core migrations; Redis for cache/queues; object store (S3-compatible) only when artifacts ship.
- **Contracts:** OpenAPI, error envelopes and tracing IDs; no direct browser access to private AI or database services.
- **Operations:** Docker Compose local, CI checks for each language, staging-only previews, telemetry, backups and restore drills.

## Execution model
Each Phase has a concrete acceptance gate. Keep scope bounded, create test evidence, add ADRs for irreversible decisions and never claim a phase complete without passing its gate. Default to feature branches and PRs in a **new V2 GitHub repository**, to be created separately; do not use or rewrite the old repository. Keep the historical code for reference, not for copy/paste.

| Phase | Deliverable | Gate for completion |
|---|---|---|
| **0 — Foundation** | Fresh monorepo; .NET layers; Next frontend; FastAPI; local PG+Redis; git hygiene; initial CI; docs | API/AI/web build/tests pass; compose validates; no secrets committed |
| **1 — Identity and tenancy** | Users, secure login, refresh sessions, workspaces, membership roles and invites | Auth security tests; PostgreSQL tenant isolation; denial by default |
| **2 — Workspace UX** | Dashboard, teams, settings, projects, navigation, responsive design | Role-based E2E flows, accessible UI, typed API |
| **3 — AI Core** | Provider abstraction, safe chat, request tracing, rate/usage limits, evaluations | Provider contract tests, explicit opt-in for external paid requests |
| **4 — Knowledge/RAG** | Upload, parse, chunk, embed, pgvector search, source citations | Workspace isolation, injection defenses, grounded evaluations |
| **5 — Tools & policy** | Tool registry, per-tool permission, approval, sandboxing, quotas and audit | Deny-by-default, timeout/replay/permission tests |
| **6 — Workflows** | Visual authoring, durable engine, retries, checkpoints, schedules | Exactly-once effects where possible, idempotent task tests, resume after crash |
| **7 — Artifacts and integrations** | Versioned artifacts, object storage, export, vetted connectors | Virus scanning/size limits, tenant-scoped access and recovery |
| **8 — SaaS foundation** | Plans, entitlements, period usage ledger, subscriptions, transactional admission | DB concurrency/invoice integrity, no unpaid plan grants, accurate usage |
| **9 — Billing (optional launch)** | Stripe TEST-mode checkout, portal, signed webhooks, provider reconcile, failed payments, cancel & renew | Full sandbox E2E, verified owner identity and provider state, rollback and refunds plan; live stays OFF |
| **10 — Admin and observability** | Security events, support tooling, monitoring, backups, SLOs | Redaction, alerts and tested recovery drill |
| **11 — Security & scale** | Threat model, penetration tests, load tests, data retention, DR | All critical findings fixed; cross-tenant tests green |
| **12 — Staging to production** | End-to-end acceptance, accessibility, staging canary, runbooks and staged release | User acceptance + explicit owner approval; no implicit prod release |

## Phase 0 — completion checklist
- [x] New isolated directory and git initialized; original repository preserved.
- [x] .NET 9 solution with Domain, Application, Infrastructure, API and xUnit tests.
- [x] FastAPI entrypoint and pytest health/security placeholder checks.
- [x] PostgreSQL+pgvector / Redis Compose development definition.
- [x] GitHub Actions blueprint (API, AI, frontend), safe .gitignore and config template.
- [x] Next.js installed with a custom Phase 0 product shell; frontend Vitest tests added. Build/lint verified locally.
- [x] Fresh .NET restore/build and xUnit green (13 tests after template cleanup).
- [x] FastAPI dependencies installed in isolated venv; pytest green (2 tests, one dependency deprecation warning).
- [x] Docker Compose development config validation green.
- [x] New V2 GitHub remote established and CI green (ICEHOTT-AI-OS, PR #1 merged; main run 40785933505).
The checkboxes above were verified in local builds and GitHub Actions. **Phase 0 is complete as a development foundation only**; the product is not deployed or production-ready.

## Phase 1 implementation progress
- [x] Phase 1A: PostgreSQL identity/users, workspace and membership schema; role-based actor-scoped internal access; atomic workspace ownership; EF migration and real-PostgreSQL integration tests (see docs/PHASE1_IDENTITY_TENANCY_FOUNDATION.md).
- [x] Phase 1B: verified email, PBKDF2 password hashing, JWT, refresh rotation/replay detection, logout, reset and persistent account lockout, rate limiting. See docs/PHASE1_IDENTITY_COMPLETE.md.
- [x] Phase 1C: authenticated workspace API and browser BFF, invite-only onboarding, membership role change/removal, atomic owner transfer, audit and cross-tenant negative PostgreSQL tests.
- [x] Phase 1 feature acceptance in isolated tests and local HTTP/Mailpit smoke: authenticated web app, real PostgreSQL migration and tenant isolation. Production staging/mail delivery/penetration review remain separate Phase 10–12 release gates. No public registration or production billing activated.

## Phase 2 implementation progress
- [x] Responsive dashboard, workspace selector and role-aware Overview / Projects / Settings / Team navigation.
- [x] PostgreSQL workspace_projects table, EF migration, scoped list/create/edit/archive with bounded payloads and Admin-only archival.
- [x] Workspace settings name update with Admin+ access, authoritative activity/counts, safe TypeScript BFF client and no browser token storage.
- [x] Role-based authenticated HTTP integration tests, frontend accessibility/role tests, and real local HTTP/SMTP smoke test (see docs/PHASE2_WORKSPACE_UX.md).
- [x] Phase 2 code acceptance; staged assistive-technology testing and live-service readiness are later release gates.

## Rules for every phase
1. Protect data/credentials. No .env, tokens, live Stripe or provider keys in git.
2. Every tenant-owned table requires WorkspaceId, database integrity and server-side member authorization.
3. Migrations are additive and reviewed; no production database edits from local tasks.
4. The API is authority for plans, tokens, prompt budgets, payments and tool admission; browser and webhook event fields are untrusted.
5. Avoid ambient agent executions and never promise autonomous long-running completion without an explicit scheduled agent.
6. PR acceptance: clean diff, unit+integration+security tests, frontend lint/build, dependency audit, docs; require CI green before merge.
7. No deploy, secrets provisioning or live charges until the project owner explicitly approves the relevant release.
