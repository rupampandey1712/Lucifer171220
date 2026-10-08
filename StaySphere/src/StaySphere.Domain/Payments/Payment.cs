using StaySphere.Domain.Common;

namespace StaySphere.Domain.Payments;

public enum PaymentStatus
{
    Pending,
    Succeeded,
    Failed,
    PartiallyRefunded,
    Refunded,
}

public enum PaymentTransactionKind
{
    Charge,
    Failure,
    Refund,
}

public sealed class Payment : AggregateRoot
{
    private readonly List<PaymentTransaction> _transactions = [];
    private readonly List<Refund> _refunds = [];

    private Payment() { }

    public Guid ReservationId { get; private set; }
    public Guid PayerId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = default!;
    public PaymentStatus Status { get; private set; }
    public string Provider { get; private set; } = default!;
    public string? ProviderPaymentId { get; private set; }
    public string? CardLast4 { get; private set; }
    public string? FailureReason { get; private set; }
    public string IdempotencyKey { get; private set; } = default!;
    public IReadOnlyCollection<PaymentTransaction> Transactions => _transactions;
    public IReadOnlyCollection<Refund> Refunds => _refunds;

    public decimal RefundedAmount => _refunds.Where(r => r.Status == RefundStatus.Succeeded).Sum(r => r.Amount);

    public static Payment Start(Guid reservationId, Guid payerId, decimal amount, string currency, string provider,
        string idempotencyKey, string? cardLast4, DateTimeOffset now) =>
        new()
        {
            ReservationId = reservationId,
            PayerId = payerId,
            Amount = amount,
            Currency = currency,
            Provider = provider,
            IdempotencyKey = idempotencyKey,
            CardLast4 = cardLast4,
            Status = PaymentStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now,
        };

    /// <summary>Idempotent: a second success notification (e.g. duplicate webhook) is a no-op.</summary>
    public bool MarkSucceeded(string providerPaymentId, DateTimeOffset now)
    {
        if (Status != PaymentStatus.Pending) return false;
        Status = PaymentStatus.Succeeded;
        ProviderPaymentId = providerPaymentId;
        _transactions.Add(new PaymentTransaction(Id, PaymentTransactionKind.Charge, Amount, providerPaymentId, now));
        Raise(new PaymentSucceededDomainEvent(Id, ReservationId, PayerId, Amount, Currency));
        return true;
    }

    public bool MarkFailed(string reason, string? providerRef, DateTimeOffset now)
    {
        if (Status != PaymentStatus.Pending) return false;
        Status = PaymentStatus.Failed;
        FailureReason = reason;
        ProviderPaymentId ??= providerRef;
        _transactions.Add(new PaymentTransaction(Id, PaymentTransactionKind.Failure, 0, providerRef, now));
        Raise(new PaymentFailedDomainEvent(Id, ReservationId, PayerId, reason));
        return true;
    }

    public Result<Refund> RequestRefund(decimal amount, string reason, DateTimeOffset now)
    {
        if (Status is not (PaymentStatus.Succeeded or PaymentStatus.PartiallyRefunded))
            return Error.Conflict("payment.not_refundable", "Only successful payments can be refunded.");
        if (amount <= 0 || amount > Amount - RefundedAmount - _refunds.Where(r => r.Status == RefundStatus.Pending).Sum(r => r.Amount))
            return Error.Validation("payment.refund_amount", "Refund amount exceeds the refundable balance.");
        var refund = new Refund(Id, amount, reason, now);
        _refunds.Add(refund);
        return refund;
    }

    public void CompleteRefund(Guid refundId, string providerRefundId, DateTimeOffset now)
    {
        var refund = _refunds.Single(r => r.Id == refundId);
        if (!refund.Complete(providerRefundId, now)) return;
        _transactions.Add(new PaymentTransaction(Id, PaymentTransactionKind.Refund, -refund.Amount, providerRefundId, now));
        Status = RefundedAmount >= Amount ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        Raise(new RefundCompletedDomainEvent(Id, ReservationId, PayerId, refund.Amount, Currency));
    }
}

public sealed class PaymentTransaction : Entity
{
    private PaymentTransaction() { }

    internal PaymentTransaction(Guid paymentId, PaymentTransactionKind kind, decimal amount, string? providerRef, DateTimeOffset at)
    {
        PaymentId = paymentId;
        Kind = kind;
        Amount = amount;
        ProviderReference = providerRef;
        At = at;
    }

    public Guid PaymentId { get; private set; }
    public PaymentTransactionKind Kind { get; private set; }
    public decimal Amount { get; private set; }
    public string? ProviderReference { get; private set; }
    public DateTimeOffset At { get; private set; }
}

public enum RefundStatus
{
    Pending,
    Succeeded,
    Failed,
}

public sealed class Refund : Entity
{
    private Refund() { }

    internal Refund(Guid paymentId, decimal amount, string reason, DateTimeOffset at)
    {
        PaymentId = paymentId;
        Amount = amount;
        Reason = reason;
        RequestedAt = at;
        Status = RefundStatus.Pending;
    }

    public Guid PaymentId { get; private set; }
    public decimal Amount { get; private set; }
    public string Reason { get; private set; } = default!;
    public RefundStatus Status { get; private set; }
    public string? ProviderRefundId { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    internal bool Complete(string providerRefundId, DateTimeOffset now)
    {
        if (Status != RefundStatus.Pending) return false;
        Status = RefundStatus.Succeeded;
        ProviderRefundId = providerRefundId;
        CompletedAt = now;
        return true;
    }
}

public enum LedgerAccount
{
    GuestPayment,
    PlatformFee,
    TaxesPayable,
    HostEarning,
    Refund,
    Adjustment,
    Payout,
}

/// <summary>
/// Immutable double-entry style ledger line. Never updated or deleted; corrections are compensating entries.
/// Positive = credit to the account, negative = debit.
/// </summary>
public sealed class LedgerEntry : Entity
{
    private LedgerEntry() { }

    public Guid ReservationId { get; private set; }
    public Guid HostId { get; private set; }
    public LedgerAccount Account { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = default!;
    public DateTimeOffset OccurredAt { get; private set; }
    public string Description { get; private set; } = default!;

    public static LedgerEntry Create(Guid reservationId, Guid hostId, LedgerAccount account, decimal amount, string currency,
        DateTimeOffset at, string description) =>
        new()
        {
            ReservationId = reservationId,
            HostId = hostId,
            Account = account,
            Amount = decimal.Round(amount, 2),
            Currency = currency,
            OccurredAt = at,
            Description = description,
        };
}

/// <summary>Persisted provider webhook; the unique (Provider, EventId) index gives replay protection.</summary>
public sealed class WebhookEvent : Entity
{
    private WebhookEvent() { }

    public string Provider { get; private set; } = default!;
    public string EventId { get; private set; } = default!;
    public string Type { get; private set; } = default!;
    public string Payload { get; private set; } = default!;
    public DateTimeOffset ReceivedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }

    public static WebhookEvent Receive(string provider, string eventId, string type, string payload, DateTimeOffset now) =>
        new() { Provider = provider, EventId = eventId, Type = type, Payload = payload, ReceivedAt = now };

    public void MarkProcessed(DateTimeOffset now) => ProcessedAt = now;
}

public sealed record PaymentSucceededDomainEvent(Guid PaymentId, Guid ReservationId, Guid PayerId, decimal Amount, string Currency) : IDomainEvent;
public sealed record PaymentFailedDomainEvent(Guid PaymentId, Guid ReservationId, Guid PayerId, string Reason) : IDomainEvent;
public sealed record RefundCompletedDomainEvent(Guid PaymentId, Guid ReservationId, Guid PayerId, decimal Amount, string Currency) : IDomainEvent;
