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

/// <summary>The provider's view of a host's connected account.</summary>
/// <param name="Requirement">What the host still has to provide, if anything (shown to them).</param>
public sealed record ConnectedAccountStatus(bool PayoutsEnabled, string? AccountHolder, string? BankLast4, string? Country, string? Requirement);

/// <summary>
/// Host payout port: the fake bank-transfer simulator locally, Stripe Connect in production. Providers with hosted
/// onboarding collect identity and bank details themselves; StaySphere then only stores the provider's account id.
/// </summary>
public interface IPayoutProvider
{
    /// <summary>True when hosts link an account through the provider instead of typing an IBAN into StaySphere.</summary>
    bool UsesHostedOnboarding { get; }

    /// <summary>Transfers money. Must be idempotent per <paramref name="payoutId"/>: retries never pay twice.</summary>
    Task<PayoutTransferResult> SendAsync(Guid payoutId, string destination, decimal amount, string currency, CancellationToken cancellationToken);
    Task<PayoutTransferResult?> GetStatusAsync(Guid payoutId, CancellationToken cancellationToken);

    /// <summary>Creates the host's account at the provider (idempotent per host) and returns its id.</summary>
    Task<string> CreateConnectedAccountAsync(Guid hostId, string email, string country, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This payout provider has no hosted onboarding.");
    /// <summary>A short-lived URL where the host completes onboarding on the provider's site.</summary>
    Task<string> CreateOnboardingLinkAsync(string providerAccountId, string returnUrl, string refreshUrl, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This payout provider has no hosted onboarding.");
    Task<ConnectedAccountStatus> GetConnectedAccountAsync(string providerAccountId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This payout provider has no hosted onboarding.");
}
