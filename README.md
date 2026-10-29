# ICEHOTT AI OS V2

Clean standalone rebuild of ICEHOTT AI OS in GitHub repository floriansylejmani/ICEHOTT-AI-OS. The historical ICEHOTT-AI-Workspace remains untouched.

## Project status

- Phase 0: monorepo, .NET 9, Next.js 16, FastAPI, PostgreSQL/pgvector, Redis and CI.
- Phase 1: authenticated verified accounts; JWT/refresh/revocation and recovery; account lockout; server-authoritative workspace roles and email invites; invite-only registration, audit and web BFF with HttpOnly cookies.
- Future: Phase 2 UI expansion, Phase 3 AI, Phase 4 RAG, Phase 5 tools, Phase 6 workflows, Phase 7 artifacts, Phase 8 SaaS, Phase 9 optional Stripe and Phase 10–12 release readiness.

See docs/MASTER_PLAN.md and docs/PHASE1_IDENTITY_COMPLETE.md. **No live billing, hosted public registration, or production deploy.**

## Source tree

- apps/web — Next 16 / React 19 and server-side session BFF.
- services/api — ASP.NET Core 9 API / Domain / Application / Infrastructure; EF migrations + real PostgreSQL xUnit.
- services/ai — FastAPI with pytest health tests.
- infra/compose.dev.yml — local PostgreSQL, Redis and Mailpit (127.0.0.1 bound).
- scripts/phase1-local-smoke.ps1 — disposable-only HTTP + email + tenancy acceptance smoke.

## Development on Windows PowerShell

From the repo root, copy .env.example to .env, choose a strong LOCAL PostgreSQL password, and run:
  docker compose --env-file .env -f infra/compose.dev.yml up -d
  dotnet tool restore
  dotnet restore ICEHOTT.AIOS.sln

In your API terminal, export environment variables (DO NOT store secrets in tracked files):
  $env:ASPNETCORE_ENVIRONMENT = 'Development'
  $env:ConnectionStrings__Identity = 'Host=127.0.0.1;Port=5434;Database=icehott_v2_dev;Username=icehott_dev;Password=REPLACE_WITH_LOCAL_POSTGRES_PASSWORD'
  $env:Auth__Enabled = 'true'
  $env:Auth__RegistrationEnabled = 'false'
  $bytes = New-Object byte[] 48
  $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
  $rng.GetBytes($bytes)
  $rng.Dispose()
  $env:Auth__SigningKey = [Convert]::ToBase64String($bytes)
  $env:Auth__Issuer = 'ICEHOTT-Local-Dev'
  $env:Auth__Audience = 'ICEHOTT-Local-Web'
  $env:Auth__PublicBaseUrl = 'http://localhost:3000'
  $env:Email__Smtp__Host = '127.0.0.1'
  $env:Email__Smtp__Port = '1025'
  $env:Email__Smtp__From = 'noreply@icehott.local'
  $env:Email__Smtp__AllowPlaintextLocal = 'true'
  $env:ICEHOTT_MIGRATION_CONNECTION = $env:ConnectionStrings__Identity

Apply migrations ONLY to your intended local development DB:
  dotnet tool run dotnet-ef database update --project services/api/src/ICEHOTT.Infrastructure --startup-project services/api/src/ICEHOTT.Api --context IdentityDbContext --configuration Release
Start API:
  dotnet run --project services/api/src/ICEHOTT.Api --urls http://127.0.0.1:5035

In another terminal:
  cd apps/web
  $env:ICEHOTT_API_BASE_URL = 'http://127.0.0.1:5035'
  npm ci
  npm run dev -- -p 3000

Open http://localhost:3000/login and local mail at http://127.0.0.1:8025. Public registration is OFF by default; invite-only registration works when Auth is enabled. For local public signup tests only, set Auth__RegistrationEnabled=true before API startup.

## Tests

Backend tests require ICEHOTT_POSTGRES_TEST_CONNECTION pointing to a disposable PostgreSQL server where a test user can CREATE DATABASE. Each test fixture migrates and drops only its uniquely named test database:
  dotnet test ICEHOTT.AIOS.sln --configuration Release

Frontend:
  cd apps/web
  npm run test
  npm run lint
  npm run build
  npm audit --omit=dev --audit-level=low

AI (inside services/ai venv): python -m pytest -q

After local services are running against disposable data, run:
  powershell -NoProfile -File scripts/phase1-local-smoke.ps1

Do not run the smoke script on production data. The web interface is an initial Phase 1 shell, not the complete Phase 2 workspace UX. Production email, credentials and deployment remain subject to separate staging verification.
