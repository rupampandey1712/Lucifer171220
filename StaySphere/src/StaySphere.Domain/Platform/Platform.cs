namespace StaySphere.Domain.Platform;

/// <summary>Immutable audit record (who did what, when, from where, correlated to a trace).</summary>
public sealed class AuditLog
{
    private AuditLog() { }

    public long Id { get; private set; }
    public Guid? ActorId { get; private set; }
    public string Action { get; private set; } = default!;
    public string EntityType { get; private set; } = default!;
    public string? EntityId { get; private set; }
    public string? Details { get; private set; }
    public string? IpAddress { get; private set; }
    public string? CorrelationId { get; private set; }
    public DateTimeOffset At { get; private set; }

    public static AuditLog Create(Guid? actorId, string action, string entityType, string? entityId, string? details,
        string? ip, string? correlationId, DateTimeOffset at) =>
        new()
        {
            ActorId = actorId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details,
            IpAddress = ip,
            CorrelationId = correlationId,
            At = at,
        };
}

/// <summary>Transactional outbox row. Written in the same transaction as the business change; published afterwards.</summary>
public sealed class OutboxMessage
{
    private OutboxMessage() { }

    public Guid Id { get; private set; }
    public string Type { get; private set; } = default!;
    public string Payload { get; private set; } = default!;
    public string? CorrelationId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public DateTimeOffset? NextAttemptAt { get; private set; }
    public DateTimeOffset? FailedAt { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }

    public static OutboxMessage Create(string type, string payload, string? correlationId, DateTimeOffset now) =>
        new() { Id = Guid.CreateVersion7(), Type = type, Payload = payload, CorrelationId = correlationId, OccurredAt = now };

    public const int MaxAttempts = 10;

    public void MarkProcessed(DateTimeOffset now) => ProcessedAt = now;

    public void MarkFailedAttempt(string error, DateTimeOffset now)
    {
        Attempts++;
        LastError = error.Length > 2000 ? error[..2000] : error;
        if (Attempts >= MaxAttempts)
            FailedAt = now;
        else
            NextAttemptAt = now.AddSeconds(Math.Min(300, Math.Pow(2, Attempts)));
    }
}

/// <summary>Consumer-side de-duplication: (MessageId, Consumer) processed at most once.</summary>
public sealed class InboxMessage
{
    private InboxMessage() { }

    public InboxMessage(Guid messageId, string consumer, DateTimeOffset processedAt)
    {
        MessageId = messageId;
        Consumer = consumer;
        ProcessedAt = processedAt;
    }

    public Guid MessageId { get; private set; }
    public string Consumer { get; private set; } = default!;
    public DateTimeOffset ProcessedAt { get; private set; }
}

/// <summary>Stored response for an Idempotency-Key so retries replay rather than re-execute.</summary>
public sealed class IdempotencyRecord
{
    private IdempotencyRecord() { }

    public string Scope { get; private set; } = default!;
    public Guid UserId { get; private set; }
    public string Key { get; private set; } = default!;
    public string RequestHash { get; private set; } = default!;
    public int? ResponseStatus { get; private set; }
    public string? ResponseBody { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    public bool IsCompleted => ResponseStatus is not null;

    public static IdempotencyRecord Begin(string scope, Guid userId, string key, string requestHash, DateTimeOffset now) =>
        new() { Scope = scope, UserId = userId, Key = key, RequestHash = requestHash, CreatedAt = now, ExpiresAt = now.AddHours(24) };

    public void Complete(int status, string? body)
    {
        ResponseStatus = status;
        ResponseBody = body;
    }
}

public enum PendingActionStatus
{
    Pending,
    Approved,
    Rejected,
    Expired,
}

/// <summary>An AI-proposed write/financial action awaiting explicit human confirmation in the UI.</summary>
public sealed class AiPendingAction
{
    private AiPendingAction() { }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string ToolName { get; private set; } = default!;
    public string ArgumentsJson { get; private set; } = default!;
    public string Summary { get; private set; } = default!;
    public PendingActionStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public string? ResultJson { get; private set; }

    public static AiPendingAction Create(Guid userId, string toolName, string argumentsJson, string summary, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            ToolName = toolName,
            ArgumentsJson = argumentsJson,
            Summary = summary,
            Status = PendingActionStatus.Pending,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(10),
        };

    public bool CanExecute(Guid userId, DateTimeOffset now) => UserId == userId && Status == PendingActionStatus.Pending && ExpiresAt > now;

    public void Approve(string? resultJson)
    {
        Status = PendingActionStatus.Approved;
        ResultJson = resultJson;
    }

    public void Reject() => Status = PendingActionStatus.Rejected;
}
