# Phase 1A — Identity persistence and tenant authorization

Status: implementation slice of Phase 1. This does not complete public authentication.

## Security contract
- New PostgreSQL tables: identity_users, workspaces, workspace_memberships.
- Users have unique normalized lowercase email and a password_hash schema field. This slice does not accept passwords from HTTP, hash them or expose registration/login.
- Workspaces have globally unique lowercase ASCII slugs, bounded display names and server-generated UUIDs.
- Membership composite primary key (workspace_id, user_id); restrictive FKs to real users and workspaces. DB check restricts roles to Viewer=1, Member=2, Admin=3, Owner=4.
- Workspace creation transaction atomically creates workspace and Owner membership, or rejects unique slug conflicts and rolls back. Concurrency tests protect against two owners for the same slug.
- Internal WorkspaceAccessService scopes reads by user and workspace ID, denies empty IDs, unknown membership, and insufficient role.
- Workspace slugs and client-supplied user IDs are NEVER authorization credentials. A future authenticated endpoint must obtain user identity from validated session/JWT only.
- No public user or workspace endpoint, membership writer, billing or AI provider activation is added.

## Database migrations
- EF Core 9 generated InitialIdentityAndWorkspaces migration. Adds only tables/indexes and constraints.
- There is no automatic migration at API startup. Migration authoring requires explicit ICEHOTT_MIGRATION_CONNECTION (no checked-in secrets).
- Real-PostgreSQL tests create and migrate a new disposable database; do not reuse or alter the historical project DB.
- A CI PostgreSQL service executes the same integration tests. A missing connection is a test failure, not a skipped test.

## Local test procedure
Start a disposable pgvector/pgvector:pg16 PostgreSQL instance with localhost port 55444 and an isolated throwaway password; set ICEHOTT_POSTGRES_TEST_CONNECTION to its connection string. Run dotnet tool restore and dotnet test ICEHOTT.AIOS.sln --configuration Release. Stop the disposable container afterwards.

## Not yet delivered (must complete Phase 1)
1. Secure registration/invite workflow, password hashing, anti-abuse rate limits, email verification, recovery.
2. JWT/session issuance, refresh rotation, replay/revocation protection and logout.
3. Authenticated HTTP routes with validated actor claims, full cross-tenant authorization tests.
4. Role changes/invites, last-owner protection, audit logs, transactional PostgreSQL tests.

Public registration, paid providers, billing and production deployment remain OFF.
