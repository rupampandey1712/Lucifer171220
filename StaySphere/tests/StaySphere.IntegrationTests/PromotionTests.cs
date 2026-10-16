using System.Net;
using StaySphere.Contracts.Booking;
using StaySphere.IntegrationTests.Infrastructure;

namespace StaySphere.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class PromotionTests(ApiFactory api)
{
    private static int _offset = 1200;

    private static DateOnly NextCheckIn() => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(Interlocked.Add(ref _offset, 10));

    private static CreatePromotionRequest LongStay(Guid? propertyId, decimal percent = 20m) =>
        new("Stay longer, save more", "LongStay", percent, propertyId, 3, null, null, null, null, DateTimeOffset.UtcNow.AddDays(30));

    [Fact]
    public async Task Host_promotion_lowers_the_quote_and_the_held_price_matches()
    {
        var (hostClient, host) = await api.RegisterHostAsync();
        var propertyId = await api.CreatePublishedPropertyAsync(host.User.Id, price: 100m);
        var (guest, _) = await api.RegisterAsync();
        var checkIn = NextCheckIn();

        var before = await (await guest.PostJsonAsync($"/api/v1/properties/{propertyId}/quote", new QuoteRequest(checkIn, checkIn.AddDays(3), 2, null))).ReadAsync<QuoteDto>();
        (await hostClient.PostJsonAsync("/api/v1/host/promotions", LongStay(propertyId))).StatusCode.ShouldBe(HttpStatusCode.OK);

        var after = await (await guest.PostJsonAsync($"/api/v1/properties/{propertyId}/quote", new QuoteRequest(checkIn, checkIn.AddDays(3), 2, null))).ReadAsync<QuoteDto>();
        after.PromotionName.ShouldBe("Stay longer, save more");
        after.PromotionDiscount.ShouldBe(60m);
        after.Total.ShouldBeLessThan(before.Total);

        var held = await guest.PostJsonAsync("/api/v1/reservations",
            new CreateReservationRequest(propertyId, checkIn, checkIn.AddDays(3), 2, after.QuoteToken, null), Guid.NewGuid().ToString());
        held.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await held.ReadAsync<ReservationDto>()).TotalAmount.ShouldBe(after.Total);

        var shown = await (await api.CreateClient().GetAsync($"/api/v1/properties/{propertyId}/promotions")).ReadAsync<List<PromotionDto>>();
        shown.ShouldHaveSingleItem().Summary.ShouldContain("3+ nights");
    }

    [Fact]
    public async Task Short_stays_dont_get_a_long_stay_promotion()
    {
        var (hostClient, host) = await api.RegisterHostAsync();
        var propertyId = await api.CreatePublishedPropertyAsync(host.User.Id, price: 100m);
        await hostClient.PostJsonAsync("/api/v1/host/promotions", LongStay(null));
        var checkIn = NextCheckIn();

        var quote = await (await api.CreateClient().PostJsonAsync($"/api/v1/properties/{propertyId}/quote", new QuoteRequest(checkIn, checkIn.AddDays(2), 2, null)))
            .ReadAsync<QuoteDto>();

        quote.PromotionDiscount.ShouldBe(0m);
        quote.PromotionName.ShouldBeNull();
    }

    [Fact]
    public async Task Hosts_cant_promote_or_deactivate_someone_elses_listing()
    {
        var (ownerClient, owner) = await api.RegisterHostAsync();
        var propertyId = await api.CreatePublishedPropertyAsync(owner.User.Id);
        var (intruder, _) = await api.RegisterHostAsync();

        (await intruder.PostJsonAsync("/api/v1/host/promotions", LongStay(propertyId))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var promo = await (await ownerClient.PostJsonAsync("/api/v1/host/promotions", LongStay(propertyId))).ReadAsync<PromotionDto>();
        (await intruder.PostJsonAsync($"/api/v1/host/promotions/{promo.Id}/deactivate", new { })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ownerClient.PostJsonAsync($"/api/v1/host/promotions/{promo.Id}/deactivate", new { })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await (await ownerClient.GetAsync("/api/v1/host/promotions")).ReadAsync<List<PromotionDto>>()).ShouldContain(p => p.Id == promo.Id && !p.IsActive);
    }

    [Fact]
    public async Task Platform_promotions_are_admin_only_and_apply_to_every_listing()
    {
        var (host, _) = await api.RegisterHostAsync();
        (await host.PostJsonAsync("/api/v1/admin/promotions", LongStay(null, 5m))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var (admin, _) = await api.RegisterAdminAsync();
        var created = await admin.PostJsonAsync("/api/v1/admin/promotions",
            new CreatePromotionRequest("Platform flash sale", "FlashSale", 7m, null, null, null,
                NextCheckIn(), NextCheckIn().AddDays(400), null, DateTimeOffset.UtcNow.AddDays(30)));
        created.StatusCode.ShouldBe(HttpStatusCode.OK);
        var promo = await created.ReadAsync<PromotionDto>();
        promo.HostId.ShouldBeNull();

        try
        {
            var (_, someHost) = await api.RegisterHostAsync();
            var propertyId = await api.CreatePublishedPropertyAsync(someHost.User.Id, price: 100m);
            var checkIn = NextCheckIn();
            var quote = await (await api.CreateClient().PostJsonAsync($"/api/v1/properties/{propertyId}/quote", new QuoteRequest(checkIn, checkIn.AddDays(2), 2, null)))
                .ReadAsync<QuoteDto>();
            quote.PromotionName.ShouldBe("Platform flash sale");
        }
        finally
        {
            // Platform-wide: switch it off so it doesn't change prices in other tests sharing this database.
            (await admin.PostJsonAsync($"/api/v1/admin/promotions/{promo.Id}/deactivate", new { })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }
    }

    [Fact]
    public async Task Invalid_promotions_are_rejected()
    {
        var (host, _) = await api.RegisterHostAsync();
        (await host.PostJsonAsync("/api/v1/host/promotions", LongStay(null, 80m))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await host.PostJsonAsync("/api/v1/host/promotions",
            new CreatePromotionRequest("Early", "EarlyBird", 10m, null, null, null, null, null, null, DateTimeOffset.UtcNow.AddDays(5))))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await host.PostJsonAsync("/api/v1/host/promotions",
            new CreatePromotionRequest("Odd", "Bogus", 10m, null, null, null, null, null, null, DateTimeOffset.UtcNow.AddDays(5))))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }
}
