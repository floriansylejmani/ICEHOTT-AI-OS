using ICEHOTT.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ICEHOTT.Infrastructure.Identity;

public sealed record SessionTokens(Guid UserId, Guid FamilyId, string RefreshToken);
public sealed class AuthRejectedException : Exception;
public sealed class AuthConflictException(string message) : Exception(message);

/// <summary>
/// PostgreSQL-authoritative identity. No public actor ID is accepted.
/// Raw high-entropy tokens are emailed/returned once, only hashes are stored.
/// </summary>
public sealed class AccountService(
    IdentityDbContext db,
    IPasswordHasher<UserAccount> hasher,
    IAccountEmailSender sender,
    IOptions<AuthSettings> configuration)
{
    private readonly AuthSettings _settings = configuration.Value;

    public async Task RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var email = AuthCrypto.NormalizeEmail(request.Email);
        AuthCrypto.ValidatePassword(request.Password);
        if (!await db.Users.AnyAsync(x => x.EmailNormalized == email, ct))
        {
            var user = new UserAccount
            {
                Id = Guid.NewGuid(), EmailNormalized = email,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            user.PasswordHash = hasher.HashPassword(user, request.Password);
            db.Users.Add(user);
            IdentityAudit.Record(db, "account.registered", user.Id);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg &&
                pg.SqlState == PostgresErrorCodes.UniqueViolation && pg.ConstraintName == "ux_identity_users_email")
            {
                db.ChangeTracker.Clear();
                return; // race: do not reveal whether an account exists
            }
            await SendEmailActionAsync(user, "verify", ct);
        }
    }

    public async Task SendVerificationAsync(string emailRaw, CancellationToken ct = default)
    {
        var email = AuthCrypto.NormalizeEmail(emailRaw);
        var user = await db.Users.SingleOrDefaultAsync(x => x.EmailNormalized == email && x.DisabledAtUtc == null && x.EmailVerifiedAtUtc == null, ct);
        if (user != null) await SendEmailActionAsync(user, "verify", ct);
    }

    public async Task VerifyEmailAsync(string token, CancellationToken ct = default)
    {
        var record = await ConsumeActionTokenAsync(token, "verify", ct, null);
        if (record == null) throw new AuthRejectedException();
    }

    public async Task RequestResetAsync(string emailRaw, CancellationToken ct = default)
    {
        var email = AuthCrypto.NormalizeEmail(emailRaw);
        var user = await db.Users.SingleOrDefaultAsync(x => x.EmailNormalized == email && x.EmailVerifiedAtUtc != null && x.DisabledAtUtc == null, ct);
        if (user != null) await SendEmailActionAsync(user, "reset", ct);
    }

    public async Task ConfirmResetAsync(PasswordResetConfirm request, CancellationToken ct = default)
    {
        AuthCrypto.ValidatePassword(request.NewPassword);
        var record = await ConsumeActionTokenAsync(request.Token, "reset", ct, request.NewPassword);
        if (record == null) throw new AuthRejectedException();
    }

    private async Task<EmailActionToken?> ConsumeActionTokenAsync(
        string token, string purpose, CancellationToken ct, string? newPassword)
    {
        if (!AuthCrypto.IsPlausibleToken(token)) return null;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var hash = AuthCrypto.HashToken(token);
        var record = await db.EmailTokens
            .FromSqlInterpolated($"SELECT * FROM auth_email_action_tokens WHERE token_hash = {hash} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        if (record == null || record.Purpose != purpose || record.UsedAtUtc != null ||
            record.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            return null;
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == record.UserId, ct);
        if (user == null || user.DisabledAtUtc != null) return null;

        var now = DateTimeOffset.UtcNow;
        record.UsedAtUtc = now;
        IdentityAudit.Record(db, purpose == "verify" ? "email.verified" : "password.reset", user.Id);
        if (purpose == "verify")
        {
            if (user.EmailVerifiedAtUtc == null) user.EmailVerifiedAtUtc = now;
        }
        else
        {
            user.PasswordHash = hasher.HashPassword(user, newPassword!);
            user.FailedLoginCount = 0;
            user.LockoutUntilUtc = null;
            await db.RefreshSessions.Where(x => x.UserId == user.Id && x.RevokedAtUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAtUtc, now), ct);
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return record;
    }

    private async Task SendEmailActionAsync(UserAccount user, string purpose, CancellationToken ct)
    {
        var raw = AuthCrypto.NewToken();
        db.EmailTokens.Add(new EmailActionToken
        {
            Id = Guid.NewGuid(), UserId = user.Id, Purpose = purpose,
            TokenHash = AuthCrypto.HashToken(raw),
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(purpose == "verify" ? 60 : 20)
        });
        await db.SaveChangesAsync(ct);
        var route = purpose == "verify" ? "verify-email" : "reset-password";
        var url = $"{_settings.PublicBaseUrl.TrimEnd('/')}/{route}?token={Uri.EscapeDataString(raw)}";
        await sender.SendAsync(user.EmailNormalized,
            purpose == "verify" ? "Verify your ICEHOTT AI OS email" : "Reset your ICEHOTT AI OS password",
            $"Follow this one-time link (expires shortly): {url}", ct);
    }

    public async Task<SessionTokens> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var email = AuthCrypto.NormalizeEmail(request.Email);
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length > 256)
            throw new AuthRejectedException();

        // Serialize attempts for a known user across every API instance and IP.
        // Avoid race/lost increments when attackers use concurrent clients.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var user = await db.Users.FromSqlInterpolated(
            $"SELECT * FROM identity_users WHERE email_normalized = {email} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        if (user is null || user.DisabledAtUtc != null || user.EmailVerifiedAtUtc == null ||
            (user.LockoutUntilUtc != null && user.LockoutUntilUtc > DateTimeOffset.UtcNow))
            throw new AuthRejectedException();
        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= 5)
            {
                user.FailedLoginCount = 0;
                user.LockoutUntilUtc = DateTimeOffset.UtcNow.AddMinutes(15);
                IdentityAudit.Record(db, "account.locked_by_failed_login", user.Id);
            }
            else
                IdentityAudit.Record(db, "login.failed", user.Id);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            throw new AuthRejectedException();
        }
        user.FailedLoginCount = 0;
        user.LockoutUntilUtc = null;
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = hasher.HashPassword(user, request.Password);
        var session = NewSession(user.Id, Guid.NewGuid());
        db.RefreshSessions.Add(session.Record);
        IdentityAudit.Record(db, "session.login", user.Id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return session.Output;
    }

    public async Task<SessionTokens> RotateAsync(string? rawToken, CancellationToken ct = default)
    {
        if (!AuthCrypto.IsPlausibleToken(rawToken)) throw new AuthRejectedException();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var hash = AuthCrypto.HashToken(rawToken!);
        var old = await db.RefreshSessions
            .FromSqlInterpolated($"SELECT * FROM auth_refresh_sessions WHERE token_hash = {hash} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        if (old is null) throw new AuthRejectedException();

        var now = DateTimeOffset.UtcNow;
        if (old.ReplacedAtUtc != null)
        {
            await RevokeFamilyInternalAsync(old.UserId, old.FamilyId, now, ct);
            IdentityAudit.Record(db, "session.refresh_replay_revoked", old.UserId);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            throw new AuthRejectedException();
        }
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == old.UserId, ct);
        if (old.RevokedAtUtc != null || old.ExpiresAtUtc <= now ||
            user is null || user.DisabledAtUtc != null || user.EmailVerifiedAtUtc == null)
            throw new AuthRejectedException();

        var next = NewSession(old.UserId, old.FamilyId);
        old.ReplacedAtUtc = now;
        old.RevokedAtUtc = now;
        db.RefreshSessions.Add(next.Record);
        IdentityAudit.Record(db, "session.rotated", old.UserId);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return next.Output;
    }

    public async Task RevokeFamilyAsync(Guid userId, Guid familyId, CancellationToken ct = default)
    {
        if (userId == Guid.Empty || familyId == Guid.Empty) throw new AuthRejectedException();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await RevokeFamilyInternalAsync(userId, familyId, DateTimeOffset.UtcNow, ct);
        IdentityAudit.Record(db, "session.logout", userId);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<bool> IsActiveSessionAsync(Guid userId, Guid familyId, CancellationToken ct = default)
    {
        if (userId == Guid.Empty || familyId == Guid.Empty) return false;
        var now = DateTimeOffset.UtcNow;
        return await db.Users.AnyAsync(u => u.Id == userId && u.DisabledAtUtc == null && u.EmailVerifiedAtUtc != null, ct)
            && await db.RefreshSessions.AsNoTracking().AnyAsync(x =>
                x.UserId == userId && x.FamilyId == familyId &&
                x.RevokedAtUtc == null && x.ExpiresAtUtc > now, ct);
    }

    private Task<int> RevokeFamilyInternalAsync(Guid userId, Guid familyId, DateTimeOffset now, CancellationToken ct) =>
        db.RefreshSessions.Where(x => x.UserId == userId && x.FamilyId == familyId && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAtUtc, now), ct);

    private static (RefreshSession Record, SessionTokens Output) NewSession(Guid userId, Guid familyId)
    {
        var raw = AuthCrypto.NewToken();
        return (new RefreshSession
        {
            Id = Guid.NewGuid(), UserId = userId, FamilyId = familyId,
            TokenHash = AuthCrypto.HashToken(raw),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(14)
        }, new SessionTokens(userId, familyId, raw));
    }
}
