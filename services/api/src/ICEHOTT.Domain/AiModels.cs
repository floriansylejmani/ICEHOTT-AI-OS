namespace ICEHOTT.Domain;

public enum AiAttemptStatus : short { Reserved = 1, Completed = 2, Failed = 3 }
public enum AiMessageRole : short { User = 1, Assistant = 2 }

public sealed class AiConversation
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Title { get; set; } = "";
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
public sealed class AiMessage
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ConversationId { get; set; }
    public AiMessageRole Role { get; set; }
    public string Content { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; }
}
public sealed class AiAttempt
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid ActorId { get; set; }
    public Guid RequestId { get; set; }
    public Guid TraceId { get; set; }
    public AiAttemptStatus Status { get; set; }
    public string InputHash { get; set; } = "";
    public int ReservedTokens { get; set; }
    public int UsedTokens { get; set; }
    public string Provider { get; set; } = "";
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? FinishedAtUtc { get; set; }
    public Guid? ReplyMessageId { get; set; }
}
public sealed record NewAiConversation(string? Title);
public sealed record AiSendRequest(Guid RequestId, string Message);
public sealed record AiChatReply(Guid ConversationId, Guid RequestId, Guid TraceId, string Answer, int UsedTokens, string Provider);
public sealed record AiConversationView(Guid Id, string Title, DateTimeOffset UpdatedAtUtc);
public sealed record AiMessageView(Guid Id, AiMessageRole Role, string Content, DateTimeOffset CreatedAtUtc);
public sealed record AiUsageView(int RequestsToday, int DailyRequestLimit, int ReservedTokensToday, int DailyTokenLimit);
