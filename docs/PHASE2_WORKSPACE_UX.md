# ICEHOTT AI OS V2 — Phase 2 Workspace UX and Dashboard

Scope: responsive dashboard, workspace projects, settings and team navigation, typed browser API, real PostgreSQL persistence, role-based authorization. This is NOT a production SaaS deployment.

## Delivered experience

- /dashboard provides responsive navigation, workspace selector, overview, projects and settings; /workspaces remains the team and invitation management route.
- Overview uses server-side PostgreSQL counts for members, non-archived projects, active/completed projects, recent workspace audit activity. Invitation totals require Admin+ role.
- Projects include create, edit, status transitions and soft archive. Viewer can read; Member+ can create/update; Admin+ can archive. Updated/archived projects are scoped to the selected workspace and auditable.
- Settings allow Admin+ to rename a workspace (1–120 characters); slug stays unchanged. Viewer/Member are read-only.
- Keyboard-focus styles, semantic headings, labelled fields, responsive sidebar, live status messaging and accessible empty states.
- TypeScript workspace-api.ts provides a same-origin BFF client. No JWT/refresh tokens in localStorage.

## Server authority and data

- EF Core 9 additive migration Phase2WorkspaceProjects creates workspace_projects with workspace and creator foreign keys, UUID identity, bounded title/description, valid status check and workspace/status/update index.
- States: Planned=1, Active=2, Blocked=3, Completed=4, Archived=5. Project title max 120, description max 2000; max 200 non-archived projects in each workspace.
- Workspaces are locked for mutations, and database-verified membership is required. Read: Viewer+. Create/update: Member+. Archive and settings: Admin+.
- Archived status cannot be set by a generic Member update: separate Admin+ archive route. Actor IDs come only from verified JWT/server session.
- API routes, all authenticated: GET /api/workspaces/{id}/overview, GET/POST /api/workspaces/{id}/projects, PUT/DELETE /api/workspaces/{id}/projects/{projectId}, PUT /api/workspaces/{id}/settings.
- BFF accepts only allowlisted routes/methods. Existing Phase 1 auth, refresh rotation, invites, last-owner checks and session revocation remain enforced.

## Acceptance evidence

- Disposable PostgreSQL 16 migration and .NET Release xUnit: 35/35 passed, zero skipped (cross-tenant denies, role matrix, data integrity, settings, audited project lifecycle).
- Frontend: 11 Vitest tests incl. Viewer/Owner UI and workspace switching, ESLint clean, Next production build, npm production audit 0 vulnerabilities.
- Local real end-to-end smoke (scripts/phase2-local-smoke.ps1): Phase1 signup/verification + browser BFF → API → PostgreSQL/Mailpit, project lifecycle, dashboard counts, member 403 settings/archive, owner rename/archive/audit. PASS.
- GitHub Actions API + AI + web jobs required GREEN before merge.

## Safety and follow-up

Auth and public registration remain disabled by default. No live SMTP, billing, provider calls, migrations at API startup or production deployment introduced. Staging verification with real SMTP, cross-browser audits, assistive-technology accessibility and independent security testing remain release gates (Phases 10–12). Run the smoke only on disposable local services; it creates users, workspaces and projects.

To apply migrations only to the intended dev/staging database, set ICEHOTT_MIGRATION_CONNECTION explicitly and use the dotnet-ef database update procedure in README. Never run the smoke script against customer data.
