namespace StaySphere.Contracts.Ai;

/// <param name="Agent">travel (default), recommendation, host, support or admin. See GET /ai/agents for what the caller may use.</param>
public sealed record AssistantMessageRequest(Guid? ConversationId, string Message, string? Agent = null);

public sealed record AgentInfoDto(string Key, string Name, string DisplayName, string Description, IReadOnlyList<string> Tools);

public sealed record AssistantReplyDto(Guid ConversationId, string Reply, IReadOnlyList<AssistantPropertySuggestionDto> Properties,
    PendingActionDto? PendingAction, IReadOnlyList<string> ToolsUsed, string Provider, string Agent = "travel");

public sealed record AssistantPropertySuggestionDto(Guid Id, string Title, string City, string Country, decimal NightlyPrice, string Currency,
    decimal? TotalPrice, double Rating, int ReviewCount, string? ImageUrl, string Reason);

public sealed record PendingActionDto(Guid Id, string Kind, string Summary, DateTimeOffset ExpiresAt);

public sealed record ConfirmActionRequest(bool Approve);

public sealed record ConfirmActionResultDto(Guid ActionId, string Status, string Message, Guid? ReservationId);
