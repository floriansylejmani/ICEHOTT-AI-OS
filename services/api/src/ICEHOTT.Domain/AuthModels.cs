namespace ICEHOTT.Domain;

public sealed class RefreshSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid FamilyId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public DateTimeOffset? ReplacedAtUtc { get; set; }
}

public sealed class EmailActionToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = "";
    public string Purpose { get; set; } = "";
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? UsedAtUtc { get; set; }
}

public sealed class WorkspaceInvitation
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid InvitedByUserId { get; set; }
    public string EmailNormalized { get; set; } = "";
    public WorkspaceRole Role { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? AcceptedAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
}

public sealed class IdentityAuditEvent
{
    public Guid Id { get; set; }
    public Guid? ActorUserId { get; set; }
    public Guid? WorkspaceId { get; set; }
    public string Action { get; set; } = "";
    public DateTimeOffset HappenedAtUtc { get; set; }
}
