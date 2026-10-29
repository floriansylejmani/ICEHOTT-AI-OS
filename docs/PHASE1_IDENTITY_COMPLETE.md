# ICEHOTT AI OS V2 — Phase 1: Identity & Tenancy

**Status:** Full Phase 1 feature implementation, tested with PostgreSQL and local Mailpit SMTP. Production activation and external SMTP staging sign-off remain separate release gates.

## Features

- PostgreSQL identity users (unique normalized email, PBKDF2 ASP.NET PasswordHasher with 210,000 iterations, account disabled and verified states, failed-login counters with 15-minute lockout after five failures).
- Email verification and reset tokens: 48 cryptographically random bytes, SHA-256 hashes at rest, one-time, expiry, verified owner bound. Password reset revokes all active sessions.
- JWT access tokens (10 minutes), server-validated issuer/audience/signature/expiry; token family checked against database on every authenticated request. Refresh tokens rotate in a PostgreSQL transaction, replay revokes the whole family, logout immediately revokes active sessions.
- Workspace roles Viewer/Member/Admin/Owner, actor-scoped list, atomic create plus Owner membership, private invitations, accept/revoke, member list, change/remove, protected last-owner and ownership transfer. Workspace locks serialize privileged writes.
- Invitation-only account creation works without public registration. Mailbox receipt of a signed-looking random invitation token (verified through SHA-256 server lookup) proves control of the invited email, and creates user/verified state/membership atomically. It is not public registration.
- Security audit table for account/session/workspace actions: actor ID, workspace ID, action, time; no passwords, email bodies, private raw tokens or secret values.
- Browser: Next.js pages for login, registration, verification, password recovery, invitations and workspace/team management. BFF stores access/refresh in Secure HttpOnly SameSite=Strict cookies in production, not localStorage, checks origin/fetch metadata for mutations, and never lets the browser choose arbitrary backend destinations.
- SMTP with STARTTLS and credentials for hosted use; explicit plaintext exception only for localhost/127.0.0.1 with Development environment and Email:Smtp:AllowPlaintextLocal=true.
- Auth disabled and public registration disabled by default. No Stripe, AI billing or production deployment.

## HTTP endpoints

Auth:
- POST /api/auth/register — only when Auth:RegistrationEnabled=true
- POST /api/auth/register-with-invitation — valid unconsumed invited email token; always independently rate-limited
- POST /api/auth/verify-email, /api/auth/resend-verification
- POST /api/auth/login, /api/auth/refresh, /api/auth/logout
- POST /api/auth/request-password-reset, /api/auth/reset-password

Authenticated workspaces:
- GET/POST /api/workspaces/
- GET /api/workspaces/{id}/members
- GET/POST /api/workspaces/{id}/invitations
- DELETE /api/workspaces/{id}/invitations/{invitationId}
- POST /api/invitations/accept
- PUT /api/workspaces/{id}/members/{userId}/role
- DELETE /api/workspaces/{id}/members/{userId}
- POST /api/workspaces/{id}/transfer-ownership/{userId}

There are no authorization decisions based only on a client-provided user ID, workspace slug or JWT role claim; the authenticated actor and current DB membership control access.

## Required configuration and operating policy

Set Auth:Enabled=true explicitly and configure:
- ConnectionStrings:Identity — PostgreSQL connection, not checked into git
- Auth:SigningKey — cryptographically random secret of at least 32 bytes, generated outside the repository
- Auth:Issuer and Auth:Audience — validated for JWT
- Auth:PublicBaseUrl — expected frontend origin, HTTPS required outside Development
- Auth:RegistrationEnabled — false default (invite-only)
- Auth:RateLimitPerMinute — 10 default, 1–120 validated range
- Email:Smtp:Host, Email:Smtp:Port (587 default), Email:Smtp:Username, Email:Smtp:Password, Email:Smtp:From — server-only credentials

In non-Development, enabled auth fails startup if SMTP settings or HTTPS public URL are missing. No database migration executes automatically. Apply reviewed additive EF Core migrations via ICEHOTT_MIGRATION_CONNECTION to a chosen dev/staging instance.

For local offline email testing, use Mailpit on loopback ports 1025 and 8025 with Email:Smtp:Host=127.0.0.1, Email:Smtp:Port=1025, Email:Smtp:AllowPlaintextLocal=true and ASPNETCORE_ENVIRONMENT=Development. That exception cannot be used against non-loopback SMTP hosts or in production.

The web BFF requires ICEHOTT_API_BASE_URL as a server-side environment setting. It rejects HTTP backends in production and allows HTTP loopback only for local development.

## Verification and release gates

- Backend 32 PostgreSQL-backed xUnit tests on a disposable test database, including JWT enforcement, forged token refusal, refresh replay/concurrency, email reset, lockout, cross-tenant rejection, role and invitation rules.
- Frontend 8 Vitest tests, ESLint, Next production build and npm production dependency audit.
- FastAPI remains 2 health/surface tests (future phases).
- Local real HTTP smoke (scripts/phase1-local-smoke.ps1) verified web BFF → .NET API → PostgreSQL → Mailpit → web sign-in, secure workspace create, invite, accept and logout without external email or customer data.
- GitHub Actions API job provisions PostgreSQL; frontend and AI jobs run independently.

**Deployment not authorized:** Real mail delivery/DNS/TLS, production reverse-proxy headers, browser acceptance against staging, penetration review, retention policy, secrets management and release approval must be checked in the later staging/operations phases. Phase 1 code completion does not mean a production SaaS is live.
