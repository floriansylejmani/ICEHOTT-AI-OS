namespace ICEHOTT.Domain;

/// <summary>Persisted identity record. PasswordHash must be produced by a dedicated
/// password hasher before any registration endpoint is enabled.</summary>
public sealed class UserAccount
{
    public Guid Id { get; set; }
    public string EmailNormalized { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class WorkspaceRecord
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public enum WorkspaceRole
{
    Viewer = 1,
    Member = 2,
    Admin = 3,
    Owner = 4
}

public sealed class WorkspaceMembership
{
    public Guid WorkspaceId { get; set; }
    public Guid UserId { get; set; }
    public WorkspaceRole Role { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
