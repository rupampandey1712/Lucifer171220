using StaySphere.Domain.Common;

namespace StaySphere.Domain.Trust;

public enum TicketStatus
{
    Open,
    InProgress,
    WaitingForUser,
    Resolved,
    Closed,
}

public enum TicketPriority
{
    Low,
    Normal,
    High,
    Urgent,
}

public sealed class SupportTicket : AggregateRoot
{
    private readonly List<TicketMessage> _messages = [];

    private SupportTicket() { }

    public Guid UserId { get; private set; }
    public Guid? ReservationId { get; private set; }
    public string Category { get; private set; } = default!;
    public TicketPriority Priority { get; private set; }
    public TicketStatus Status { get; private set; }
    public string Subject { get; private set; } = default!;
    public string Description { get; private set; } = default!;
    public Guid? AssigneeId { get; private set; }
    public IReadOnlyCollection<TicketMessage> Messages => _messages;

    public static SupportTicket Open(Guid userId, Guid? reservationId, string category, TicketPriority priority, string subject,
        string description, DateTimeOffset now) =>
        new()
        {
            UserId = userId,
            ReservationId = reservationId,
            Category = category,
            Priority = priority,
            Status = TicketStatus.Open,
            Subject = subject.Trim(),
            Description = description.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };

    public TicketMessage AddMessage(Guid authorId, bool fromAgent, string body, DateTimeOffset now)
    {
        var message = new TicketMessage(Id, authorId, fromAgent, body.Trim(), now);
        _messages.Add(message);
        if (fromAgent && Status == TicketStatus.Open) Status = TicketStatus.InProgress;
        if (!fromAgent && Status == TicketStatus.WaitingForUser) Status = TicketStatus.InProgress;
        UpdatedAt = now;
        return message;
    }

    public void Update(TicketStatus status, TicketPriority priority, Guid? assigneeId)
    {
        Status = status;
        Priority = priority;
        AssigneeId = assigneeId;
    }
}

public sealed class TicketMessage : Entity
{
    private TicketMessage() { }

    internal TicketMessage(Guid ticketId, Guid authorId, bool fromAgent, string body, DateTimeOffset at)
    {
        TicketId = ticketId;
        AuthorId = authorId;
        FromAgent = fromAgent;
        Body = body;
        At = at;
    }

    public Guid TicketId { get; private set; }
    public Guid AuthorId { get; private set; }
    public bool FromAgent { get; private set; }
    public string Body { get; private set; } = default!;
    public DateTimeOffset At { get; private set; }
}

public enum ReportStatus
{
    Open,
    Actioned,
    Dismissed,
}

public sealed class Report : Entity
{
    private Report() { }

    public Guid ReporterId { get; private set; }
    public string TargetType { get; private set; } = default!;
    public Guid TargetId { get; private set; }
    public string Reason { get; private set; } = default!;
    public ReportStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static Report File(Guid reporterId, string targetType, Guid targetId, string reason, DateTimeOffset now) =>
        new() { ReporterId = reporterId, TargetType = targetType, TargetId = targetId, Reason = reason.Trim(), Status = ReportStatus.Open, CreatedAt = now };

    public void Resolve(bool actioned) => Status = actioned ? ReportStatus.Actioned : ReportStatus.Dismissed;
}

public enum RiskDecision
{
    Allow,
    Review,
    Block,
}

/// <summary>Output of a fraud/risk rule evaluation. Foundation only — not a production fraud system.</summary>
public sealed class FraudCheck : Entity
{
    private FraudCheck() { }

    public string SubjectType { get; private set; } = default!;
    public Guid SubjectId { get; private set; }
    public Guid? UserId { get; private set; }
    public string RuleCode { get; private set; } = default!;
    public int Score { get; private set; }
    public RiskDecision Decision { get; private set; }
    public string Details { get; private set; } = default!;
    public bool Acknowledged { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static FraudCheck Raise(string subjectType, Guid subjectId, Guid? userId, string ruleCode, int score, RiskDecision decision,
        string details, DateTimeOffset now) =>
        new()
        {
            SubjectType = subjectType,
            SubjectId = subjectId,
            UserId = userId,
            RuleCode = ruleCode,
            Score = score,
            Decision = decision,
            Details = details,
            CreatedAt = now,
        };

    public void Acknowledge() => Acknowledged = true;
}
