namespace StaySphere.Contracts.Support;

public sealed record CreateTicketRequest(Guid? ReservationId, string Category, string Priority, string Subject, string Description);
public sealed record TicketMessageRequest(string Body);
public sealed record UpdateTicketRequest(string Status, string Priority, Guid? AssigneeId);

public sealed record TicketDto(Guid Id, Guid UserId, string UserName, Guid? ReservationId, string Category, string Priority, string Status,
    string Subject, string Description, Guid? AssigneeId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    IReadOnlyList<TicketMessageDto> Messages);

public sealed record TicketMessageDto(Guid Id, Guid AuthorId, string AuthorName, bool FromAgent, string Body, DateTimeOffset At);

public sealed record CreateReportRequest(string TargetType, Guid TargetId, string Reason);
