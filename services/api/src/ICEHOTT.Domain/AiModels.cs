namespace ICEHOTT.Domain;

public sealed record AiConversationView(Guid Id, string Title, DateTimeOffset UpdatedAtUtc);
public sealed record AiCreateConversation(string Title);
public sealed record AiChatInput(string Text, Guid RequestId);
public sealed record AiMessageView(Guid Id, string Role, string Content, string Provider,
    int UsedTokens, DateTimeOffset CreatedAtUtc);
public sealed record AiChatResponse(AiMessageView Message, Guid TraceId);
public sealed record AiUsageView(int RequestsToday, int MaxRequestsPerDay, long UsedTokensToday);
