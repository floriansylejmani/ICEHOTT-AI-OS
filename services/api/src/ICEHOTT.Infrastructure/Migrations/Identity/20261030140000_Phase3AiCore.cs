using ICEHOTT.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ICEHOTT.Infrastructure.Migrations.Identity;

/// <summary>Raw SQL migration for the isolated AI schema. The AI persistence layer uses
/// parameterized Npgsql directly; these tables are deliberately not EF DbSets.
/// Never run automatically on startup. Existing identity/project tables are untouched.</summary>
[DbContext(typeof(IdentityDbContext))]
[Migration("20261030140000_Phase3AiCore")]
public sealed class Phase3AiCore : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE ai_conversations (
                id uuid PRIMARY KEY,
                workspace_id uuid NOT NULL REFERENCES workspaces(id) ON DELETE RESTRICT,
                created_by_user_id uuid NOT NULL REFERENCES identity_users(id) ON DELETE RESTRICT,
                title varchar(120) NOT NULL CHECK (length(trim(title)) BETWEEN 1 AND 120),
                created_at_utc timestamptz NOT NULL,
                updated_at_utc timestamptz NOT NULL,
                UNIQUE (workspace_id, id)
            );
            CREATE INDEX ix_ai_conversations_workspace_updated
                ON ai_conversations(workspace_id, updated_at_utc DESC);

            CREATE TABLE ai_messages (
                id uuid PRIMARY KEY,
                workspace_id uuid NOT NULL,
                conversation_id uuid NOT NULL,
                role varchar(12) NOT NULL CHECK (role IN ('user', 'assistant')),
                content text NOT NULL CHECK (length(content) BETWEEN 1 AND 4096),
                provider varchar(32) NOT NULL,
                used_tokens integer NOT NULL DEFAULT 0 CHECK (used_tokens BETWEEN 0 AND 10000),
                created_at_utc timestamptz NOT NULL,
                FOREIGN KEY (workspace_id, conversation_id)
                    REFERENCES ai_conversations(workspace_id, id) ON DELETE RESTRICT
            );
            CREATE INDEX ix_ai_messages_conversation_date
                ON ai_messages(workspace_id, conversation_id, created_at_utc, id);

            CREATE TABLE ai_usage_daily (
                workspace_id uuid NOT NULL REFERENCES workspaces(id) ON DELETE RESTRICT,
                usage_date date NOT NULL,
                request_count integer NOT NULL DEFAULT 0 CHECK (request_count >= 0),
                used_tokens bigint NOT NULL DEFAULT 0 CHECK (used_tokens >= 0),
                PRIMARY KEY (workspace_id, usage_date)
            );

            CREATE TABLE ai_generations (
                id uuid PRIMARY KEY,
                workspace_id uuid NOT NULL,
                conversation_id uuid NOT NULL,
                user_id uuid NOT NULL REFERENCES identity_users(id) ON DELETE RESTRICT,
                client_request_id uuid NOT NULL,
                status varchar(16) NOT NULL CHECK (status IN ('pending','completed','failed')),
                used_tokens integer NOT NULL DEFAULT 0 CHECK (used_tokens BETWEEN 0 AND 10000),
                trace_id uuid NOT NULL,
                created_at_utc timestamptz NOT NULL,
                UNIQUE (workspace_id, client_request_id),
                FOREIGN KEY (workspace_id, conversation_id)
                    REFERENCES ai_conversations(workspace_id, id) ON DELETE RESTRICT
            );
            CREATE INDEX ix_ai_generations_workspace_created
                ON ai_generations(workspace_id, created_at_utc DESC);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE ai_generations;
            DROP TABLE ai_usage_daily;
            DROP TABLE ai_messages;
            DROP TABLE ai_conversations;
            """);
    }
}
