using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StaySphere.Application.Booking;
using StaySphere.Application.Common;
using StaySphere.Contracts.Booking;
using StaySphere.Contracts.Payments;
using StaySphere.Contracts.Properties;
using StaySphere.Contracts.Reviews;
using StaySphere.Domain.Booking;
using StaySphere.Domain.Payments;
using StaySphere.Infrastructure.Payments;
using StaySphere.Infrastructure.Persistence;
using StaySphere.IntegrationTests.Infrastructure;

namespace StaySphere.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class BookingTests(ApiFactory api)
{
    private static int _offset = 20;

    private static (DateOnly CheckIn, DateOnly CheckOut) NextDates(int nights = 3)
    {
        var start = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(Interlocked.Add(ref _offset, 10));
        return (start, start.AddDays(nights));
    }

    private static async Task<QuoteDto> QuoteAsync(HttpClient client, Guid propertyId, DateOnly checkIn, DateOnly checkOut, int guests = 2)
    {
        var response = await client.PostJsonAsync($"/api/v1/properties/{propertyId}/quote", new QuoteRequest(checkIn, checkOut, guests, null));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.ReadAsync<QuoteDto>();
    }

    private static async Task<HttpResponseMessage> ReserveAsync(HttpClient client, Guid propertyId, QuoteDto quote, string? key = null) =>
        await client.PostJsonAsync("/api/v1/reservations",
            new CreateReservationRequest(propertyId, quote.CheckIn, quote.CheckOut, quote.Guests, quote.QuoteToken, null), key ?? Guid.NewGuid().ToString());

    [Fact]
    public async Task Two_users_booking_the_same_dates_concurrently_only_one_succeeds()
    {
        var (_, host) = await api.RegisterHostAsync();
        var propertyId = await api.CreatePublishedPropertyAsync(host.User.Id);
        var (checkIn, checkOut) = NextDates();

        var guests = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => api.RegisterAsync()));
        var quotes = await Task.WhenAll(guests.Select(g => QuoteAsync(g.Client, propertyId, checkIn, checkOut)));

        var responses = await Task.WhenAll(guests.Select((g, i) => ReserveAsync(g.Client, propertyId, quotes[i])));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(19);

        var activeNights = await api.WithScopeAsync(sp => sp.GetRequiredService<AppDbContext>().ReservationNights
            .CountAsync(n => n.PropertyId == propertyId && n.IsActive));
        activeNights.ShouldBe(3);
    }

    [Fact]
    public async Task Overlapping_but_not_identical_stays_also_conflict_while_back_to_back_stays_succeed()
    {
        var (_, host) = await api.RegisterHostAsync();
        var propertyId = await api.CreatePublishedPropertyAsync(host.User.Id);
        var (a, b) = await (api.RegisterAsync(), api.RegisterAsync()).WhenBoth();
        var (checkIn, checkOut) = NextDates(4);

        (await ReserveAsync(a.Client, propertyId, await QuoteAsync(a.Client, propertyId, checkIn, checkOut))).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await ReserveAsync(b.Client, propertyId, await QuoteAsync(b.Client, propertyId, checkIn.AddDays(3), checkOut.AddDays(3))))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReserveAsync(b.Client, propertyId, await QuoteAsync(b.Client, propertyId, checkOut, checkOut.AddDays(2))))
            .StatusCode.ShouldBe(HttpStatusCode.Created, "check-out day is free for the next check-in");
    }

    [Fact]
    public async Task Reservation_command_sent_twice_with_same_key_is_idempotent()
    {
        var (_, host) = await api.RegisterHostAsync();
        var propertyId = await api.CreatePublishedPropertyAsync(host.User.Id);
        var (client, guest) = await api.RegisterAsync();
        var (checkIn, checkOut) = NextDates();
        var quote = await QuoteAsync(client, propertyId, checkIn, checkOut);

        var first = await ReserveAsync(client, propertyId, quote, "same-key");
        var second = await ReserveAsync(client, propertyId, quote, "same-key");

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.Headers.GetValues("Idempotent-Replayed").ShouldContain("true");
        (await second.ReadAsync<ReservationDto>()).Id.ShouldBe((await first.ReadAsync<ReservationDto>()).Id);

        var count = await api.WithScopeAsync(sp => sp.GetRequiredService<AppDbContext>().Reservations.CountAsync(r => r.GuestId == guest.User.Id));
        count.ShouldBe(1);

        var other = await QuoteAsync(client, propertyId, checkOut.AddDays(5), checkOut.AddDays(7));
        (await ReserveAsync(client, propertyId, other, "same-key")).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, "same key, different payload");
    }

    [Fact]
    public async Task Missing_idempotency_key_and_tampered_quote_are_rejected()
    {
        var (_, host) = await api.RegisterHostAsync();
        var propertyId = await api.CreatePublishedPropertyAsync(host.User.Id);
        var (client, _) = await api.RegisterAsync();
        var (checkIn, checkOut) = NextDates();
        var quote = await QuoteAsync(client, propertyId, checkIn, checkOut);

        (await client.PostJsonAsync("/api/v1/reservations", new CreateReservationRequest(propertyId, checkIn, checkOut, 2, quote.QuoteToken, null)))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var tampered = TamperTotal(quote.QuoteToken, 1m);
        (await ReserveAsync(client, propertyId, quote with { QuoteToken = tampered })).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReserveAsync(client, propertyId, quote with { Guests = 3 })).StatusCode.ShouldBe(HttpStatusCode.Conflict, "quote is bound to the guest count");
    }

    [Fact]
    public async Task Pay_confirms_reservation_and_writes_ledger_and_outbox_drives_notifications()
    {
        var (_, host) = await api.RegisterHostAsync();
        var propertyId = await api.CreatePublishedPropertyAsync(host.User.Id);
        var (client, guest) = await api.RegisterAsync();
        var (checkIn, checkOut) = NextDates();
        var reservation = await (await ReserveAsync(client, propertyId, await QuoteAsync(client, propertyId, checkIn, checkOut))).ReadAsync<ReservationDto>();

        var pay = await client.PostJsonAsync($"/api/v1/reservations/{reservation.Id}/payment", new PayReservationRequest("tok_4242"), "pay-1");
        pay.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await pay.ReadAsync<PaymentDto>()).ReservationStatus.ShouldBe("Confirmed");

        var replay = await client.PostJsonAsync($"/api/v1/reservations/{reservation.Id}/payment", new PayReservationRequest("tok_4242"), "pay-1");
        replay.Headers.GetValues("Idempotent-Replayed").ShouldContain("true");

        var (payments, ledger) = await api.WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            return (await db.Payments.CountAsync(p => p.ReservationId == reservation.Id),
                await db.LedgerEntries.Where(l => l.ReservationId == reservation.Id).ToListAsync());
        });
        payments.ShouldBe(1);
        ledger.Single(l => l.Account == LedgerAccount.GuestPayment).Amount.ShouldBe(reservation.TotalAmount);
        ledger.Where(l => l.Account != LedgerAccount.GuestPayment).Sum(l => l.Amount).ShouldBe(reservation.TotalAmount, "fees + taxes + host earning = guest payment");

        var notified = await HttpExtensions.EventuallyAsync(
            () => api.WithScopeAsync(sp => sp.GetRequiredService<AppDbContext>().Notifications.AnyAsync(n => n.UserId == host.User.Id && n.Type == "host.booking_received")),
            done => done);
        notified.ShouldBeTrue("ReservationConfirmed → outbox → bus → notification handler");
    }

    [Fact]
    public async Task Declined_card_fails_reservation_and_releases_dates()
    {
        var (_, host) = await api.RegisterHostAsync();
        var propertyId = await api.CreatePublishedPropertyAsync(host.User.Id);
        var (client, _) = await api.RegisterAsync();
        var (checkIn, checkOut) = NextDates();
        var reservation = await (await ReserveAsync(client, propertyId, await QuoteAsync(client, propertyId, checkIn, checkOut))).ReadAsync<ReservationDto>();

        var pay = await client.PostJsonAsync($"/api/v1/reservations/{reservation.Id}/payment", new PayReservationRequest("tok_0002"), "pay-declined");

        pay.StatusCode.ShouldBe(HttpStatusCode.PaymentRequired);
        (await (await client.GetAsync($"/api/v1/reservations/{reservation.Id}")).ReadAsync<ReservationDto>()).Status.ShouldBe("Failed");
        var (other, _) = await api.RegisterAsync();
        (await ReserveAsync(other, propertyId, await QuoteAsync(other, propertyId, checkIn, checkOut))).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Payment_webhook_received_twice_produces_a_single_transaction()
    {
        var (_, host) = await api.RegisterHostAsync();
        var propertyId = await api.CreatePublishedPropertyAsync(host.User.Id);
        var (client, _) = await api.RegisterAsync();
        var (checkIn, checkOut) = NextDates();
        var reservation = await (await ReserveAsync(client, propertyId, await QuoteAsync(client, propertyId, checkIn, checkOut))).ReadAsync<ReservationDto>();

        var pending = await (await client.PostJsonAsync($"/api/v1/reservations/{reservation.Id}/payment", new PayReservationRequest("tok_timeout"), "pay-timeout"))
            .ReadAsync<PaymentDto>();
        pending.Status.ShouldBe("Pending");

        var payload = JsonSerializer.Serialize(new FakeWebhookPayload("evt_dup_" + Guid.NewGuid().ToString("N"), "payment.succeeded", "pi_test", pending.Id,
            pending.Amount, pending.Currency, null), Json.Options);
        var webhookClient = api.CreateClient();
        var first = await SendWebhook(webhookClient, payload);
        var second = await SendWebhook(webhookClient, payload);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);

        var (captures, events) = await api.WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var captureCount = await db.Payments.Where(p => p.Id == pending.Id).SelectMany(p => p.Transactions).CountAsync(t => t.Kind == PaymentTransactionKind.Charge);
            var webhookCount = await db.WebhookEvents.CountAsync(w => w.Payload == payload);
            return (captureCount, webhookCount);
        });
        captures.ShouldBe(1);
        events.ShouldBe(1);
        (await (await client.GetAsync($"/api/v1/reservations/{reservation.Id}")).ReadAsync<ReservationDto>()).Status.ShouldBe("Confirmed");
    }

    [Fact]
    public async Task Webhook_with_bad_signature_or_stale_timestamp_is_rejected()
    {
        var payload = JsonSerializer.Serialize(new FakeWebhookPayload("evt_bad", "payment.succeeded", "pi", Guid.NewGuid(), 1, "EUR", null), Json.Options);
        var client = api.CreateClient();

        (await SendWebhook(client, payload, signature: "deadbeef")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await SendWebhook(client, payload, timestamp: DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds())).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Expired_hold_is_released_automatically()
    {
        var (_, host) = await api.RegisterHostAsync();
        var propertyId = await api.CreatePublishedPropertyAsync(host.User.Id);
        var (client, _) = await api.RegisterAsync();
        var (checkIn, checkOut) = NextDates();
        var reservation = await (await ReserveAsync(client, propertyId, await QuoteAsync(client, propertyId, checkIn, checkOut))).ReadAsync<ReservationDto>();

        await api.WithScopeAsync(sp => sp.GetRequiredService<AppDbContext>().Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE booking.Reservations SET HoldExpiresAt = {DateTimeOffset.UtcNow.AddMinutes(-1)} WHERE Id = {reservation.Id}"));
        var expired = await api.WithScopeAsync(sp => sp.GetRequiredService<IBookingMaintenance>().ExpireHoldsAsync(CancellationToken.None));

        expired.ShouldBeGreaterThanOrEqualTo(1);
        (await (await client.GetAsync($"/api/v1/reservations/{reservation.Id}")).ReadAsync<ReservationDto>()).Status.ShouldBe("Expired");
        var (other, _) = await api.RegisterAsync();
        (await ReserveAsync(other, propertyId, await QuoteAsync(other, propertyId, checkIn, checkOut))).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Cancelling_a_confirmed_stay_refunds_through_the_event_pipeline()
    {
        var (_, host) = await api.RegisterHostAsync();
        var propertyId = await api.CreatePublishedPropertyAsync(host.User.Id);
        var (client, _) = await api.RegisterAsync();
        var (checkIn, checkOut) = NextDates();
        var reservation = await (await ReserveAsync(client, propertyId, await QuoteAsync(client, propertyId, checkIn, checkOut))).ReadAsync<ReservationDto>();
        await client.PostJsonAsync($"/api/v1/reservations/{reservation.Id}/payment", new PayReservationRequest("tok_4242"), "pay-c");

        var cancel = await client.PostJsonAsync($"/api/v1/reservations/{reservation.Id}/cancel", new CancelReservationRequest("changed plans"), "cancel-1");
        cancel.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await cancel.ReadAsync<ReservationDto>()).Status.ShouldBe("RefundPending");

        var final = await HttpExtensions.EventuallyAsync(
            async () => (await (await client.GetAsync($"/api/v1/reservations/{reservation.Id}")).ReadAsync<ReservationDto>()).Status,
            s => s == "Refunded");
        final.ShouldBe("Refunded");
        var refundLedger = await api.WithScopeAsync(sp => sp.GetRequiredService<AppDbContext>().LedgerEntries
            .Where(l => l.ReservationId == reservation.Id && l.Account == LedgerAccount.Refund).SumAsync(l => l.Amount));
        refundLedger.ShouldBe(-reservation.TotalAmount);
    }

    [Fact]
    public async Task Guests_cannot_see_other_guests_reservations()
    {
        var (_, host) = await api.RegisterHostAsync();
        var propertyId = await api.CreatePublishedPropertyAsync(host.User.Id);
        var (owner, _) = await api.RegisterAsync();
        var (stranger, _) = await api.RegisterAsync();
        var (checkIn, checkOut) = NextDates();
        var reservation = await (await ReserveAsync(owner, propertyId, await QuoteAsync(owner, propertyId, checkIn, checkOut))).ReadAsync<ReservationDto>();

        (await stranger.GetAsync($"/api/v1/reservations/{reservation.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await stranger.PostJsonAsync($"/api/v1/reservations/{reservation.Id}/payment", new PayReservationRequest("tok_4242"), "steal"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reviews_require_a_completed_own_stay_and_only_one_per_stay()
    {
        var (_, host) = await api.RegisterHostAsync();
        var propertyId = await api.CreatePublishedPropertyAsync(host.User.Id);
        var (guestClient, _) = await api.RegisterAsync();
        var (strangerClient, _) = await api.RegisterAsync();
        var (checkIn, checkOut) = NextDates();
        var reservation = await (await ReserveAsync(guestClient, propertyId, await QuoteAsync(guestClient, propertyId, checkIn, checkOut))).ReadAsync<ReservationDto>();
        await guestClient.PostJsonAsync($"/api/v1/reservations/{reservation.Id}/payment", new PayReservationRequest("tok_4242"), "pay-r");
        var review = new CreateReviewRequest(reservation.Id, 5, 5, 5, 5, 5, 5, 5, "Fantastic place, spotless and quiet.");

        (await strangerClient.PostJsonAsync("/api/v1/reviews", review)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, "never booked");
        (await guestClient.PostJsonAsync("/api/v1/reviews", review)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, "before checkout");

        // Fast-forward: the stay happened in the past and the completion job ran.
        await api.WithScopeAsync(sp => sp.GetRequiredService<AppDbContext>().Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE booking.Reservations SET CheckIn = {DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-5)}, CheckOut = {DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-2)} WHERE Id = {reservation.Id}"));
        await api.WithScopeAsync(sp => sp.GetRequiredService<IBookingMaintenance>().CompleteFinishedStaysAsync(CancellationToken.None));

        (await guestClient.PostJsonAsync("/api/v1/reviews", review)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await guestClient.PostJsonAsync("/api/v1/reviews", review)).StatusCode.ShouldBe(HttpStatusCode.Conflict, "one review per stay");
    }

    [Fact]
    public async Task Hosts_cannot_modify_other_hosts_properties()
    {
        var (_, owner) = await api.RegisterHostAsync();
        var (otherHost, _) = await api.RegisterHostAsync();
        var (guest, _) = await api.RegisterAsync();
        var propertyId = await api.CreatePublishedPropertyAsync(owner.User.Id);
        var update = new UpdatePropertyRequest("Hijacked", null, null, null, null, null, null, null, null, null, null, null, null, null);

        (await otherHost.PatchAsync($"/api/v1/properties/{propertyId}", JsonContent(update))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await guest.PatchAsync($"/api/v1/properties/{propertyId}", JsonContent(update))).StatusCode.ShouldBe(HttpStatusCode.Forbidden, "guests lack the host role");
        (await api.CreateClient().PatchAsync($"/api/v1/properties/{propertyId}", JsonContent(update))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static StringContent JsonContent(object body) => new(JsonSerializer.Serialize(body, Json.Options), Encoding.UTF8, "application/json");

    private static Task<HttpResponseMessage> SendWebhook(HttpClient client, string payload, string? signature = null, long? timestamp = null)
    {
        var ts = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments/webhook/fake")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Fake-Timestamp", ts.ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Headers.Add("X-Fake-Signature", signature ?? LocalFakePaymentProvider.Sign(ApiFactory.WebhookSecret, payload, ts));
        return client.SendAsync(request);
    }

    private static string TamperTotal(string token, decimal newTotal)
    {
        var parts = token.Split('.');
        var json = Encoding.UTF8.GetString(FromBase64Url(parts[0]));
        var doc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
        var modified = doc.ToDictionary(kv => kv.Key, kv => kv.Key == "total" ? (object)newTotal : kv.Value);
        var body = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(modified)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{body}.{parts[1]}";
    }

    private static byte[] FromBase64Url(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}

internal static class TupleTaskExtensions
{
    public static async Task<(T1, T2)> WhenBoth<T1, T2>(this (Task<T1> A, Task<T2> B) tasks) => (await tasks.A, await tasks.B);
}
