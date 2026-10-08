using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StaySphere.Application.Booking;
using StaySphere.Contracts.Booking;
using StaySphere.Contracts.Payments;
using StaySphere.Domain.Payments;
using StaySphere.Infrastructure.Persistence;
using StaySphere.IntegrationTests.Infrastructure;

namespace StaySphere.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class BookingRequestAndPayoutTests(ApiFactory api)
{
    private static int _offset = 600;

    private static (DateOnly, DateOnly) NextDates()
    {
        var start = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(Interlocked.Add(ref _offset, 10));
        return (start, start.AddDays(3));
    }

    private async Task<ReservationDto> RequestAsync(HttpClient guest, Guid propertyId, DateOnly checkIn, DateOnly checkOut, string card = "tok_4242")
    {
        var quote = await (await guest.PostJsonAsync($"/api/v1/properties/{propertyId}/quote", new QuoteRequest(checkIn, checkOut, 2, null))).ReadAsync<QuoteDto>();
        var held = await (await guest.PostJsonAsync("/api/v1/reservations", new CreateReservationRequest(propertyId, checkIn, checkOut, 2, quote.QuoteToken, null),
            Guid.NewGuid().ToString())).ReadAsync<ReservationDto>();
        held.RequiresApproval.ShouldBeTrue();
        var pay = await guest.PostJsonAsync($"/api/v1/reservations/{held.Id}/payment", new PayReservationRequest(card), Guid.NewGuid().ToString());
        pay.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await pay.ReadAsync<PaymentDto>()).Status.ShouldBe("Authorized");
        return await (await guest.GetAsync($"/api/v1/reservations/{held.Id}")).ReadAsync<ReservationDto>();
    }

    private Task<(int Ledger, PaymentStatus Payment)> StateAsync(Guid reservationId) => api.WithScopeAsync(async sp =>
    {
        var db = sp.GetRequiredService<AppDbContext>();
        return (await db.LedgerEntries.CountAsync(l => l.ReservationId == reservationId),
            await db.Payments.Where(p => p.ReservationId == reservationId).Select(p => p.Status).SingleAsync());
    });

    [Fact]
    public async Task Request_is_authorized_not_charged_and_accepting_captures_and_confirms()
    {
        var (hostClient, host) = await api.RegisterHostAsync();
        var propertyId = await api.CreatePublishedPropertyAsync(host.User.Id, instantBook: false);
        var (guest, _) = await api.RegisterAsync();
        var (ci, co) = NextDates();

        var request = await RequestAsync(guest, propertyId, ci, co);
        request.Status.ShouldBe("AwaitingApproval");
        request.ApprovalDeadline.ShouldNotBeNull();
        (await StateAsync(request.Id)).ShouldBe((0, PaymentStatus.Authorized), "no money moved, no earnings booked");

        var (other, _) = await api.RegisterAsync();
        var otherQuote = await (await other.PostJsonAsync($"/api/v1/properties/{propertyId}/quote", new QuoteRequest(ci, co, 2, null))).ReadAsync<QuoteDto>();
        otherQuote.Available.ShouldBeFalse("pending requests keep the dates blocked");

        var hostView = await (await hostClient.GetAsync("/api/v1/host/reservations?scope=requests")).ReadAsync<Contracts.PagedResult<ReservationDto>>();
        hostView.Items.ShouldContain(r => r.Id == request.Id && r.CanRespond);

        var accept = await hostClient.PostJsonAsync($"/api/v1/host/reservations/{request.Id}/accept", new { }, "accept-1");
        accept.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await accept.ReadAsync<ReservationDto>()).Status.ShouldBe("Confirmed");
        (await StateAsync(request.Id)).ShouldBe((4, PaymentStatus.Succeeded));

        (await hostClient.PostJsonAsync($"/api/v1/host/reservations/{request.Id}/accept", new { }, "accept-1"))
            .Headers.GetValues("Idempotent-Replayed").ShouldContain("true");
    }

    [Fact]
    public async Task Declining_voids_the_authorization_releases_dates_and_notifies_the_guest()
    {
        var (hostClient, host) = await api.RegisterHostAsync();
        var propertyId = await api.CreatePublishedPropertyAsync(host.User.Id, instantBook: false);
        var (guest, guestAuth) = await api.RegisterAsync();
        var (ci, co) = NextDates();
        var request = await RequestAsync(guest, propertyId, ci, co);

        var decline = await hostClient.PostJsonAsync($"/api/v1/host/reservations/{request.Id}/decline", new DeclineRequest("Maintenance that week"), "decline-1");
        decline.StatusCode.ShouldBe(HttpStatusCode.OK);
        var declined = await decline.ReadAsync<ReservationDto>();
        declined.Status.ShouldBe("Declined");
        declined.DeclineReason.ShouldBe("Maintenance that week");
        (await StateAsync(request.Id)).ShouldBe((0, PaymentStatus.Voided));

        var (other, _) = await api.RegisterAsync();
        var quote = await (await other.PostJsonAsync($"/api/v1/properties/{propertyId}/quote", new QuoteRequest(ci, co, 2, null))).ReadAsync<QuoteDto>();
        quote.Available.ShouldBeTrue();

        var notified = await HttpExtensions.EventuallyAsync(() => api.WithScopeAsync(sp => sp.GetRequiredService<AppDbContext>().Notifications
            .AnyAsync(n => n.UserId == guestAuth.User.Id && n.Type == "reservation.declined")), done => done);
        notified.ShouldBeTrue();
        (await hostClient.PostJsonAsync($"/api/v1/host/reservations/{request.Id}/accept", new { }, "accept-late")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Only_the_listing_host_can_respond_and_unanswered_requests_expire()
    {
        var (_, host) = await api.RegisterHostAsync();
        var (otherHost, _) = await api.RegisterHostAsync();
        var propertyId = await api.CreatePublishedPropertyAsync(host.User.Id, instantBook: false);
        var (guest, _) = await api.RegisterAsync();
        var (ci, co) = NextDates();
        var request = await RequestAsync(guest, propertyId, ci, co);

        (await otherHost.PostJsonAsync($"/api/v1/host/reservations/{request.Id}/accept", new { }, "steal")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await guest.PostJsonAsync($"/api/v1/host/reservations/{request.Id}/accept", new { }, "self")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        await api.WithScopeAsync(sp => sp.GetRequiredService<AppDbContext>().Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE booking.Reservations SET ApprovalDeadline = {DateTimeOffset.UtcNow.AddMinutes(-1)} WHERE Id = {request.Id}"));
        var expired = await api.WithScopeAsync(sp => sp.GetRequiredService<IBookingRequestService>().ExpireOverdueAsync(CancellationToken.None));

        expired.ShouldBeGreaterThanOrEqualTo(1);
        var after = await (await guest.GetAsync($"/api/v1/reservations/{request.Id}")).ReadAsync<ReservationDto>();
        after.Status.ShouldBe("Declined");
        after.DeclineReason.ShouldBe("The host did not respond in time.");
        (await StateAsync(request.Id)).Payment.ShouldBe(PaymentStatus.Voided);
    }

    [Fact]
    public async Task Guest_withdrawing_a_request_voids_the_card_hold()
    {
        var (_, host) = await api.RegisterHostAsync();
        var propertyId = await api.CreatePublishedPropertyAsync(host.User.Id, instantBook: false);
        var (guest, _) = await api.RegisterAsync();
        var (ci, co) = NextDates();
        var request = await RequestAsync(guest, propertyId, ci, co);

        var cancel = await guest.PostJsonAsync($"/api/v1/reservations/{request.Id}/cancel", new CancelReservationRequest(null), "withdraw");
        (await cancel.ReadAsync<ReservationDto>()).Status.ShouldBe("Cancelled");

        var payment = await HttpExtensions.EventuallyAsync(async () => (await StateAsync(request.Id)).Payment, s => s == PaymentStatus.Voided);
        payment.ShouldBe(PaymentStatus.Voided);
    }

    private async Task<(HttpClient Host, Guid ReservationId, decimal Earning)> HostWithCheckedInStayAsync()
    {
        var (hostClient, host) = await api.RegisterHostAsync();
        var propertyId = await api.CreatePublishedPropertyAsync(host.User.Id);
        var (guest, _) = await api.RegisterAsync();
        var (ci, co) = NextDates();
        var quote = await (await guest.PostJsonAsync($"/api/v1/properties/{propertyId}/quote", new QuoteRequest(ci, co, 2, null))).ReadAsync<QuoteDto>();
        var held = await (await guest.PostJsonAsync("/api/v1/reservations", new CreateReservationRequest(propertyId, ci, co, 2, quote.QuoteToken, null), Guid.NewGuid().ToString()))
            .ReadAsync<ReservationDto>();
        await guest.PostJsonAsync($"/api/v1/reservations/{held.Id}/payment", new PayReservationRequest("tok_4242"), Guid.NewGuid().ToString());

        // The guest checked in two days ago → earnings are past the 24h release delay.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var earning = await api.WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE booking.Reservations SET CheckIn = {today.AddDays(-2)}, CheckOut = {today.AddDays(1)} WHERE Id = {held.Id}");
            return await db.LedgerEntries.Where(l => l.ReservationId == held.Id && l.Account == LedgerAccount.HostEarning).SumAsync(l => l.Amount);
        });
        return (hostClient, held.Id, earning);
    }

    [Fact]
    public async Task Host_payout_pays_available_earnings_exactly_once()
    {
        var (host, reservationId, earning) = await HostWithCheckedInStayAsync();

        (await host.PostJsonAsync("/api/v1/host/payouts", new { }, "p0")).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, "no payout account yet");

        var summary = await (await host.GetAsync("/api/v1/host/payouts")).ReadAsync<PayoutSummaryDto>();
        summary.Balances.Single().Available.ShouldBe(earning);

        (await host.PutAsync("/api/v1/host/payout-account", System.Net.Http.Json.JsonContent.Create(new PayoutAccountRequest("Test Host", "PT50000201231234567890154", "PT"))))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // Two simultaneous requests: exactly one payout is created.
        var responses = await Task.WhenAll(host.PostJsonAsync("/api/v1/host/payouts", new { }, "p1"), host.PostJsonAsync("/api/v1/host/payouts", new { }, "p2"));
        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);

        var payouts = await api.WithScopeAsync(sp => sp.GetRequiredService<AppDbContext>().HostPayouts.Include(p => p.Items)
            .Where(p => p.Items.Any(i => i.ReservationId == reservationId)).ToListAsync());
        payouts.ShouldHaveSingleItem().Status.ShouldBe(PayoutStatus.Paid);
        payouts[0].Amount.ShouldBe(decimal.Round(earning, 2));

        var after = await (await host.GetAsync("/api/v1/host/payouts")).ReadAsync<PayoutSummaryDto>();
        after.Balances.Single().Available.ShouldBe(0m);
        after.Balances.Single().PaidOut.ShouldBe(decimal.Round(earning, 2));
        (await host.PostJsonAsync("/api/v1/host/payouts", new { }, "p3")).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, "nothing left to pay");
    }

    [Fact]
    public async Task Failed_bank_transfer_is_reversed_so_the_money_is_available_again()
    {
        var (host, _, earning) = await HostWithCheckedInStayAsync();
        await host.PutAsync("/api/v1/host/payout-account", System.Net.Http.Json.JsonContent.Create(new PayoutAccountRequest("Test Host", "GB00000000000000000000", "GB")));

        var result = await (await host.PostJsonAsync("/api/v1/host/payouts", new { }, "fail-1")).ReadAsync<List<PayoutDto>>();

        result.ShouldHaveSingleItem().Status.ShouldBe("Failed");
        var summary = await (await host.GetAsync("/api/v1/host/payouts")).ReadAsync<PayoutSummaryDto>();
        summary.Balances.Single().Available.ShouldBe(decimal.Round(earning, 2), "compensating ledger entries restored the balance");
    }

    [Fact]
    public async Task Earnings_before_check_in_are_pending_not_payable()
    {
        var (hostClient, host) = await api.RegisterHostAsync();
        var propertyId = await api.CreatePublishedPropertyAsync(host.User.Id);
        var (guest, _) = await api.RegisterAsync();
        var (ci, co) = NextDates();
        var quote = await (await guest.PostJsonAsync($"/api/v1/properties/{propertyId}/quote", new QuoteRequest(ci, co, 2, null))).ReadAsync<QuoteDto>();
        var held = await (await guest.PostJsonAsync("/api/v1/reservations", new CreateReservationRequest(propertyId, ci, co, 2, quote.QuoteToken, null), "h"))
            .ReadAsync<ReservationDto>();
        await guest.PostJsonAsync($"/api/v1/reservations/{held.Id}/payment", new PayReservationRequest("tok_4242"), "pay");

        var balance = (await (await hostClient.GetAsync("/api/v1/host/payouts")).ReadAsync<PayoutSummaryDto>()).Balances.Single();
        balance.Available.ShouldBe(0m);
        balance.Pending.ShouldBeGreaterThan(0m);
    }
}
