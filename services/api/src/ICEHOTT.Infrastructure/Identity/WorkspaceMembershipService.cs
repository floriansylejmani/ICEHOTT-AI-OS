using ICEHOTT.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ICEHOTT.Infrastructure.Identity;

/// <summary>Member modifications serialized per workspace and protected by
/// DB-verified membership. Never take the acting user from an HTTP request body.</summary>
public sealed class WorkspaceMembershipService(
    IdentityDbContext db, IAccountEmailSender sender, IOptions<AuthSettings> configuration,
    Microsoft.AspNetCore.Identity.IPasswordHasher<UserAccount> passwordHasher)
{
    /// <summary>Invitation email token acts as proof of mailbox control.
    /// Works while public registration is OFF; account + membership are atomic.</summary>
    public async Task RegisterWithInvitationAsync(InviteRegisterRequest request, CancellationToken ct = default)
    {
        AuthCrypto.ValidatePassword(request.Password);
        if (!AuthCrypto.IsPlausibleToken(request.Token)) throw new AuthRejectedException();
        var hash = AuthCrypto.HashToken(request.Token);
        var workspaceId = await db.Invitations.AsNoTracking().Where(i => i.TokenHash == hash)
            .Select(i => i.WorkspaceId).SingleOrDefaultAsync(ct);
        if (workspaceId == Guid.Empty) throw new AuthRejectedException();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockedWorkspaceAsync(workspaceId, ct);
        var invite = await db.Invitations.SingleOrDefaultAsync(i => i.TokenHash == hash, ct);
        if (invite == null || invite.AcceptedAtUtc != null || invite.RevokedAtUtc != null ||
            invite.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            throw new AuthRejectedException();
        if (await db.Users.AnyAsync(u => u.EmailNormalized == invite.EmailNormalized, ct))
            throw new AuthConflictException("Sign in to accept this invitation.");

        var now = DateTimeOffset.UtcNow;
        var user = new UserAccount
        {
            Id = Guid.NewGuid(),
            EmailNormalized = invite.EmailNormalized,
            CreatedAtUtc = now,
            EmailVerifiedAtUtc = now
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
        db.Users.Add(user);
        db.Memberships.Add(new WorkspaceMembership
        {
            UserId = user.Id, WorkspaceId = workspaceId,
            Role = invite.Role, CreatedAtUtc = now
        });
        invite.AcceptedAtUtc = now;
        IdentityAudit.Record(db, "account.invite_registered", user.Id, workspaceId);
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException pg &&
            pg.SqlState == Npgsql.PostgresErrorCodes.UniqueViolation &&
            pg.ConstraintName == "ux_identity_users_email")
        {
            throw new AuthConflictException("Sign in to accept this invitation.");
        }
    }

    public async Task InviteAsync(Guid actorId, Guid workspaceId, InviteWorkspaceRequest request,
        CancellationToken ct = default)
    {
        var email = AuthCrypto.NormalizeEmail(request.Email);
        if (request.Role is not (WorkspaceRole.Viewer or WorkspaceRole.Member or WorkspaceRole.Admin))
            throw new ArgumentException("Invalid invited role.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var workspace = await LockedWorkspaceAsync(workspaceId, ct);
        await RequireRoleAsync(actorId, workspaceId, WorkspaceRole.Admin, ct);
        var actor = await db.Memberships.SingleAsync(m => m.WorkspaceId == workspaceId && m.UserId == actorId, ct);
        if (actor.Role == WorkspaceRole.Admin && request.Role == WorkspaceRole.Admin)
            throw new UnauthorizedAccessException();
        if (await (from member in db.Memberships
                   join u in db.Users on member.UserId equals u.Id
                   where member.WorkspaceId == workspaceId && u.EmailNormalized == email
                   select member.UserId).AnyAsync(ct))
            throw new AuthConflictException("Already a member.");

        // Invalidate previous pending invitation to this same email/workspace.
        var pending = await db.Invitations.Where(i => i.WorkspaceId == workspaceId &&
            i.EmailNormalized == email && i.AcceptedAtUtc == null && i.RevokedAtUtc == null).ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        foreach (var old in pending) old.RevokedAtUtc = now;

        var raw = AuthCrypto.NewToken();
        IdentityAudit.Record(db, "workspace.invitation.created", actorId, workspaceId);
        db.Invitations.Add(new WorkspaceInvitation
        {
            Id = Guid.NewGuid(), WorkspaceId = workspaceId, InvitedByUserId = actorId,
            EmailNormalized = email, Role = request.Role, TokenHash = AuthCrypto.HashToken(raw),
            ExpiresAtUtc = now.AddDays(3)
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        var link = $"{configuration.Value.PublicBaseUrl.TrimEnd('/')}/accept-invite?token={Uri.EscapeDataString(raw)}";
        await sender.SendAsync(email, "ICEHOTT AI OS workspace invitation",
            $"You were invited to {workspace.DisplayName}. Accept within 3 days: {link}", ct);
    }

    public async Task<WorkspaceSummary> AcceptAsync(Guid actorId, string? token, CancellationToken ct = default)
    {
        if (actorId == Guid.Empty || !AuthCrypto.IsPlausibleToken(token))
            throw new AuthRejectedException();
        var hash = AuthCrypto.HashToken(token!);
        // Lookup only determines workspace lock order; re-validated after lock.
        var candidate = await db.Invitations.AsNoTracking()
            .Where(x => x.TokenHash == hash).Select(x => x.WorkspaceId).SingleOrDefaultAsync(ct);
        if (candidate == Guid.Empty) throw new AuthRejectedException();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var workspace = await LockedWorkspaceAsync(candidate, ct);
        var invitation = await db.Invitations.SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == actorId, ct);
        if (invitation == null || invitation.RevokedAtUtc != null || invitation.AcceptedAtUtc != null ||
            invitation.ExpiresAtUtc <= DateTimeOffset.UtcNow || user == null ||
            user.EmailVerifiedAtUtc == null || user.DisabledAtUtc != null ||
            user.EmailNormalized != invitation.EmailNormalized)
            throw new AuthRejectedException();
        if (await db.Memberships.AnyAsync(x => x.WorkspaceId == candidate && x.UserId == actorId, ct))
            throw new AuthConflictException("Already a member.");
        invitation.AcceptedAtUtc = DateTimeOffset.UtcNow;
        IdentityAudit.Record(db, "workspace.invitation.accepted", actorId, candidate);
        db.Memberships.Add(new WorkspaceMembership
        {
            WorkspaceId = candidate, UserId = actorId, Role = invitation.Role,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new WorkspaceSummary(candidate, workspace.Slug, workspace.DisplayName, invitation.Role);
    }

    public async Task RevokeInvitationAsync(Guid actorId, Guid workspaceId, Guid invitationId, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockedWorkspaceAsync(workspaceId, ct);
        await RequireRoleAsync(actorId, workspaceId, WorkspaceRole.Admin, ct);
        var invite = await db.Invitations.SingleOrDefaultAsync(x =>
            x.Id == invitationId && x.WorkspaceId == workspaceId && x.AcceptedAtUtc == null, ct);
        if (invite == null) throw new KeyNotFoundException();
        invite.RevokedAtUtc = DateTimeOffset.UtcNow;
        IdentityAudit.Record(db, "workspace.invitation.revoked", actorId, workspaceId);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task ChangeRoleAsync(Guid actorId, Guid workspaceId, Guid targetId, WorkspaceRole newRole,
        CancellationToken ct = default)
    {
        if (newRole is not (WorkspaceRole.Viewer or WorkspaceRole.Member or WorkspaceRole.Admin))
            throw new ArgumentException("Owner transfers require the transfer endpoint.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockedWorkspaceAsync(workspaceId, ct);
        await RequireRoleAsync(actorId, workspaceId, WorkspaceRole.Owner, ct);
        var target = await db.Memberships.SingleOrDefaultAsync(x =>
            x.WorkspaceId == workspaceId && x.UserId == targetId, ct);
        if (target == null) throw new KeyNotFoundException();
        if (target.Role == WorkspaceRole.Owner) throw new AuthConflictException("Cannot demote owner.");
        target.Role = newRole;
        IdentityAudit.Record(db, "workspace.role.changed", actorId, workspaceId);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task RemoveAsync(Guid actorId, Guid workspaceId, Guid targetId, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockedWorkspaceAsync(workspaceId, ct);
        await RequireRoleAsync(actorId, workspaceId, WorkspaceRole.Admin, ct);
        var actor = await db.Memberships.SingleAsync(x =>
            x.WorkspaceId == workspaceId && x.UserId == actorId, ct);
        var target = await db.Memberships.SingleOrDefaultAsync(x =>
            x.WorkspaceId == workspaceId && x.UserId == targetId, ct);
        if (target == null) throw new KeyNotFoundException();
        if (target.Role == WorkspaceRole.Owner ||
            (actor.Role == WorkspaceRole.Admin && target.Role >= WorkspaceRole.Admin))
            throw new UnauthorizedAccessException("Insufficient permission.");
        db.Memberships.Remove(target);
        IdentityAudit.Record(db, "workspace.member.removed", actorId, workspaceId);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task TransferOwnershipAsync(Guid actorId, Guid workspaceId, Guid targetId, CancellationToken ct = default)
    {
        if (actorId == targetId) throw new ArgumentException("Select another member.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockedWorkspaceAsync(workspaceId, ct);
        await RequireRoleAsync(actorId, workspaceId, WorkspaceRole.Owner, ct);
        var owner = await db.Memberships.SingleAsync(x =>
            x.WorkspaceId == workspaceId && x.UserId == actorId, ct);
        var target = await db.Memberships.SingleOrDefaultAsync(x =>
            x.WorkspaceId == workspaceId && x.UserId == targetId, ct);
        if (target is null) throw new KeyNotFoundException();
        owner.Role = WorkspaceRole.Admin;
        target.Role = WorkspaceRole.Owner;
        IdentityAudit.Record(db, "workspace.ownership.transferred", actorId, workspaceId);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<List<MemberSummary>> ListMembersAsync(Guid actorId, Guid workspaceId, CancellationToken ct = default)
    {
        await RequireRoleAsync(actorId, workspaceId, WorkspaceRole.Viewer, ct);
        return await (from m in db.Memberships.AsNoTracking()
                      join u in db.Users.AsNoTracking() on m.UserId equals u.Id
                      where m.WorkspaceId == workspaceId
                      orderby u.EmailNormalized
                      select new MemberSummary(u.Id, u.EmailNormalized, m.Role)).ToListAsync(ct);
    }

    public async Task<List<InvitationSummary>> ListPendingInvitationsAsync(Guid actorId, Guid workspaceId, CancellationToken ct = default)
    {
        await RequireRoleAsync(actorId, workspaceId, WorkspaceRole.Admin, ct);
        return await db.Invitations.AsNoTracking().Where(x =>
            x.WorkspaceId == workspaceId && x.AcceptedAtUtc == null &&
            x.RevokedAtUtc == null && x.ExpiresAtUtc > DateTimeOffset.UtcNow)
            .OrderBy(x => x.EmailNormalized)
            .Select(x => new InvitationSummary(x.Id, x.EmailNormalized, x.Role, x.ExpiresAtUtc))
            .ToListAsync(ct);
    }

    private async Task<WorkspaceRecord> LockedWorkspaceAsync(Guid workspaceId, CancellationToken ct) =>
        await db.Workspaces.FromSqlInterpolated(
            $"SELECT * FROM workspaces WHERE id = {workspaceId} FOR UPDATE").SingleOrDefaultAsync(ct)
        ?? throw new KeyNotFoundException("Workspace missing.");

    private async Task RequireRoleAsync(Guid actorId, Guid workspaceId, WorkspaceRole role, CancellationToken ct)
    {
        if (!await new WorkspaceAccessService(db).HasRoleAsync(actorId, workspaceId, role, ct))
            throw new UnauthorizedAccessException();
    }
}

public sealed record MemberSummary(Guid UserId, string Email, WorkspaceRole Role);
public sealed record InvitationSummary(Guid Id, string Email, WorkspaceRole Role, DateTimeOffset ExpiresAtUtc);
