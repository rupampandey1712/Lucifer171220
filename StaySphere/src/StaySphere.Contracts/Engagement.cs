namespace StaySphere.Contracts.Engagement;

public sealed record StartConversationRequest(Guid PropertyId, Guid? ReservationId, string Message);
public sealed record SendMessageRequest(string Body);

public sealed record ConversationDto(Guid Id, Guid PropertyId, string PropertyTitle, string? PropertyImageUrl, Guid? ReservationId,
    Guid OtherUserId, string OtherUserName, string? OtherUserAvatarUrl, string? LastMessage, DateTimeOffset LastMessageAt, int UnreadCount);

public sealed record MessageDto(Guid Id, Guid ConversationId, Guid SenderId, string Body, DateTimeOffset SentAt, bool IsRead);

public sealed record NotificationDto(Guid Id, string Type, string Title, string Body, string? Link, bool IsRead, DateTimeOffset CreatedAt);
