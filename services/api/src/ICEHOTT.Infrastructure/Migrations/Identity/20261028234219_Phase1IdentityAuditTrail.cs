using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ICEHOTT.Infrastructure.Migrations.Identity
{
    /// <inheritdoc />
    public partial class Phase1IdentityAuditTrail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "identity_audit_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    happened_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_audit_events", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_identity_audit_events_actor_user_id_happened_at_utc",
                table: "identity_audit_events",
                columns: new[] { "actor_user_id", "happened_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_identity_audit_events_workspace_id_happened_at_utc",
                table: "identity_audit_events",
                columns: new[] { "workspace_id", "happened_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "identity_audit_events");
        }
    }
}
