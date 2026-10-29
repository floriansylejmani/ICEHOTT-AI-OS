using ICEHOTT.Domain;
using Microsoft.EntityFrameworkCore;

namespace ICEHOTT.Infrastructure.Identity;

/// <summary>
/// Internal persistence. NEVER expose a raw DbSet to untrusted HTTP handlers.
/// Every tenant read/write must pass through verified actor authorization.
/// </summary>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<UserAccount> Users => Set<UserAccount>();
    public DbSet<WorkspaceRecord> Workspaces => Set<WorkspaceRecord>();
    public DbSet<WorkspaceMembership> Memberships => Set<WorkspaceMembership>();
    public DbSet<RefreshSession> RefreshSessions => Set<RefreshSession>();
    public DbSet<EmailActionToken> EmailTokens => Set<EmailActionToken>();
    public DbSet<WorkspaceInvitation> Invitations => Set<WorkspaceInvitation>();
    public DbSet<IdentityAuditEvent> AuditEvents => Set<IdentityAuditEvent>();
    public DbSet<WorkspaceProject> Projects => Set<WorkspaceProject>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserAccount>(e =>
        {
            e.ToTable("identity_users", t =>
                t.HasCheckConstraint("ck_identity_users_email_lower",
                    "email_normalized = lower(email_normalized)"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.EmailNormalized).HasColumnName("email_normalized").HasMaxLength(254).IsRequired();
            e.HasIndex(x => x.EmailNormalized).IsUnique().HasDatabaseName("ux_identity_users_email");
            e.Property(x => x.PasswordHash).HasColumnName("password_hash").HasMaxLength(1024).IsRequired();
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamp with time zone");
            e.Property(x => x.EmailVerifiedAtUtc).HasColumnName("email_verified_at_utc").HasColumnType("timestamp with time zone");
            e.Property(x => x.DisabledAtUtc).HasColumnName("disabled_at_utc").HasColumnType("timestamp with time zone");
            e.Property(x => x.FailedLoginCount).HasColumnName("failed_login_count").HasDefaultValue(0);
            e.Property(x => x.LockoutUntilUtc).HasColumnName("lockout_until_utc").HasColumnType("timestamp with time zone");
        });
        modelBuilder.Entity<WorkspaceRecord>(e =>
        {
            e.ToTable("workspaces", t =>
                t.HasCheckConstraint("ck_workspaces_slug",
                    "length(slug) BETWEEN 3 AND 48 AND slug ~ '^[a-z0-9]([a-z0-9-]*[a-z0-9])?$'"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.Slug).HasColumnName("slug").HasMaxLength(48).IsRequired();
            e.HasIndex(x => x.Slug).IsUnique().HasDatabaseName("ux_workspaces_slug");
            e.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(120).IsRequired();
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamp with time zone");
        });
        modelBuilder.Entity<WorkspaceMembership>(e =>
        {
            e.ToTable("workspace_memberships", t =>
                t.HasCheckConstraint("ck_workspace_memberships_role", "role IN (1, 2, 3, 4)"));
            e.HasKey(x => new { x.WorkspaceId, x.UserId });
            e.Property(x => x.WorkspaceId).HasColumnName("workspace_id").ValueGeneratedNever();
            e.Property(x => x.UserId).HasColumnName("user_id").ValueGeneratedNever();
            e.Property(x => x.Role).HasColumnName("role").HasConversion<short>().IsRequired();
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamp with time zone");
            e.HasIndex(x => x.UserId).HasDatabaseName("ix_memberships_user_id");
            e.HasOne<WorkspaceRecord>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<WorkspaceProject>(e =>
        {
            e.ToTable("workspace_projects", table =>
            {
                table.HasCheckConstraint("ck_projects_status", "status IN (1, 2, 3, 4, 5)");
                table.HasCheckConstraint("ck_projects_name", "length(trim(name)) BETWEEN 1 AND 120");
                table.HasCheckConstraint("ck_projects_description", "length(description) <= 2000");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.WorkspaceId).HasColumnName("workspace_id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
            e.Property(x => x.Description).HasColumnName("description").HasMaxLength(2000).IsRequired();
            e.Property(x => x.Status).HasColumnName("status").HasConversion<short>().IsRequired();
            e.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            e.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
            e.HasIndex(x => new { x.WorkspaceId, x.Status, x.UpdatedAtUtc }).HasDatabaseName("ix_projects_workspace_status");
            e.HasOne<WorkspaceRecord>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<IdentityAuditEvent>(e =>
        {
            e.ToTable("identity_audit_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.ActorUserId).HasColumnName("actor_user_id");
            e.Property(x => x.WorkspaceId).HasColumnName("workspace_id");
            e.Property(x => x.Action).HasColumnName("action").HasMaxLength(64).IsRequired();
            e.Property(x => x.HappenedAtUtc).HasColumnName("happened_at_utc");
            e.HasIndex(x => new { x.WorkspaceId, x.HappenedAtUtc });
            e.HasIndex(x => new { x.ActorUserId, x.HappenedAtUtc });
        });
        modelBuilder.Entity<RefreshSession>(e =>
        {
            e.ToTable("auth_refresh_sessions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.FamilyId).HasColumnName("family_id");
            e.Property(x => x.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("ux_refresh_token_hash");
            e.HasIndex(x => new { x.FamilyId, x.UserId }).HasDatabaseName("ix_refresh_family");
            e.Property(x => x.ExpiresAtUtc).HasColumnName("expires_at_utc");
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            e.Property(x => x.RevokedAtUtc).HasColumnName("revoked_at_utc");
            e.Property(x => x.ReplacedAtUtc).HasColumnName("replaced_at_utc");
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<EmailActionToken>(e =>
        {
            e.ToTable("auth_email_action_tokens", t => t.HasCheckConstraint(
                "ck_auth_email_token_purpose", "purpose IN ('verify', 'reset')"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.Property(x => x.Purpose).HasColumnName("purpose").HasMaxLength(12).IsRequired();
            e.Property(x => x.ExpiresAtUtc).HasColumnName("expires_at_utc");
            e.Property(x => x.UsedAtUtc).HasColumnName("used_at_utc");
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<WorkspaceInvitation>(e =>
        {
            e.ToTable("workspace_invitations", t => t.HasCheckConstraint(
                "ck_workspace_invitation_role", "role IN (1, 2, 3)"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.WorkspaceId).HasColumnName("workspace_id");
            e.Property(x => x.InvitedByUserId).HasColumnName("invited_by_user_id");
            e.Property(x => x.EmailNormalized).HasColumnName("email_normalized").HasMaxLength(254).IsRequired();
            e.Property(x => x.Role).HasColumnName("role").HasConversion<short>();
            e.Property(x => x.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => new { x.WorkspaceId, x.EmailNormalized });
            e.Property(x => x.ExpiresAtUtc).HasColumnName("expires_at_utc");
            e.Property(x => x.AcceptedAtUtc).HasColumnName("accepted_at_utc");
            e.Property(x => x.RevokedAtUtc).HasColumnName("revoked_at_utc");
            e.HasOne<WorkspaceRecord>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.InvitedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
