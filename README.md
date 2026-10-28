# ICEHOTT AI OS V2
**A fresh professional rebuild of the ICEHOTT AI OS platform, starting at Phase 0.**

This is a clean project created on 2026-10-28 in `C:\Users\Flori\ICEHOTT-AI-OS-V2`.
The previous `ICEHOTT-Phase8` workspace and GitHub repo are preserved as a historical reference; no data was migrated or deleted.

## Structure
- `apps/web` — Next.js 16, TypeScript, Tailwind; browser frontend.
- `services/api/src/ICEHOTT.Api` — ASP.NET Core HTTP API, server-side authority.
- `services/api/src/ICEHOTT.Application` — use cases and interfaces.
- `services/api/src/ICEHOTT.Domain` — core value objects, independent of framework.
- `services/api/src/ICEHOTT.Infrastructure` — persistence and adapters (introduced incrementally).
- `services/api/tests` — xUnit unit/integration tests.
- `services/ai` — FastAPI AI runtime with pytest.
- `infra/compose.dev.yml` — local PostgreSQL/pgvector and Redis only.
- `docs/MASTER_PLAN.md` — phases, deliverables and security acceptance criteria.

## Local development (Windows PowerShell)
```powershell
Set-Location C:\Users\Flori\ICEHOTT-AI-OS-V2

# .NET API
dotnet restore ICEHOTT.AIOS.sln
dotnet test ICEHOTT.AIOS.sln --configuration Release
dotnet run --project services/api/src/ICEHOTT.Api

# Frontend in another terminal
Set-Location apps/web
npm ci
npm run dev

# AI in another terminal, use isolated environment
Set-Location services/ai
python -m venv .venv
.venv\Scripts\python -m pip install -r requirements.txt
.venv\Scripts\python -m pytest -q
.venv\Scripts\python -m uvicorn app.main:app --host 127.0.0.1 --port 8000

# Optional local infrastructure from root:
Copy-Item .env.example .env
# EDIT .env with a strong LOCAL-only password before starting
docker compose --env-file .env -f infra/compose.dev.yml up -d
```
No production API keys or billing required for Phase 0.

## Working agreement
Only the Phase 0 skeleton is currently being implemented. This is **not** a production-ready SaaS and does not include authorization, real AI execution, payments or database-backed tenancy. See `docs/MASTER_PLAN.md` for the full roadmap.
