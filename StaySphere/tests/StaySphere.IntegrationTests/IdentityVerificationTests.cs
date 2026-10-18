using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StaySphere.Application.Abstractions;
using StaySphere.Application.Identity;
using StaySphere.Contracts.Auth;
using StaySphere.Contracts.Properties;
using StaySphere.Infrastructure.IdentityVerification;
using StaySphere.Infrastructure.Persistence;
using StaySphere.IntegrationTests.Infrastructure;

namespace StaySphere.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class IdentityVerificationTests(ApiFactory api)
{
    // ---------- Stripe Identity adapter ----------

    private sealed class StubStripe(Func<HttpRequestMessage, object> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add((request, request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(respond(request)), Encoding.UTF8, "application/json") };
        }
    }

    private static StripeIdentityProvider Stripe(StubStripe stub)
    {
        var options = new StripeIdentityOptions { SecretKey = "sk_test_1", BaseUrl = "https://stripe.test/" };
        var http = new HttpClient(stub);
        StripeIdentityProvider.Configure(http, options);
        return new StripeIdentityProvider(http, options);
    }

    [Fact]
    public async Task Stripe_sessions_are_document_checks_with_a_selfie_and_a_return_url()
    {
        var stub = new StubStripe(_ => new { id = "vs_123", url = "https://verify.stripe.com/start/abc" });
        var userId = Guid.NewGuid();

        var (id, url) = await Stripe(stub).CreateSessionAsync(userId, "http://localhost:5173/host/verification?identity=return", CancellationToken.None);

        (id, url).ShouldBe(("vs_123", "https://verify.stripe.com/start/abc"));
        var body = Uri.UnescapeDataString(stub.Calls[0].Body).Replace('+', ' ');
        stub.Calls[0].Request.RequestUri!.AbsolutePath.ShouldBe("/v1/identity/verification_sessions");
        body.ShouldContain("type=document");
        body.ShouldContain("options[document][require_matching_selfie]=true");
        body.ShouldContain($"metadata[user_id]={userId}");
        body.ShouldContain("return_url=http://localhost:5173/host/verification?identity=return");
    }

    [Theory]
    [InlineData("verified", null, IdentitySessionStatus.Verified)]
    [InlineData("processing", null, IdentitySessionStatus.Pending)]
    [InlineData("requires_input", null, IdentitySessionStatus.Pending)]
    [InlineData("requires_input", "The document is expired.", IdentitySessionStatus.Failed)]
    [InlineData("canceled", null, IdentitySessionStatus.Failed)]
    public async Task Stripe_session_states_map_to_outcomes(string status, string? error, IdentitySessionStatus expected)
    {
        var stub = new StubStripe(_ => new
        {
            id = "vs_1", status,
            last_error = error is null ? null : new { code = "document_expired", reason = error },
            verified_outputs = status == "verified" ? new { first_name = "Ana", last_name = "Silva", dob = new { year = 1990 } } : null,
        });

        var result = await Stripe(stub).GetSessionAsync("vs_1", CancellationToken.None);

        result.Status.ShouldBe(expected);
        if (expected == IdentitySessionStatus.Verified) result.VerifiedName.ShouldBe("Ana Silva");
        if (error is not null) result.FailureReason.ShouldBe(error);
        stub.Calls[0].Request.RequestUri!.Query.ShouldContain("expand[]=verified_outputs");
    }

    // ---------- Flow through the API ----------

    private sealed class ScriptedIdentity : IIdentityVerificationProvider
    {
        public ConcurrentDictionary<string, IdentitySessionResult> Results { get; } = new();
        public bool Enabled => true;

        public Task<(string SessionId, string Url)> CreateSessionAsync(Guid userId, string returnUrl, CancellationToken ct)
        {
            var id = "vs_" + Guid.NewGuid().ToString("N")[..12];
            Results[id] = new IdentitySessionResult(id, IdentitySessionStatus.Pending, null, null);
            return Task.FromResult((id, $"https://verify.stripe.test/{id}"));
        }

        public Task<IdentitySessionResult> GetSessionAsync(string sessionId, CancellationToken ct) => Task.FromResult(Results[sessionId]);
    }

    private async Task<(HttpClient Client, Guid HostId, Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> App)> HostAsync(ScriptedIdentity identity)
    {
        var app = api.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IIdentityVerificationProvider>(identity)));
        var (main, auth) = await api.RegisterHostAsync();
        await api.WithScopeAsync(sp => sp.GetRequiredService<AppDbContext>().Database
            .ExecuteSqlInterpolatedAsync($"UPDATE [identity].[Users] SET EmailConfirmed = 1 WHERE Id = {auth.User.Id}"));
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = main.DefaultRequestHeaders.Authorization;
        return (client, auth.User.Id, app);
    }

    private static string SessionOf(ScriptedIdentity identity, string url) => identity.Results.Keys.Single(k => url.EndsWith(k, StringComparison.Ordinal));

    [Fact]
    public async Task A_verified_document_verifies_the_host_without_an_admin()
    {
        var identity = new ScriptedIdentity();
        var (host, hostId, app) = await HostAsync(identity);
        using var _ = app;
        var propertyId = await api.CreatePublishedPropertyAsync(hostId);

        (await (await host.GetAsync("/api/v1/host/verification")).ReadAsync<HostVerificationDto>()).DocumentCheckAvailable.ShouldBeTrue();
        var check = await (await host.PostJsonAsync("/api/v1/host/verification/identity", new { })).ReadAsync<IdentityCheckDto>();
        check.Url.ShouldStartWith("https://verify.stripe.test/vs_");
        var session = SessionOf(identity, check.Url);

        var pending = await (await host.PostJsonAsync("/api/v1/host/verification/identity/refresh", new { })).ReadAsync<HostVerificationDto>();
        pending.ShouldSatisfyAllConditions(p => p.Status.ShouldBe("Pending"), p => p.Method.ShouldBe("Document"));

        identity.Results[session] = new IdentitySessionResult(session, IdentitySessionStatus.Verified, "Ana Silva", null);
        var verified = await (await host.PostJsonAsync("/api/v1/host/verification/identity/refresh", new { })).ReadAsync<HostVerificationDto>();
        verified.Status.ShouldBe("Verified");
        verified.LegalName.ShouldBe("Ana Silva");

        (await (await api.CreateClient().GetAsync($"/api/v1/properties/{propertyId}")).ReadAsync<PropertyDetailDto>()).Host.Verified.ShouldBeTrue();
        var notified = await api.WithScopeAsync(sp => sp.GetRequiredService<AppDbContext>().Notifications.AnyAsync(n => n.UserId == hostId && n.Type == "host.verified"));
        notified.ShouldBeTrue();
    }

    [Fact]
    public async Task A_rejected_document_says_why_and_the_host_can_retry()
    {
        var identity = new ScriptedIdentity();
        var (host, _, app) = await HostAsync(identity);
        using var _ = app;
        var first = SessionOf(identity, (await (await host.PostJsonAsync("/api/v1/host/verification/identity", new { })).ReadAsync<IdentityCheckDto>()).Url);
        identity.Results[first] = new IdentitySessionResult(first, IdentitySessionStatus.Failed, null, "The document is expired.");

        var rejected = await (await host.PostJsonAsync("/api/v1/host/verification/identity/refresh", new { })).ReadAsync<HostVerificationDto>();
        rejected.ShouldSatisfyAllConditions(r => r.Status.ShouldBe("Rejected"), r => r.Note.ShouldBe("The document is expired."));

        var second = SessionOf(identity, (await (await host.PostJsonAsync("/api/v1/host/verification/identity", new { })).ReadAsync<IdentityCheckDto>()).Url);
        identity.Results[first] = new IdentitySessionResult(first, IdentitySessionStatus.Verified, "Old", null); // a stale session can't verify
        (await (await host.PostJsonAsync("/api/v1/host/verification/identity/refresh", new { })).ReadAsync<HostVerificationDto>()).Status.ShouldBe("Pending");
        identity.Results[second] = new IdentitySessionResult(second, IdentitySessionStatus.Verified, "Ana Silva", null);
        (await (await host.PostJsonAsync("/api/v1/host/verification/identity/refresh", new { })).ReadAsync<HostVerificationDto>()).Status.ShouldBe("Verified");
    }

    [Fact]
    public async Task The_job_settles_checks_for_hosts_who_never_come_back_and_expires_abandoned_ones()
    {
        var identity = new ScriptedIdentity();
        var (verifiedHost, verifiedId, app) = await HostAsync(identity);
        using var _ = app;
        var (abandonedHost, abandonedId, app2) = await HostAsync(identity);
        using var __ = app2;
        var done = SessionOf(identity, (await (await verifiedHost.PostJsonAsync("/api/v1/host/verification/identity", new { })).ReadAsync<IdentityCheckDto>()).Url);
        await abandonedHost.PostJsonAsync("/api/v1/host/verification/identity", new { });
        identity.Results[done] = new IdentitySessionResult(done, IdentitySessionStatus.Verified, null, null);
        await api.WithScopeAsync(sp => sp.GetRequiredService<AppDbContext>().Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [identity].[Users] SET HostVerificationSubmittedAt = {DateTimeOffset.UtcNow.AddDays(-3)} WHERE Id = {abandonedId}"));

        using (var scope = app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IHostVerificationService>().ReconcileDocumentChecksAsync(CancellationToken.None);

        var statuses = await api.WithScopeAsync(sp => sp.GetRequiredService<AppDbContext>().Users.AsNoTracking()
            .Where(u => u.Id == verifiedId || u.Id == abandonedId).ToDictionaryAsync(u => u.Id, u => u.HostVerification.ToString()));
        statuses[verifiedId].ShouldBe("Verified");
        statuses[abandonedId].ShouldBe("Rejected", "abandoned for more than 2 days");
    }

    [Fact]
    public async Task Without_a_provider_document_checks_are_unavailable()
    {
        var (host, _) = await api.RegisterHostAsync();
        (await (await host.GetAsync("/api/v1/host/verification")).ReadAsync<HostVerificationDto>()).DocumentCheckAvailable.ShouldBeFalse();
        (await host.PostJsonAsync("/api/v1/host/verification/identity", new { })).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }
}
