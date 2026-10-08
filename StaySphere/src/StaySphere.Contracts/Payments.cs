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

public sealed record PayoutAccountRequest(string AccountHolder, string Iban, string Country);

public sealed record PayoutAccountDto(string AccountHolder, string MaskedAccount, string Country);

public sealed record CurrencyBalanceDto(string Currency, decimal Available, decimal Pending, decimal PaidOut);

public sealed record PayoutDto(Guid Id, Guid HostId, string? HostName, decimal Amount, string Currency, string Status, string Destination,
    bool Automatic, string? FailureReason, DateTimeOffset CreatedAt, DateTimeOffset? PaidAt, int ReservationCount);

public sealed record PayoutSummaryDto(PayoutAccountDto? Account, IReadOnlyList<CurrencyBalanceDto> Balances, IReadOnlyList<PayoutDto> Payouts,
    string Schedule);
