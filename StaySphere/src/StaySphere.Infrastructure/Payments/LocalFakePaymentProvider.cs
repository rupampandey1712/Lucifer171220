using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StaySphere.Application.Abstractions;
using StaySphere.Application.Common;
using StaySphere.Contracts.Payments;

namespace StaySphere.Infrastructure.Payments;

public sealed class FakePaymentOptions
{
    public const string Section = "Payments:Fake";
    public string WebhookSecret { get; set; } = string.Empty;
    /// <summary>Where the simulator delivers webhooks (the API's own webhook endpoint).</summary>
    public string? WebhookUrl { get; set; }
    public int DelayedWebhookSeconds { get; set; } = 5;
}

/// <summary>
/// Development payment gateway. Scenario is chosen by test token — no real money, no card numbers.
/// <list type="bullet">
/// <item>tok_4242 / tok_visa — success</item>
/// <item>tok_0002 / tok_declined — card declined</item>
/// <item>tok_9995 — insufficient funds</item>
/// <item>tok_0119 / tok_timeout — no immediate answer; success arrives by webhook later</item>
/// <item>tok_0259 — success, and the webhook is delivered twice (exercise idempotency)</item>
/// </list>
/// Repeated calls with the same idempotency key return the original result, like real gateways.
/// </summary>
public sealed class LocalFakePaymentProvider(
    IOptions<FakePaymentOptions> options,
    IHttpClientFactory httpClientFactory,
    TimeProvider clock,
    ILogger<LocalFakePaymentProvider> logger) : IPaymentProvider
{
    public const string ProviderName = "fake";
    private static readonly ConcurrentDictionary<string, ChargeResult> ByIdempotencyKey = new();
    private static readonly ConcurrentDictionary<Guid, ChargeResult> ByPaymentId = new();
    private static readonly ConcurrentDictionary<string, ProviderRefundResult> Refunds = new();

    public string Name => ProviderName;

    public Task<ChargeResult> ChargeAsync(ChargeRequest request, CancellationToken cancellationToken)
    {
        var result = ByIdempotencyKey.GetOrAdd(request.IdempotencyKey, _ =>
        {
            var intentId = "pi_" + Guid.NewGuid().ToString("N")[..20];
            var token = request.PaymentMethodToken.ToLowerInvariant();
            var outcome = token switch
            {
                "tok_0002" or "tok_declined" => new ChargeResult(ChargeStatus.Declined, intentId, "Your card was declined.", "0002"),
                "tok_9995" => new ChargeResult(ChargeStatus.Declined, intentId, "Insufficient funds.", "9995"),
                "tok_0119" or "tok_timeout" => new ChargeResult(ChargeStatus.Pending, intentId, null, "0119"),
                "tok_0259" => new ChargeResult(ChargeStatus.Succeeded, intentId, null, "0259"),
                _ when token.StartsWith("tok_", StringComparison.Ordinal) => new ChargeResult(ChargeStatus.Succeeded, intentId, null, "4242"),
                _ => new ChargeResult(ChargeStatus.Declined, intentId, "Invalid payment method.", null),
            };

            if (token is "tok_0119" or "tok_timeout")
            {
                ByPaymentId[request.PaymentId] = outcome with { Status = ChargeStatus.Succeeded };
                ScheduleWebhook(request, intentId, "payment.succeeded", null, TimeSpan.FromSeconds(options.Value.DelayedWebhookSeconds), 1);
            }
            else
            {
                ByPaymentId[request.PaymentId] = outcome;
                ScheduleWebhook(request, intentId, outcome.Status == ChargeStatus.Succeeded ? "payment.succeeded" : "payment.failed",
                    outcome.FailureReason, TimeSpan.FromMilliseconds(500), token == "tok_0259" ? 2 : 1);
            }

            return outcome;
        });

        return Task.FromResult(result);
    }

    public Task<ProviderRefundResult> RefundAsync(string providerPaymentId, decimal amount, string idempotencyKey, CancellationToken cancellationToken) =>
        Task.FromResult(Refunds.GetOrAdd(idempotencyKey, _ => new ProviderRefundResult(true, "re_" + Guid.NewGuid().ToString("N")[..20], null)));

    public Task<ChargeResult?> GetStatusAsync(Guid paymentId, CancellationToken cancellationToken) =>
        Task.FromResult(ByPaymentId.TryGetValue(paymentId, out var r) ? r : null);

    public bool VerifyWebhookSignature(string payload, string signature, long timestamp, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(options.Value.WebhookSecret) || string.IsNullOrEmpty(signature)) return false;
        // Replay window: reject anything older/newer than 5 minutes.
        if (Math.Abs(now.ToUnixTimeSeconds() - timestamp) > 300) return false;
        var expected = Sign(options.Value.WebhookSecret, payload, timestamp);
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(signature));
    }

    public static string Sign(string secret, string payload, long timestamp) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(timestamp.ToString(CultureInfo.InvariantCulture) + "." + payload))).ToLowerInvariant();

    private void ScheduleWebhook(ChargeRequest request, string intentId, string type, string? failure, TimeSpan delay, int deliveries)
    {
        var url = options.Value.WebhookUrl;
        if (string.IsNullOrWhiteSpace(url)) return;
        var payload = JsonSerializer.Serialize(new FakeWebhookPayload("evt_" + Guid.NewGuid().ToString("N")[..20], type, intentId, request.PaymentId,
            request.Amount, request.Currency, failure), Json.Options);

        _ = Task.Run(async () =>
        {
            await Task.Delay(delay);
            for (var i = 0; i < deliveries; i++)
            {
                try
                {
                    var ts = clock.GetUtcNow().ToUnixTimeSeconds();
                    using var message = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
                    message.Headers.Add("X-Fake-Timestamp", ts.ToString(CultureInfo.InvariantCulture));
                    message.Headers.Add("X-Fake-Signature", Sign(options.Value.WebhookSecret, payload, ts));
                    using var response = await httpClientFactory.CreateClient("fake-payments-webhook").SendAsync(message);
                    logger.LogInformation("Fake provider delivered webhook {Type} ({Status})", type, (int)response.StatusCode);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Fake provider webhook delivery failed (reconciliation will catch up)");
                }
            }
        });
    }
}
