namespace StaySphere.Contracts.Ai;

public sealed record AssistantMessageRequest(Guid? ConversationId, string Message);

public sealed record AssistantReplyDto(Guid ConversationId, string Reply, IReadOnlyList<AssistantPropertySuggestionDto> Properties,
    PendingActionDto? PendingAction, IReadOnlyList<string> ToolsUsed, string Provider);

public sealed record AssistantPropertySuggestionDto(Guid Id, string Title, string City, string Country, decimal NightlyPrice, string Currency,
    decimal? TotalPrice, double Rating, int ReviewCount, string? ImageUrl, string Reason);

public sealed record PendingActionDto(Guid Id, string Kind, string Summary, DateTimeOffset ExpiresAt);

public sealed record ConfirmActionRequest(bool Approve);

public sealed record ConfirmActionResultDto(Guid ActionId, string Status, string Message, Guid? ReservationId);
