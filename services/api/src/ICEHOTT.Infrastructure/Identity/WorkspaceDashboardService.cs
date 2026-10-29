using ICEHOTT.Domain;
using Microsoft.EntityFrameworkCore;

namespace ICEHOTT.Infrastructure.Identity;

/// <summary>
/// PostgreSQL-backed tenant-aware UX operations. Auth identity is always
/// derived from validated JWT claims, never a user ID supplied by the browser.
/// </summary>
public sealed class WorkspaceDashboardService(IdentityDbContext db)
{
    public async Task<WorkspaceOverview> OverviewAsync(Guid actor, Guid workspaceId, CancellationToken ct = default)
    {
        var role = await GetRoleAsync(actor, workspaceId, WorkspaceRole.Viewer, ct);
        var ws = await db.Workspaces.AsNoTracking().SingleAsync(x => x.Id == workspaceId, ct);
        var memberCount = await db.Memberships.CountAsync(x => x.WorkspaceId == workspaceId, ct);
        var projectCount = await db.Projects.CountAsync(x => x.WorkspaceId == workspaceId && x.Status != ProjectStatus.Archived, ct);
        var active = await db.Projects.CountAsync(x => x.WorkspaceId == workspaceId && x.Status == ProjectStatus.Active, ct);
        var completed = await db.Projects.CountAsync(x => x.WorkspaceId == workspaceId && x.Status == ProjectStatus.Completed, ct);
        var now = DateTimeOffset.UtcNow;
        var pending = role >= WorkspaceRole.Admin
            ? await db.Invitations.CountAsync(x => x.WorkspaceId == workspaceId && x.AcceptedAtUtc == null &&
                x.RevokedAtUtc == null && x.ExpiresAtUtc > now, ct) : 0;
        var recent = await db.AuditEvents.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId)
            .OrderByDescending(x => x.HappenedAtUtc).ThenByDescending(x => x.Id)
            .Take(10).Select(x => new WorkspaceActivity(x.Action, x.HappenedAtUtc)).ToListAsync(ct);
        return new WorkspaceOverview(ws.Id, ws.Slug, ws.DisplayName, role, memberCount, projectCount,
            active, completed, pending, recent);
    }

    public async Task<WorkspaceSummary> RenameAsync(
        Guid actor, Guid workspaceId, WorkspaceSettingsInput input, CancellationToken ct = default)
    {
        var name = input.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120) throw new ArgumentException("Invalid workspace name.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var workspace = await LockWorkspaceAsync(workspaceId, ct);
        var role = await GetRoleAsync(actor, workspaceId, WorkspaceRole.Admin, ct);
        workspace.DisplayName = name;
        IdentityAudit.Record(db, "workspace.settings.updated", actor, workspaceId);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new WorkspaceSummary(workspaceId, workspace.Slug, workspace.DisplayName, role);
    }

    public async Task<List<ProjectView>> ListProjectsAsync(
        Guid actor, Guid workspaceId, CancellationToken ct = default)
    {
        await GetRoleAsync(actor, workspaceId, WorkspaceRole.Viewer, ct);
        return await db.Projects.AsNoTracking().Where(x => x.WorkspaceId == workspaceId && x.Status != ProjectStatus.Archived)
            .OrderByDescending(x => x.UpdatedAtUtc).ThenBy(x => x.Id)
            .Take(200)
            .Select(x => new ProjectView(x.Id, x.WorkspaceId, x.Name, x.Description, x.Status,
                x.CreatedByUserId, x.CreatedAtUtc, x.UpdatedAtUtc))
            .ToListAsync(ct);
    }

    public async Task<ProjectView> CreateProjectAsync(
        Guid actor, Guid workspaceId, ProjectInput input, CancellationToken ct = default)
    {
        ValidateProject(input);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockWorkspaceAsync(workspaceId, ct);
        await GetRoleAsync(actor, workspaceId, WorkspaceRole.Member, ct);
        var existing = await db.Projects.CountAsync(x => x.WorkspaceId == workspaceId && x.Status != ProjectStatus.Archived, ct);
        if (existing >= 200) throw new AuthConflictException("Project limit reached.");
        var now = DateTimeOffset.UtcNow;
        var project = new WorkspaceProject
        {
            Id = Guid.NewGuid(), WorkspaceId = workspaceId,
            Name = input.Name.Trim(), Description = input.Description?.Trim() ?? "",
            Status = input.Status, CreatedByUserId = actor,
            CreatedAtUtc = now, UpdatedAtUtc = now
        };
        db.Projects.Add(project);
        IdentityAudit.Record(db, "project.created", actor, workspaceId);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Map(project);
    }

    public async Task<ProjectView> UpdateProjectAsync(
        Guid actor, Guid workspaceId, Guid projectId, ProjectInput input, CancellationToken ct = default)
    {
        ValidateProject(input);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockWorkspaceAsync(workspaceId, ct);
        await GetRoleAsync(actor, workspaceId, WorkspaceRole.Member, ct);
        var project = await db.Projects.SingleOrDefaultAsync(
            x => x.WorkspaceId == workspaceId && x.Id == projectId && x.Status != ProjectStatus.Archived, ct)
            ?? throw new KeyNotFoundException();
        project.Name = input.Name.Trim();
        project.Description = input.Description?.Trim() ?? "";
        project.Status = input.Status;
        project.UpdatedAtUtc = DateTimeOffset.UtcNow;
        IdentityAudit.Record(db, "project.updated", actor, workspaceId);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Map(project);
    }

    public async Task ArchiveProjectAsync(Guid actor, Guid workspaceId, Guid projectId, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockWorkspaceAsync(workspaceId, ct);
        await GetRoleAsync(actor, workspaceId, WorkspaceRole.Admin, ct);
        var project = await db.Projects.SingleOrDefaultAsync(
            x => x.WorkspaceId == workspaceId && x.Id == projectId && x.Status != ProjectStatus.Archived, ct)
            ?? throw new KeyNotFoundException();
        project.Status = ProjectStatus.Archived;
        project.UpdatedAtUtc = DateTimeOffset.UtcNow;
        IdentityAudit.Record(db, "project.archived", actor, workspaceId);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task<WorkspaceRecord> LockWorkspaceAsync(Guid workspaceId, CancellationToken ct) =>
        await db.Workspaces.FromSqlInterpolated(
            $"SELECT * FROM workspaces WHERE id = {workspaceId} FOR UPDATE")
            .SingleOrDefaultAsync(ct) ?? throw new KeyNotFoundException();

    private async Task<WorkspaceRole> GetRoleAsync(Guid actor, Guid workspaceId, WorkspaceRole minimum, CancellationToken ct)
    {
        if (actor == Guid.Empty || workspaceId == Guid.Empty) throw new UnauthorizedAccessException();
        var role = await db.Memberships.AsNoTracking()
            .Where(x => x.UserId == actor && x.WorkspaceId == workspaceId)
            .Select(x => (WorkspaceRole?)x.Role).SingleOrDefaultAsync(ct);
        if (role is null || role.Value < minimum) throw new UnauthorizedAccessException();
        return role.Value;
    }

    private static void ValidateProject(ProjectInput input)
    {
        if (input is null || string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 120 ||
            (input.Description?.Trim().Length ?? 0) > 2000 ||
            !Enum.IsDefined(input.Status) || input.Status == ProjectStatus.Archived)
            throw new ArgumentException("Invalid project payload.");
    }

    private static ProjectView Map(WorkspaceProject x) =>
        new(x.Id, x.WorkspaceId, x.Name, x.Description, x.Status,
            x.CreatedByUserId, x.CreatedAtUtc, x.UpdatedAtUtc);
}
