using ICEHOTT.Domain;
using Microsoft.EntityFrameworkCore;

namespace ICEHOTT.Infrastructure.Identity;

/// <summary>
/// No implicit tenant filter: all reads in the application MUST use an
/// actor-scoped access method and verify membership server-side.
/// Do not expose DbSet queries as public API endpoints.
/// </summary>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<UserAccount> Users => Set<UserAccount>();
    public DbSet<WorkspaceRecord> Workspaces => Set<WorkspaceRecord>();
    public DbSet<WorkspaceMembership> Memberships => Set<WorkspaceMembership>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserAccount>(e =>
        {
            e.ToTable("identity_users", t =>
                t.HasCheckConstraint("ck_identity_users_email_lower",
                    "email_normalized = lower(email_normalized)"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.EmailNormalized).HasColumnName("email_normalized")
                .HasMaxLength(254).IsRequired();
            e.HasIndex(x => x.EmailNormalized).IsUnique()
                .HasDatabaseName("ux_identity_users_email");
            e.Property(x => x.PasswordHash).HasColumnName("password_hash")
                .HasMaxLength(1024).IsRequired();
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc")
                .HasColumnType("timestamp with time zone");
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
            e.Property(x => x.DisplayName).HasColumnName("display_name")
                .HasMaxLength(120).IsRequired();
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc")
                .HasColumnType("timestamp with time zone");
        });

        modelBuilder.Entity<WorkspaceMembership>(e =>
        {
            e.ToTable("workspace_memberships", t =>
                t.HasCheckConstraint("ck_workspace_memberships_role",
                    "role IN (1, 2, 3, 4)"));
            e.HasKey(x => new { x.WorkspaceId, x.UserId });
            e.Property(x => x.WorkspaceId).HasColumnName("workspace_id").ValueGeneratedNever();
            e.Property(x => x.UserId).HasColumnName("user_id").ValueGeneratedNever();
            e.Property(x => x.Role).HasColumnName("role").HasConversion<short>().IsRequired();
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc")
                .HasColumnType("timestamp with time zone");
            e.HasIndex(x => x.UserId).HasDatabaseName("ix_memberships_user_id");
            e.HasOne<WorkspaceRecord>().WithMany()
                .HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserAccount>().WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
