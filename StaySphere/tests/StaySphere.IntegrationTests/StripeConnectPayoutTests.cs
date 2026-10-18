using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using StaySphere.Application.Abstractions;
using StaySphere.Contracts.Booking;
using StaySphere.Contracts.Payments;
using StaySphere.Domain.Payments;
using StaySphere.Infrastructure.Payments;
using StaySphere.Infrastructure.Persistence;
using StaySphere.IntegrationTests.Infrastructure;

namespace StaySphere.IntegrationTests;

/// <summary>The Stripe Connect adapter against a stub Stripe, and the hosted-onboarding flow through the real API.</summary>
[Collection(ApiCollection.Name)]
public sealed class StripeConnectPayoutTests(ApiFactory api)
{
    private static int _offset = 2600;

    // ---------- Adapter: exact HTTP contract with Stripe ----------

    private sealed class StubStripe(Func<HttpRequestMessage, string, (HttpStatusCode, object)> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request, body));
            var (status, json) = respond(request, body);
            return new HttpResponseMessage(status) { Content = new StringContent(JsonSerializer.Serialize(json), Encoding.UTF8, "application/json") };
        }
    }

    private static StripeConnectPayoutProvider Provider(StubStripe stub)
    {
        var http = new HttpClient(stub);
        StripeConnectPayoutProvider.Configure(http, new StripeConnectOptions { SecretKey = "sk_test_123", BaseUrl = "https://stripe.test/" });
        return new StripeConnectPayoutProvider(http, NullLogger<StripeConnectPayoutProvider>.Instance);
    }

    private static Dictionary<string, string> Form(string body) => body.Split('&').Select(p => p.Split('=')).ToDictionary(
        p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1].Replace('+', ' ')));

    [Fact]
    public async Task Transfers_are_sent_in_minor_units_with_an_idempotency_key_per_payout()
    {
        var stub = new StubStripe((_, _) => (HttpStatusCode.OK, new { id = "tr_123" }));
        var provider = Provider(stub);
        var payoutId = Guid.NewGuid();

        var eur = await provider.SendAsync(payoutId, "acct_host1", 1234.565m, "EUR", CancellationToken.None);
        await provider.SendAsync(Guid.NewGuid(), "acct_host1", 15000m, "JPY", CancellationToken.None);

        eur.ShouldBe(new PayoutTransferResult(true, "tr_123", null));
        var (request, body) = stub.Calls[0];
        request.RequestUri!.ToString().ShouldBe("https://stripe.test/v1/transfers");
        request.Headers.Authorization!.ToString().ShouldBe("Bearer sk_test_123");
        request.Headers.GetValues("Idempotency-Key").Single().ShouldBe($"payout-{payoutId:N}");
        request.Headers.GetValues("Stripe-Version").ShouldNotBeEmpty();
        var form = Form(body);
        form["amount"].ShouldBe("123457");
        form["currency"].ShouldBe("eur");
        form["destination"].ShouldBe("acct_host1");
        form["transfer_group"].ShouldBe($"payout_{payoutId:N}");
        Form(stub.Calls[1].Body)["amount"].ShouldBe("15000", "JPY is zero-decimal");
    }

    [Fact]
    public async Task Rejections_fail_the_payout_but_outages_throw_so_it_is_retried()
    {
        var rejected = Provider(new StubStripe((_, _) => (HttpStatusCode.BadRequest, new { error = new { message = "Insufficient funds in platform balance." } })));
        (await rejected.SendAsync(Guid.NewGuid(), "acct_x", 10m, "USD", CancellationToken.None))
            .ShouldBe(new PayoutTransferResult(false, null, "Insufficient funds in platform balance."));

        var down = Provider(new StubStripe((_, _) => (HttpStatusCode.ServiceUnavailable, new { })));
        await Should.ThrowAsync<HttpRequestException>(() => down.SendAsync(Guid.NewGuid(), "acct_x", 10m, "USD", CancellationToken.None));

        var stub = new StubStripe((_, _) => (HttpStatusCode.OK, new { id = "tr_never" }));
        (await Provider(stub).SendAsync(Guid.NewGuid(), "PT50 •••• 0154", 10m, "EUR", CancellationToken.None)).Succeeded.ShouldBeFalse();
        stub.Calls.ShouldBeEmpty("never send money to something that isn't a connected account");
    }

    [Fact]
    public async Task Status_lookup_and_account_sync_read_stripe_responses()
    {
        var payoutId = Guid.NewGuid();
        var provider = Provider(new StubStripe((req, _) => req.RequestUri!.AbsolutePath switch
        {
            "/v1/transfers" => (HttpStatusCode.OK, new { data = new[] { new { id = "tr_9", reversed = true } } }),
            _ => (HttpStatusCode.OK, (object)new
            {
                id = "acct_1", country = "PT", payouts_enabled = false,
                external_accounts = new { data = new[] { new { last4 = "0154", account_holder_name = "Ana Silva" } } },
                requirements = new { currently_due = new[] { "individual.verification.document" }, disabled_reason = (string?)null },
            }),
        }));

        (await provider.GetStatusAsync(payoutId, CancellationToken.None))!.Succeeded.ShouldBeFalse("a reversed transfer is a failed payout");
        var status = await provider.GetConnectedAccountAsync("acct_1", CancellationToken.None);
        status.ShouldBe(new ConnectedAccountStatus(false, "Ana Silva", "0154", "PT", "Stripe needs more information to enable payouts. Continue setup to finish."));
    }

    // ---------- Service + API: hosted onboarding flow ----------

    private sealed class ScriptedConnect : IPayoutProvider
    {
        public bool Enabled { get; set; }
        public ConcurrentBag<string> Destinations { get; } = [];
        public bool UsesHostedOnboarding => true;

        public Task<PayoutTransferResult> SendAsync(Guid payoutId, string destination, decimal amount, string currency, CancellationToken ct)
        {
            Destinations.Add(destination);
            return Task.FromResult(new PayoutTransferResult(true, "tr_" + payoutId.ToString("N")[..8], null));
        }

        public Task<PayoutTransferResult?> GetStatusAsync(Guid payoutId, CancellationToken ct) => Task.FromResult<PayoutTransferResult?>(null);
        public Task<string> CreateConnectedAccountAsync(Guid hostId, string email, string country, CancellationToken ct) => Task.FromResult($"acct_{hostId:N}"[..21]);
        public Task<string> CreateOnboardingLinkAsync(string id, string returnUrl, string refreshUrl, CancellationToken ct) =>
            Task.FromResult($"https://connect.stripe.test/setup/{id}?return={Uri.EscapeDataString(returnUrl)}");
        public Task<ConnectedAccountStatus> GetConnectedAccountAsync(string id, CancellationToken ct) =>
            Task.FromResult(new ConnectedAccountStatus(Enabled, "Ana Silva", Enabled ? "0154" : null, "PT", Enabled ? null : "Continue setup"));
    }

    [Fact]
    public async Task Hosts_onboard_on_stripe_and_are_paid_to_their_connected_account()
    {
        var stripe = new ScriptedConnect();
        using var app = api.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IPayoutProvider>(stripe)));
        var (mainHost, host) = await api.RegisterHostAsync();
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = mainHost.DefaultRequestHeaders.Authorization;

        // A paid stay that checked in 2 days ago, so its earnings are available.
        var propertyId = await api.CreatePublishedPropertyAsync(host.User.Id);
        var (guest, _) = await api.RegisterAsync();
        var ci = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(Interlocked.Add(ref _offset, 10));
        var quote = await (await guest.PostJsonAsync($"/api/v1/properties/{propertyId}/quote", new QuoteRequest(ci, ci.AddDays(3), 2, null))).ReadAsync<QuoteDto>();
        var held = await (await guest.PostJsonAsync("/api/v1/reservations", new CreateReservationRequest(propertyId, ci, ci.AddDays(3), 2, quote.QuoteToken, null),
            Guid.NewGuid().ToString())).ReadAsync<ReservationDto>();
        await guest.PostJsonAsync($"/api/v1/reservations/{held.Id}/payment", new PayReservationRequest("tok_4242"), Guid.NewGuid().ToString());
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await api.WithScopeAsync(sp => sp.GetRequiredService<AppDbContext>().Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE booking.Reservations SET CheckIn = {today.AddDays(-2)}, CheckOut = {today.AddDays(1)} WHERE Id = {held.Id}"));

        (await (await client.GetAsync("/api/v1/host/payouts")).ReadAsync<PayoutSummaryDto>()).HostedOnboarding.ShouldBeTrue();
        (await client.PutAsync("/api/v1/host/payout-account", System.Net.Http.Json.JsonContent.Create(new PayoutAccountRequest("Ana", "PT50000201231234567890154", "PT"))))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, "bank details go to Stripe, never to us");

        var link = await (await client.PostJsonAsync("/api/v1/host/payouts/onboarding", new { })).ReadAsync<PayoutOnboardingDto>();
        link.Url.ShouldStartWith("https://connect.stripe.test/setup/acct_");
        link.Url.ShouldContain(Uri.EscapeDataString("/host/earnings?payouts=return"));
        var again = await (await client.PostJsonAsync("/api/v1/host/payouts/onboarding", new { })).ReadAsync<PayoutOnboardingDto>();
        again.Url.Split('?')[0].ShouldBe(link.Url.Split('?')[0], "one connected account per host");

        (await client.PostJsonAsync("/api/v1/host/payouts", new { }, Guid.NewGuid().ToString())).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, "onboarding unfinished");

        stripe.Enabled = true;
        var account = await (await client.PostJsonAsync("/api/v1/host/payouts/account/refresh", new { })).ReadAsync<PayoutAccountDto>();
        account.PayoutsEnabled.ShouldBeTrue();
        account.MaskedAccount.ShouldBe("Bank •••• 0154");

        var paid = await (await client.PostJsonAsync("/api/v1/host/payouts", new { }, Guid.NewGuid().ToString())).ReadAsync<List<PayoutDto>>();
        paid.ShouldHaveSingleItem().Status.ShouldBe(nameof(PayoutStatus.Paid));
        stripe.Destinations.ShouldAllBe(d => d.StartsWith("acct_"));
    }
}
