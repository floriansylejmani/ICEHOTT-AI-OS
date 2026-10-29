using System.Security.Cryptography;
using System.Text;
using ICEHOTT.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ICEHOTT.Infrastructure.Identity;

public sealed class AiQuotaException : Exception;
public sealed class AiRuntimeUnavailableException : Exception;

/// <summary>
/// PostgreSQL is authority for tenant isolation, daily usage reservation,
/// duplicate request IDs and stored turns. No provider call inside DB transaction.
/// Failed/reserved attempts retain quota to prevent retry-spend amplification.
/// </summary>
public sealed class AiConversationService(
    IdentityDbContext db, WorkspaceAccessService access,
    IAiRuntime runtime, IOptions<AiCoreSettings> configuration)
{
    private readonly AiCoreSettings _policy = configuration.Value;

    private async Task RequireAsync(Guid actor, Guid workspace, WorkspaceRole minimum, CancellationToken ct)
    {
        if (actor == Guid.Empty || workspace == Guid.Empty ||
            !await access.HasRoleAsync(actor, workspace, minimum, ct))
            throw new UnauthorizedAccessException();
    }

    public async Task<List<AiConversationView>> ListAsync(Guid actor, Guid workspace, CancellationToken ct)
    {
        await RequireAsync(actor, workspace, WorkspaceRole.Viewer, ct);
        return await db.AiConversations.AsNoTracking()
            .Where(x => x.WorkspaceId == workspace)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(100)
            .Select(x => new AiConversationView(x.Id, x.Title, x.UpdatedAtUtc)).ToListAsync(ct);
    }

    public async Task<AiConversationView> CreateAsync(Guid actor, Guid workspace,
        NewAiConversation input, CancellationToken ct)
    {
        var title = (input.Title ?? "New conversation").Trim();
        if (title.Length is < 1 or > 100) throw new ArgumentException("Invalid conversation title.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockWorkspaceAsync(workspace, ct);
        await RequireAsync(actor, workspace, WorkspaceRole.Member, ct);
        if (await db.AiConversations.CountAsync(x => x.WorkspaceId == workspace, ct) >= 100)
            throw new AuthConflictException("Conversation limit.");
        var now = DateTimeOffset.UtcNow;
        var chat = new AiConversation
        {
            Id = Guid.NewGuid(), WorkspaceId = workspace, CreatedByUserId = actor,
            Title = title, CreatedAtUtc = now, UpdatedAtUtc = now
        };
        db.AiConversations.Add(chat);
        IdentityAudit.Record(db, "ai.conversation.created", actor, workspace);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new AiConversationView(chat.Id, chat.Title, chat.UpdatedAtUtc);
    }

    public async Task<List<AiMessageView>> MessagesAsync(Guid actor, Guid workspace, Guid conversation, CancellationToken ct)
    {
        await RequireAsync(actor, workspace, WorkspaceRole.Viewer, ct);
        if (!await db.AiConversations.AnyAsync(x => x.WorkspaceId == workspace && x.Id == conversation, ct))
            throw new KeyNotFoundException();
        return await db.AiMessages.AsNoTracking().Where(x => x.WorkspaceId == workspace &&
            x.ConversationId == conversation)
            .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id).Take(500)
            .Select(x => new AiMessageView(x.Id, x.Role, x.Content, x.CreatedAtUtc)).ToListAsync(ct);
    }

    public async Task<AiUsageView> UsageAsync(Guid actor, Guid workspace, CancellationToken ct)
    {
        await RequireAsync(actor, workspace, WorkspaceRole.Viewer, ct);
        var today = DateTimeOffset.UtcNow.Date;
        var used = await db.AiAttempts.AsNoTracking()
            .Where(x => x.WorkspaceId == workspace && x.StartedAtUtc >= new DateTimeOffset(today, TimeSpan.Zero))
            .GroupBy(x => x.WorkspaceId).Select(g => new
            {
                Count = g.Count(),
                Reserved = g.Sum(x => x.ReservedTokens)
            }).SingleOrDefaultAsync(ct);
        return new AiUsageView(used?.Count ?? 0, _policy.DailyRequestLimit,
            used?.Reserved ?? 0, _policy.DailyTokenLimit);
    }

    public async Task<AiChatReply> SendAsync(Guid actor, Guid workspace, Guid conversation,
        AiSendRequest input, CancellationToken ct)
    {
        if (!_policy.Enabled) throw new AiRuntimeUnavailableException();
        await RequireAsync(actor, workspace, WorkspaceRole.Member, ct);
        if (input.RequestId == Guid.Empty || string.IsNullOrWhiteSpace(input.Message) ||
            input.Message.Length > 4000 || input.Message.Any(char.IsControl) &&
                input.Message.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
            throw new ArgumentException("Invalid chat message or request ID.");

        var prompt = input.Message.Trim();
        if (prompt.Length == 0) throw new ArgumentException("Empty chat message.");
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(prompt)));
        var now = DateTimeOffset.UtcNow;
        var reserved = 0;
        AiAttempt attempt;
        List<string> context;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await LockWorkspaceAsync(workspace, ct);
            await RequireAsync(actor, workspace, WorkspaceRole.Member, ct);
            if (!await db.AiConversations.AnyAsync(x => x.Id == conversation && x.WorkspaceId == workspace, ct))
                throw new KeyNotFoundException();

            var prior = await db.AiAttempts.AsNoTracking().SingleOrDefaultAsync(x =>
                x.WorkspaceId == workspace && x.RequestId == input.RequestId, ct);
            if (prior != null)
            {
                if (prior.ActorId != actor || prior.ConversationId != conversation ||
                    prior.InputHash != fingerprint) throw new AuthConflictException("Request ID conflict.");
                if (prior.Status != AiAttemptStatus.Completed || prior.ReplyMessageId == null)
                    throw new AuthConflictException("Request already admitted.");
                var reply = await db.AiMessages.AsNoTracking().SingleAsync(x =>
                    x.WorkspaceId == workspace && x.ConversationId == conversation && x.Id == prior.ReplyMessageId, ct);
                return new AiChatReply(conversation, prior.RequestId, prior.TraceId,
                    reply.Content, prior.UsedTokens, prior.Provider);
            }
            var recent = await db.AiAttempts.CountAsync(x =>
                x.WorkspaceId == workspace && x.StartedAtUtc > now.AddMinutes(-1), ct);
            if (recent >= _policy.PerMinuteRequestLimit) throw new AiQuotaException();
            var cutoff = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
            var stats = await db.AiAttempts.AsNoTracking()
                .Where(x => x.WorkspaceId == workspace && x.StartedAtUtc >= cutoff)
                .GroupBy(x => x.WorkspaceId).Select(g => new { Count = g.Count(), Reserved = g.Sum(x => x.ReservedTokens) })
                .SingleOrDefaultAsync(ct);
            context = await db.AiMessages.AsNoTracking()
                .Where(x => x.WorkspaceId == workspace && x.ConversationId == conversation)
                .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id).Take(6)
                .Select(x => x.Content).ToListAsync(ct);
            context.Reverse();
            context = context.Select(x => x.Length > 1000 ? x[..1000] : x).ToList();
            // Conservative allowance includes history sent to the provider, not
            // merely the current prompt. Workspace lock serializes reservations.
            reserved = (prompt.Length + context.Sum(x => x.Length) + 1) / 2 + 768;
            if ((stats?.Count ?? 0) >= _policy.DailyRequestLimit ||
                (stats?.Reserved ?? 0) + reserved > _policy.DailyTokenLimit)
                throw new AiQuotaException();
            attempt = new AiAttempt
            {
                Id = Guid.NewGuid(), WorkspaceId = workspace, ConversationId = conversation,
                ActorId = actor, RequestId = input.RequestId, TraceId = Guid.NewGuid(),
                InputHash = fingerprint, ReservedTokens = reserved,
                StartedAtUtc = now, Status = AiAttemptStatus.Reserved, Provider = "pending"
            };
            db.AiAttempts.Add(attempt);
            IdentityAudit.Record(db, "ai.request.admitted", actor, workspace);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        db.ChangeTracker.Clear();
        try
        {
            // Cancellation/timeouts still consume a safe reserved allowance. Repeats
            // with the same requestId never trigger another provider request.
            var output = await runtime.GenerateAsync(new AiRuntimeRequest(prompt, context, 512, attempt.TraceId), ct);
            if (string.IsNullOrWhiteSpace(output.Answer) || output.Answer.Length > 4096 ||
                output.UsedTokens is <= 0 or > 2000 || output.UsedTokens > reserved ||
                output.Provider.Length is < 1 or > 40)
                throw new InvalidOperationException("Invalid provider output.");

            await using var tx = await db.Database.BeginTransactionAsync(CancellationToken.None);
            await LockWorkspaceAsync(workspace, CancellationToken.None);
            await RequireAsync(actor, workspace, WorkspaceRole.Member, CancellationToken.None);
            var current = await db.AiAttempts.SingleAsync(x => x.Id == attempt.Id &&
                x.WorkspaceId == workspace, CancellationToken.None);
            if (current.Status != AiAttemptStatus.Reserved) throw new AuthConflictException("Already settled.");
            var stamp = DateTimeOffset.UtcNow;
            var userMessage = new AiMessage
            {
                Id = Guid.NewGuid(), WorkspaceId = workspace, ConversationId = conversation,
                Role = AiMessageRole.User, Content = prompt, CreatedAtUtc = stamp
            };
            var answerMessage = new AiMessage
            {
                Id = Guid.NewGuid(), WorkspaceId = workspace, ConversationId = conversation,
                Role = AiMessageRole.Assistant, Content = output.Answer,
                CreatedAtUtc = stamp.AddMilliseconds(1)
            };
            db.AiMessages.AddRange(userMessage, answerMessage);
            current.Status = AiAttemptStatus.Completed;
            current.UsedTokens = output.UsedTokens;
            current.Provider = output.Provider;
            current.ReplyMessageId = answerMessage.Id;
            current.FinishedAtUtc = stamp;
            var chat = await db.AiConversations.SingleAsync(x => x.WorkspaceId == workspace &&
                x.Id == conversation, CancellationToken.None);
            chat.UpdatedAtUtc = stamp;
            IdentityAudit.Record(db, "ai.request.completed", actor, workspace);
            await db.SaveChangesAsync(CancellationToken.None);
            await tx.CommitAsync(CancellationToken.None);
            return new AiChatReply(conversation, attempt.RequestId, attempt.TraceId,
                output.Answer, output.UsedTokens, output.Provider);
        }
        catch (Exception)
        {
            // Fail closed, never log/store the prompt or provider's raw exception.
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(CancellationToken.None);
            await LockWorkspaceAsync(workspace, CancellationToken.None);
            var current = await db.AiAttempts.SingleAsync(x =>
                x.Id == attempt.Id && x.WorkspaceId == workspace, CancellationToken.None);
            if (current.Status == AiAttemptStatus.Reserved)
            {
                current.Status = AiAttemptStatus.Failed;
                current.FinishedAtUtc = DateTimeOffset.UtcNow;
                current.Provider = "failed";
                IdentityAudit.Record(db, "ai.request.failed", actor, workspace);
                await db.SaveChangesAsync(CancellationToken.None);
            }
            await tx.CommitAsync(CancellationToken.None);
            throw new AiRuntimeUnavailableException();
        }
    }

    private async Task LockWorkspaceAsync(Guid workspace, CancellationToken ct)
    {
        if (workspace == Guid.Empty) throw new UnauthorizedAccessException();
        if (!await db.Workspaces.FromSqlInterpolated(
            $"SELECT * FROM workspaces WHERE id = {workspace} FOR UPDATE").AnyAsync(ct))
            throw new KeyNotFoundException();
    }
}
