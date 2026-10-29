namespace ICEHOTT.Domain;

public enum ProjectStatus : short
{
    Planned = 1,
    Active = 2,
    Blocked = 3,
    Completed = 4,
    Archived = 5
}

public sealed class WorkspaceProject
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public ProjectStatus Status { get; set; } = ProjectStatus.Planned;
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed record ProjectInput(string Name, string? Description, ProjectStatus Status);
public sealed record WorkspaceSettingsInput(string Name);
public sealed record ProjectView(
    Guid Id, Guid WorkspaceId, string Name, string Description, ProjectStatus Status,
    Guid CreatedByUserId, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record WorkspaceOverview(
    Guid Id, string Slug, string Name, WorkspaceRole Role,
    int MemberCount, int ProjectCount, int ActiveProjects, int CompletedProjects,
    int PendingInvitations, IReadOnlyList<WorkspaceActivity> RecentActivity);
public sealed record WorkspaceActivity(string Action, DateTimeOffset HappenedAtUtc);
