using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ICEHOTT.Infrastructure.Migrations.Identity
{
    /// <inheritdoc />
    public partial class Phase2WorkspaceProjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "workspace_projects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspace_projects", x => x.id);
                    table.CheckConstraint("ck_projects_description", "length(description) <= 2000");
                    table.CheckConstraint("ck_projects_name", "length(trim(name)) BETWEEN 1 AND 120");
                    table.CheckConstraint("ck_projects_status", "status IN (1, 2, 3, 4, 5)");
                    table.ForeignKey(
                        name: "FK_workspace_projects_identity_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "identity_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workspace_projects_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_projects_workspace_status",
                table: "workspace_projects",
                columns: new[] { "workspace_id", "status", "updated_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_workspace_projects_created_by_user_id",
                table: "workspace_projects",
                column: "created_by_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "workspace_projects");
        }
    }
}
