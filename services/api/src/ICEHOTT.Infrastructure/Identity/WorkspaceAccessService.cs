using ICEHOTT.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ICEHOTT.Infrastructure.Identity;

/// <summary>
/// Internal-only access path until a JWT-authenticated API is introduced.
/// Caller identity must always come from validated server auth claims, never
/// an arbitrary client-submitted userId.
/// </summary>
public sealed class WorkspaceAccessService(IdentityDbContext db)
{
    public async Task<WorkspaceSummary> CreateAsync(
        Guid authenticatedUserId, string? slug, string? displayName,
        CancellationToken cancellationToken = default)
    {
        if (authenticatedUserId == Guid.Empty)
            throw new UnauthorizedAccessException("Authenticated user required.");
        var canonicalSlug = WorkspaceSlug.Create(slug).Value;
        var canonicalName = displayName?.Trim();
        if (string.IsNullOrWhiteSpace(canonicalName) || canonicalName.Length > 120)
            throw new ArgumentException("Workspace name must be 1–120 characters.", nameof(displayName));
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await db.Users.AnyAsync(u => u.Id == authenticatedUserId, cancellationToken))
            throw new UnauthorizedAccessException("Authenticated user not found.");
        var workspace = new WorkspaceRecord
        {
            Id = Guid.NewGuid(),
            Slug = canonicalSlug,
            DisplayName = canonicalName,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        db.Workspaces.Add(workspace);
        db.Memberships.Add(new WorkspaceMembership
        {
            WorkspaceId = workspace.Id,
            UserId = authenticatedUserId,
            Role = WorkspaceRole.Owner,
            CreatedAtUtc = workspace.CreatedAtUtc
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when
            (ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation &&
             pg.ConstraintName == "ux_workspaces_slug")
        {
            throw new WorkspaceSlugAlreadyTakenException(canonicalSlug, ex);
        }
        return new WorkspaceSummary(workspace.Id, workspace.Slug, workspace.DisplayName, WorkspaceRole.Owner);
    }

    public Task<bool> HasRoleAsync(
        Guid authenticatedUserId, Guid workspaceId, WorkspaceRole minimumRole,
        CancellationToken cancellationToken = default)
    {
        if (authenticatedUserId == Guid.Empty || workspaceId == Guid.Empty ||
            !Enum.IsDefined(minimumRole))
            return Task.FromResult(false);

        return db.Memberships.AsNoTracking()
            .AnyAsync(m => m.UserId == authenticatedUserId &&
                           m.WorkspaceId == workspaceId &&
                           m.Role >= minimumRole,
                      cancellationToken);
    }

    public Task<List<WorkspaceSummary>> ListMineAsync(
        Guid authenticatedUserId, CancellationToken cancellationToken = default)
    {
        if (authenticatedUserId == Guid.Empty)
            return Task.FromResult(new List<WorkspaceSummary>());

        return (from member in db.Memberships.AsNoTracking()
                join workspace in db.Workspaces.AsNoTracking()
                    on member.WorkspaceId equals workspace.Id
                where member.UserId == authenticatedUserId
                orderby workspace.Slug
                select new WorkspaceSummary(workspace.Id, workspace.Slug,
                    workspace.DisplayName, member.Role)).ToListAsync(cancellationToken);
    }
}

public sealed record WorkspaceSummary(Guid Id, string Slug, string DisplayName, WorkspaceRole Role);

public sealed class WorkspaceSlugAlreadyTakenException(string slug, Exception inner)
    : Exception($"Workspace slug '{slug}' is unavailable.", inner);
