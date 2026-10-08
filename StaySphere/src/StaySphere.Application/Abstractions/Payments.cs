namespace StaySphere.Application.Abstractions;

public enum ChargeStatus
{
    Succeeded,
    Authorized,
    Declined,
    Pending,
}

public sealed record ChargeRequest(Guid PaymentId, decimal Amount, string Currency, string PaymentMethodToken, string IdempotencyKey);

public sealed record ChargeResult(ChargeStatus Status, string? ProviderPaymentId, string? FailureReason, string? CardLast4);

public sealed record ProviderRefundResult(bool Succeeded, string? ProviderRefundId, string? FailureReason);

/// <summary>
/// Payment gateway port. Implementations: LocalFakePaymentProvider (dev/test). Stripe/Adyen/PayPal adapters plug in here.
/// Charge calls are NOT retried automatically — they are not idempotent at the HTTP level; provider idempotency keys
/// plus reconciliation handle uncertainty instead.
/// </summary>
public interface IPaymentProvider
{
    string Name { get; }
    Task<ChargeResult> ChargeAsync(ChargeRequest request, CancellationToken cancellationToken);

    /// <summary>Reserves funds without capturing (request-to-book). Returns Authorized or Declined.</summary>
    Task<ChargeResult> AuthorizeAsync(ChargeRequest request, CancellationToken cancellationToken);
    Task<ChargeResult> CaptureAsync(string providerPaymentId, decimal amount, string idempotencyKey, CancellationToken cancellationToken);
    Task<bool> VoidAsync(string providerPaymentId, string idempotencyKey, CancellationToken cancellationToken);
    Task<ProviderRefundResult> RefundAsync(string providerPaymentId, decimal amount, string idempotencyKey, CancellationToken cancellationToken);
    Task<ChargeResult?> GetStatusAsync(Guid paymentId, CancellationToken cancellationToken);
    bool VerifyWebhookSignature(string payload, string signature, long timestamp, DateTimeOffset now);
}

public sealed record PayoutTransferResult(bool Succeeded, string? ProviderPayoutId, string? FailureReason);

/// <summary>Bank-transfer port for host payouts (fake locally; Stripe Connect / Adyen for Platforms in production).</summary>
public interface IPayoutProvider
{
    Task<PayoutTransferResult> SendAsync(Guid payoutId, string destination, decimal amount, string currency, CancellationToken cancellationToken);
    Task<PayoutTransferResult?> GetStatusAsync(Guid payoutId, CancellationToken cancellationToken);
}
