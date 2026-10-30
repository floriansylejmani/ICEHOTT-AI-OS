using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ICEHOTT.Infrastructure.Migrations.Identity
{
    /// <inheritdoc />
    public partial class Phase3AiCoreConversationsUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_conversations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_conversations", x => x.id);
                    table.UniqueConstraint("AK_ai_conversations_workspace_id_id", x => new { x.workspace_id, x.id });
                    table.CheckConstraint("ck_ai_conv_title", "length(trim(title)) BETWEEN 1 AND 100");
                    table.ForeignKey(
                        name: "FK_ai_conversations_identity_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "identity_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ai_conversations_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ai_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    input_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reserved_tokens = table.Column<int>(type: "integer", nullable: false),
                    used_tokens = table.Column<int>(type: "integer", nullable: false),
                    provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reply_message_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_attempts", x => x.id);
                    table.CheckConstraint("ck_ai_attempt_status", "status IN (1, 2, 3)");
                    table.ForeignKey(
                        name: "FK_ai_attempts_ai_conversations_workspace_id_conversation_id",
                        columns: x => new { x.workspace_id, x.conversation_id },
                        principalTable: "ai_conversations",
                        principalColumns: new[] { "workspace_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ai_attempts_identity_users_actor_id",
                        column: x => x.actor_id,
                        principalTable: "identity_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ai_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<short>(type: "smallint", nullable: false),
                    content = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_messages", x => x.id);
                    table.CheckConstraint("ck_ai_msg_role", "role IN (1, 2)");
                    table.CheckConstraint("ck_ai_msg_size", "length(content) BETWEEN 1 AND 4096");
                    table.ForeignKey(
                        name: "FK_ai_messages_ai_conversations_workspace_id_conversation_id",
                        columns: x => new { x.workspace_id, x.conversation_id },
                        principalTable: "ai_conversations",
                        principalColumns: new[] { "workspace_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ai_attempts_actor_id",
                table: "ai_attempts",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "IX_ai_attempts_workspace_id_conversation_id",
                table: "ai_attempts",
                columns: new[] { "workspace_id", "conversation_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ai_usage_daily",
                table: "ai_attempts",
                columns: new[] { "workspace_id", "started_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_ai_request_workspace",
                table: "ai_attempts",
                columns: new[] { "workspace_id", "request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ai_conv_workspace",
                table: "ai_conversations",
                columns: new[] { "workspace_id", "updated_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_ai_conversations_created_by_user_id",
                table: "ai_conversations",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_messages_conversation",
                table: "ai_messages",
                columns: new[] { "workspace_id", "conversation_id", "created_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_attempts");

            migrationBuilder.DropTable(
                name: "ai_messages");

            migrationBuilder.DropTable(
                name: "ai_conversations");
        }
    }
}
