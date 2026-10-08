namespace StaySphere.Contracts.Payments;

/// <summary>A tokenised card from the (fake) provider's client SDK, e.g. "tok_4242". Raw card numbers are never accepted.</summary>
public sealed record PayReservationRequest(string PaymentMethodToken);

public sealed record PaymentDto(Guid Id, Guid ReservationId, decimal Amount, string Currency, string Status, string Provider,
    string? CardLast4, string? FailureReason, decimal RefundedAmount, DateTimeOffset CreatedAt, string ReservationStatus);

public sealed record RefundRequest(decimal Amount, string Reason);

public sealed record EarningsSummaryDto(string Currency, decimal Gross, decimal PlatformFees, decimal Refunds, decimal Net,
    IReadOnlyList<LedgerLineDto> Lines);

public sealed record LedgerLineDto(Guid ReservationId, string Account, decimal Amount, string Currency, DateTimeOffset OccurredAt, string Description);

public sealed record FakeWebhookPayload(string EventId, string Type, string PaymentIntentId, Guid PaymentId, decimal Amount, string Currency, string? FailureReason);
