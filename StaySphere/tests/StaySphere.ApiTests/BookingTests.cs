using System.Net;
using StaySphere.Contracts;
using StaySphere.Contracts.Booking;
using StaySphere.Contracts.Payments;
using StaySphere.Contracts.Properties;

namespace StaySphere.ApiTests;

/// <summary>The money path end to end over HTTP: search → quote → hold → pay, plus the guarantees around it.</summary>
public sealed class BookingTests
{
    private static async Task<PropertyCardDto> InstantBookListingAsync(HttpClient client)
    {
        var page = await (await client.GetAsync("api/v1/search/properties?instantBook=true&pageSize=50")).ReadAsync<PagedResult<PropertyCardDto>>();
        page.Items.ShouldNotBeEmpty("the seeded stack has instant-book listings");
        return page.Items[Random.Shared.Next(page.Items.Count)];
    }

    // Far-future, randomised dates so reruns against the same stack don't collide.
    private static DateOnly FreeCheckIn() => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(400 + Random.Shared.Next(0, 1500));

    [ApiFact]
    public async Task Guest_books_and_pays_and_the_payment_is_idempotent()
    {
        var guest = await Api.DemoGuestAsync();
        var listing = await InstantBookListingAsync(guest);
        var ci = FreeCheckIn();

        var quote = await (await guest.PostAsync($"api/v1/properties/{listing.Id}/quote", new QuoteRequest(ci, ci.AddDays(2), 1, null))).ReadAsync<QuoteDto>();
        quote.Total.ShouldBeGreaterThan(0);
        var held = await (await guest.PostAsync("api/v1/reservations", new CreateReservationRequest(listing.Id, ci, ci.AddDays(2), 1, quote.QuoteToken, null),
            Guid.NewGuid().ToString())).ReadAsync<ReservationDto>();
        held.TotalAmount.ShouldBe(quote.Total, "the hold is bound to the signed quote");

        var key = Guid.NewGuid().ToString();
        var first = await guest.PostAsync($"api/v1/reservations/{held.Id}/payment", new PayReservationRequest("tok_4242"), key);
        var replay = await guest.PostAsync($"api/v1/reservations/{held.Id}/payment", new PayReservationRequest("tok_4242"), key);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        replay.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await replay.Content.ReadAsStringAsync()).ShouldBe(await first.Content.ReadAsStringAsync(), "same key → same response, charged once");
    }

    [ApiFact]
    public async Task The_same_nights_can_never_be_held_twice()
    {
        var listing = await InstantBookListingAsync(Api.Client());
        var ci = FreeCheckIn();
        var (newUser, _) = await Api.NewUserAsync();
        var clients = new[] { newUser, await Api.DemoGuestAsync(), newUser, await Api.DemoGuestAsync() };

        var attempts = await Task.WhenAll(clients.Select(async c =>
        {
            var quote = await (await c.PostAsync($"api/v1/properties/{listing.Id}/quote", new QuoteRequest(ci, ci.AddDays(3), 1, null))).ReadAsync<QuoteDto>();
            return await c.PostAsync("api/v1/reservations", new CreateReservationRequest(listing.Id, ci, ci.AddDays(3), 1, quote.QuoteToken, null),
                Guid.NewGuid().ToString());
        }));

        attempts.Count(r => r.IsSuccessStatusCode).ShouldBe(1);
        attempts.Where(r => !r.IsSuccessStatusCode).ShouldAllBe(r => r.StatusCode == HttpStatusCode.Conflict);
    }

    [ApiFact]
    public async Task A_tampered_quote_token_is_rejected()
    {
        var guest = await Api.DemoGuestAsync();
        var listing = await InstantBookListingAsync(guest);
        var ci = FreeCheckIn();
        var quote = await (await guest.PostAsync($"api/v1/properties/{listing.Id}/quote", new QuoteRequest(ci, ci.AddDays(2), 1, null))).ReadAsync<QuoteDto>();

        var response = await guest.PostAsync("api/v1/reservations",
            new CreateReservationRequest(listing.Id, ci, ci.AddDays(5), 1, quote.QuoteToken, null), Guid.NewGuid().ToString());
        response.IsSuccessStatusCode.ShouldBeFalse("a quote for 2 nights can't buy 5");
    }
}
