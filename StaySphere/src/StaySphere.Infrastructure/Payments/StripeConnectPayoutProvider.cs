using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StaySphere.Application.Abstractions;

namespace StaySphere.Infrastructure.Payments;

public sealed class PayoutOptions
{
    public const string Section = "Payouts";
    /// <summary>Fake (default: local simulator) or StripeConnect.</summary>
    public string Provider { get; set; } = "Fake";
    public StripeConnectOptions Stripe { get; set; } = new();
}

public sealed class StripeConnectOptions
{
    /// <summary>Platform secret key (<c>sk_test_…</c> in test mode). From Key Vault / user secrets, never appsettings.</summary>
    public string SecretKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.stripe.com/";
    /// <summary>Pinned so a Stripe-side default change can't alter response shapes under us.</summary>
    public string ApiVersion { get; set; } = "2024-06-20";
}

/// <summary>
/// Real payout rails through Stripe Connect (Express accounts). Hosts onboard on Stripe (identity checks and bank account),
/// StaySphere stores only the <c>acct_…</c> id; a payout is a Transfer from the platform balance to that account, and Stripe
/// pays it out to the host's bank on the account's schedule. Every write carries an Idempotency-Key, so a retried payout
/// (crash, timeout, reconciliation) can never transfer twice.
/// </summary>
public sealed class StripeConnectPayoutProvider(HttpClient http, ILogger<StripeConnectPayoutProvider> logger) : IPayoutProvider
{
    // ISO 4217 currencies Stripe treats as zero-decimal (amounts are sent in whole units).
    private static readonly HashSet<string> ZeroDecimal = new(StringComparer.OrdinalIgnoreCase)
        { "BIF", "CLP", "DJF", "GNF", "JPY", "KMF", "KRW", "MGA", "PYG", "RWF", "UGX", "VND", "VUV", "XAF", "XOF", "XPF" };

    public bool UsesHostedOnboarding => true;

    public static long ToMinorUnits(decimal amount, string currency) =>
        (long)decimal.Round(ZeroDecimal.Contains(currency) ? amount : amount * 100m, 0, MidpointRounding.AwayFromZero);

    private static string TransferGroup(Guid payoutId) => $"payout_{payoutId:N}";

    public async Task<PayoutTransferResult> SendAsync(Guid payoutId, string destination, decimal amount, string currency, CancellationToken cancellationToken)
    {
        if (!destination.StartsWith("acct_", StringComparison.Ordinal))
            return new PayoutTransferResult(false, null, "The host has no Stripe account linked.");

        using var response = await PostAsync("v1/transfers", $"payout-{payoutId:N}", cancellationToken,
            ("amount", ToMinorUnits(amount, currency).ToString(CultureInfo.InvariantCulture)),
            ("currency", currency.ToLowerInvariant()),
            ("destination", destination),
            ("transfer_group", TransferGroup(payoutId)),
            ("metadata[payout_id]", payoutId.ToString()));
        var json = await ReadAsync(response, cancellationToken);
        if (response.IsSuccessStatusCode) return new PayoutTransferResult(true, json.RootElement.GetProperty("id").GetString(), null);

        // 4xx other than auth/rate limiting is a definite "no" (insufficient platform balance, account not payable, …):
        // the payout fails and its ledger entries are reversed. Anything else is thrown and retried by reconciliation.
        var message = Message(json);
        logger.LogWarning("Stripe rejected transfer for payout {PayoutId}: {Status} {Message}", payoutId, (int)response.StatusCode, message);
        return new PayoutTransferResult(false, null, message);
    }

    public async Task<PayoutTransferResult?> GetStatusAsync(Guid payoutId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, $"v1/transfers?transfer_group={TransferGroup(payoutId)}&limit=1"), cancellationToken);
        var json = await ReadAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Stripe transfer lookup failed: {Message(json)}", null, response.StatusCode);
        var data = json.RootElement.GetProperty("data");
        if (data.GetArrayLength() == 0) return null;
        var transfer = data[0];
        return transfer.TryGetProperty("reversed", out var reversed) && reversed.GetBoolean()
            ? new PayoutTransferResult(false, transfer.GetProperty("id").GetString(), "The transfer was reversed.")
            : new PayoutTransferResult(true, transfer.GetProperty("id").GetString(), null);
    }

    public async Task<string> CreateConnectedAccountAsync(Guid hostId, string email, string country, CancellationToken cancellationToken)
    {
        using var response = await PostAsync("v1/accounts", $"account-{hostId:N}", cancellationToken,
            ("type", "express"),
            ("country", country.ToUpperInvariant()),
            ("email", email),
            ("capabilities[transfers][requested]", "true"),
            ("business_type", "individual"),
            ("metadata[host_id]", hostId.ToString()));
        var json = await ReadAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Stripe account creation failed: {Message(json)}", null, response.StatusCode);
        return json.RootElement.GetProperty("id").GetString()!;
    }

    public async Task<string> CreateOnboardingLinkAsync(string providerAccountId, string returnUrl, string refreshUrl, CancellationToken cancellationToken)
    {
        // Account links are single-use and short-lived, so no idempotency key: each click gets a fresh one.
        using var response = await PostAsync("v1/account_links", null, cancellationToken,
            ("account", providerAccountId), ("return_url", returnUrl), ("refresh_url", refreshUrl), ("type", "account_onboarding"));
        var json = await ReadAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Stripe onboarding link failed: {Message(json)}", null, response.StatusCode);
        return json.RootElement.GetProperty("url").GetString()!;
    }

    public async Task<ConnectedAccountStatus> GetConnectedAccountAsync(string providerAccountId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, $"v1/accounts/{Uri.EscapeDataString(providerAccountId)}"), cancellationToken);
        var json = await ReadAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Stripe account lookup failed: {Message(json)}", null, response.StatusCode);
        var a = json.RootElement;

        string? holder = null, last4 = null, requirement = null;
        if (a.TryGetProperty("external_accounts", out var ext) && ext.TryGetProperty("data", out var banks) && banks.GetArrayLength() > 0)
        {
            var bank = banks[0];
            last4 = bank.TryGetProperty("last4", out var l) ? l.GetString() : null;
            holder = bank.TryGetProperty("account_holder_name", out var h) && h.ValueKind == JsonValueKind.String ? h.GetString() : null;
        }
        if (a.TryGetProperty("requirements", out var req) && req.TryGetProperty("currently_due", out var due) && due.GetArrayLength() > 0)
            requirement = "Stripe needs more information to enable payouts. Continue setup to finish.";
        if (a.TryGetProperty("requirements", out req) && req.TryGetProperty("disabled_reason", out var reason) && reason.ValueKind == JsonValueKind.String)
            requirement ??= $"Payouts are paused by Stripe ({reason.GetString()}).";

        return new ConnectedAccountStatus(a.TryGetProperty("payouts_enabled", out var enabled) && enabled.GetBoolean(), holder, last4,
            a.TryGetProperty("country", out var c) ? c.GetString() : null, requirement);
    }

    private Task<HttpResponseMessage> PostAsync(string path, string? idempotencyKey, CancellationToken ct, params (string Key, string Value)[] form)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new FormUrlEncodedContent(form.Select(f => new KeyValuePair<string, string>(f.Key, f.Value))),
        };
        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
        return SendAsync(request, ct);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using (request)
        {
            var response = await http.SendAsync(request, ct);
            // Auth, rate limiting and server errors are not answers about the payout: throw so the caller retries later.
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                response.Dispose();
                throw new HttpRequestException($"Stripe returned {(int)response.StatusCode}: {body[..Math.Min(body.Length, 300)]}", null, response.StatusCode);
            }
            return response;
        }
    }

    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response, CancellationToken ct) =>
        await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

    private static string Message(JsonDocument json) =>
        json.RootElement.TryGetProperty("error", out var e) && e.TryGetProperty("message", out var m) ? m.GetString() ?? "Unknown error" : "Unknown error";

    /// <summary>Applies auth and version headers to the typed client.</summary>
    public static void Configure(HttpClient client, StripeConnectOptions options)
    {
        client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.SecretKey);
        client.DefaultRequestHeaders.Add("Stripe-Version", options.ApiVersion);
        client.Timeout = TimeSpan.FromSeconds(30);
    }
}
