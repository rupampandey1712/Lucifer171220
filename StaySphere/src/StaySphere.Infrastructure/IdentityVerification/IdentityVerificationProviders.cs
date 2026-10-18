using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using StaySphere.Application.Abstractions;

namespace StaySphere.Infrastructure.IdentityVerification;

public sealed class IdentityVerificationOptions
{
    public const string Section = "IdentityVerification";
    /// <summary>None (admin review only, the default), Simulated (local demo: every check passes) or StripeIdentity.</summary>
    public string Provider { get; set; } = "None";
    public StripeIdentityOptions Stripe { get; set; } = new();
}

public sealed class StripeIdentityOptions
{
    public string SecretKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.stripe.com/";
    public string ApiVersion { get; set; } = "2024-06-20";
    /// <summary>Also match a live selfie against the document photo.</summary>
    public bool RequireMatchingSelfie { get; set; } = true;
}

/// <summary>No identity provider: hosts are verified by admin review only.</summary>
public sealed class DisabledIdentityVerificationProvider : IIdentityVerificationProvider
{
    public bool Enabled => false;
    public Task<(string SessionId, string Url)> CreateSessionAsync(Guid userId, string returnUrl, CancellationToken cancellationToken) =>
        throw new NotSupportedException("No identity verification provider is configured.");
    public Task<IdentitySessionResult> GetSessionAsync(string sessionId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("No identity verification provider is configured.");
}

/// <summary>Local demo: "capturing" the document just returns to the site, and every check passes.</summary>
public sealed class SimulatedIdentityVerificationProvider : IIdentityVerificationProvider
{
    private static readonly ConcurrentDictionary<string, Guid> Sessions = new();

    public bool Enabled => true;

    public Task<(string SessionId, string Url)> CreateSessionAsync(Guid userId, string returnUrl, CancellationToken cancellationToken)
    {
        var id = "vs_sim_" + Guid.NewGuid().ToString("N")[..20];
        Sessions[id] = userId;
        return Task.FromResult((id, returnUrl));
    }

    public Task<IdentitySessionResult> GetSessionAsync(string sessionId, CancellationToken cancellationToken) =>
        Task.FromResult(Sessions.ContainsKey(sessionId)
            ? new IdentitySessionResult(sessionId, IdentitySessionStatus.Verified, null, null)
            : new IdentitySessionResult(sessionId, IdentitySessionStatus.Failed, null, "Unknown session (the API was restarted)."));
}

/// <summary>
/// Stripe Identity: a hosted flow captures a government ID (and optionally a selfie), Stripe verifies it and keeps the
/// images. We create a VerificationSession, send the host to its URL, and read back only the status and the verified name.
/// </summary>
public sealed class StripeIdentityProvider(HttpClient http, StripeIdentityOptions options) : IIdentityVerificationProvider
{
    public bool Enabled => true;

    public async Task<(string SessionId, string Url)> CreateSessionAsync(Guid userId, string returnUrl, CancellationToken cancellationToken)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("type", "document"),
            new("return_url", returnUrl),
            new("metadata[user_id]", userId.ToString()),
            new("options[document][require_matching_selfie]", options.RequireMatchingSelfie ? "true" : "false"),
            new("options[document][allowed_types][]", "passport"),
            new("options[document][allowed_types][]", "id_card"),
            new("options[document][allowed_types][]", "driving_license"),
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/identity/verification_sessions") { Content = new FormUrlEncodedContent(form) };
        using var response = await http.SendAsync(request, cancellationToken);
        using var json = await ReadAsync(response, cancellationToken);
        return (json.RootElement.GetProperty("id").GetString()!, json.RootElement.GetProperty("url").GetString()!);
    }

    public async Task<IdentitySessionResult> GetSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        // verified_outputs is only returned when expanded; we read the name and nothing else from it.
        using var response = await http.GetAsync($"v1/identity/verification_sessions/{Uri.EscapeDataString(sessionId)}?expand[]=verified_outputs", cancellationToken);
        using var json = await ReadAsync(response, cancellationToken);
        var s = json.RootElement;
        var status = s.GetProperty("status").GetString();
        string? reason = s.TryGetProperty("last_error", out var error) && error.ValueKind == JsonValueKind.Object && error.TryGetProperty("reason", out var r)
            ? r.GetString() : null;

        return status switch
        {
            "verified" => new IdentitySessionResult(sessionId, IdentitySessionStatus.Verified, VerifiedName(s), null),
            "canceled" => new IdentitySessionResult(sessionId, IdentitySessionStatus.Failed, null, "The ID check was cancelled."),
            // requires_input with an error = the document was rejected (expired, unreadable, selfie mismatch, …).
            "requires_input" when reason is not null => new IdentitySessionResult(sessionId, IdentitySessionStatus.Failed, null, reason),
            _ => new IdentitySessionResult(sessionId, IdentitySessionStatus.Pending, null, null), // requires_input (not started), processing
        };
    }

    private static string? VerifiedName(JsonElement session)
    {
        if (!session.TryGetProperty("verified_outputs", out var o) || o.ValueKind != JsonValueKind.Object) return null;
        var first = o.TryGetProperty("first_name", out var f) ? f.GetString() : null;
        var last = o.TryGetProperty("last_name", out var l) ? l.GetString() : null;
        var name = $"{first} {last}".Trim();
        return name.Length == 0 ? null : name;
    }

    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (response.IsSuccessStatusCode) return json;
        var message = json.RootElement.TryGetProperty("error", out var e) && e.TryGetProperty("message", out var m) ? m.GetString() : null;
        json.Dispose();
        throw new HttpRequestException($"Stripe Identity returned {(int)response.StatusCode}: {message}", null, response.StatusCode);
    }

    public static void Configure(HttpClient client, StripeIdentityOptions options)
    {
        client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.SecretKey);
        client.DefaultRequestHeaders.Add("Stripe-Version", options.ApiVersion);
        client.Timeout = TimeSpan.FromSeconds(30);
    }
}
