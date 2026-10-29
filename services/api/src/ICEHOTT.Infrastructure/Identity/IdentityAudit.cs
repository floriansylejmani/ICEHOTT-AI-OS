using ICEHOTT.Domain;

namespace ICEHOTT.Infrastructure.Identity;

internal static class IdentityAudit
{
    // Intentionally no token hashes, emails, raw IPs or secrets in audit entries.
    public static void Record(IdentityDbContext db, string action, Guid? actor = null, Guid? workspace = null)
    {
        db.AuditEvents.Add(new IdentityAuditEvent
        {
            Id = Guid.NewGuid(), Action = action, ActorUserId = actor,
            WorkspaceId = workspace, HappenedAtUtc = DateTimeOffset.UtcNow
        });
    }
}
