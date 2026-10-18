using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StaySphere.Contracts.Experiences;
using StaySphere.Infrastructure.Persistence;
using StaySphere.IntegrationTests.Infrastructure;

namespace StaySphere.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class ExperienceTests(ApiFactory api)
{
    private static CreateExperienceRequest Request(string city, int maxGuests = 6) => new("Sunset kayak trip",
        "Paddle along the coast at golden hour with a certified guide. All equipment included.", "Outdoors", city, "PT", "Marina gate 3",
        150, maxGuests, 60m, "EUR", null);

    private async Task<(HttpClient Host, ExperienceDetailDto Experience, ExperienceSlotDto Slot)> PublishedAsync(string city, int capacity = 6)
    {
        var (host, _) = await api.RegisterHostAsync();
        var experience = await (await host.PostJsonAsync("/api/v1/host/experiences", Request(city))).ReadAsync<ExperienceDetailDto>();
        var slot = await (await host.PostJsonAsync($"/api/v1/host/experiences/{experience.Id}/slots",
            new AddExperienceSlotRequest(DateTimeOffset.UtcNow.AddDays(5), capacity))).ReadAsync<ExperienceSlotDto>();
        (await host.PostJsonAsync($"/api/v1/host/experiences/{experience.Id}/publish", new { })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        return (host, experience, slot);
    }

    [Fact]
    public async Task Hosts_publish_guests_find_and_book_with_a_server_computed_total()
    {
        var city = $"Kayakville{Guid.NewGuid():N}"[..18];
        var (_, experience, slot) = await PublishedAsync(city);

        var found = await (await api.CreateClient().GetAsync($"/api/v1/experiences?city={city}&guests=2")).ReadAsync<List<ExperienceCardDto>>();
        found.ShouldHaveSingleItem().NextSlotAt.ShouldNotBeNull();

        var (guest, _) = await api.RegisterAsync();
        var booking = await (await guest.PostJsonAsync($"/api/v1/experiences/{experience.Id}/bookings", new BookExperienceRequest(slot.Id, 2),
            Guid.NewGuid().ToString())).ReadAsync<ExperienceBookingDto>();
        booking.Total.ShouldBe(120m);
        booking.CanCancel.ShouldBeTrue();

        var detail = await (await api.CreateClient().GetAsync($"/api/v1/experiences/{experience.Id}")).ReadAsync<ExperienceDetailDto>();
        detail.Slots.Single().Remaining.ShouldBe(4);
        (await (await guest.GetAsync("/api/v1/experience-bookings")).ReadAsync<List<ExperienceBookingDto>>()).ShouldContain(b => b.Id == booking.Id);
    }

    [Fact]
    public async Task Concurrent_bookings_never_oversell_a_session()
    {
        var (_, experience, slot) = await PublishedAsync($"Racetown{Guid.NewGuid():N}"[..16], capacity: 5);
        var guests = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => api.RegisterAsync()));

        var responses = await Task.WhenAll(guests.Select(g => g.Client.PostJsonAsync($"/api/v1/experiences/{experience.Id}/bookings",
            new BookExperienceRequest(slot.Id, 2), Guid.NewGuid().ToString())));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(2, "5 seats fit two bookings of 2");
        responses.Where(r => r.StatusCode != HttpStatusCode.Created).ShouldAllBe(r => r.StatusCode == HttpStatusCode.Conflict);
        var booked = await api.WithScopeAsync(sp => sp.GetRequiredService<AppDbContext>().ExperienceSlots.Where(s => s.Id == slot.Id).Select(s => s.Booked).SingleAsync());
        booked.ShouldBe(4);
    }

    [Fact]
    public async Task Cancelling_returns_the_seats_and_drafts_stay_private()
    {
        var (host, experience, slot) = await PublishedAsync($"Cancelton{Guid.NewGuid():N}"[..16], capacity: 2);
        var (guest, _) = await api.RegisterAsync();
        var booking = await (await guest.PostJsonAsync($"/api/v1/experiences/{experience.Id}/bookings", new BookExperienceRequest(slot.Id, 2),
            Guid.NewGuid().ToString())).ReadAsync<ExperienceBookingDto>();
        var (other, _) = await api.RegisterAsync();
        (await other.PostJsonAsync($"/api/v1/experiences/{experience.Id}/bookings", new BookExperienceRequest(slot.Id, 1), Guid.NewGuid().ToString()))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);

        (await other.PostJsonAsync($"/api/v1/experience-bookings/{booking.Id}/cancel", new { })).StatusCode.ShouldBe(HttpStatusCode.NotFound, "not theirs");
        (await (await guest.PostJsonAsync($"/api/v1/experience-bookings/{booking.Id}/cancel", new { })).ReadAsync<ExperienceBookingDto>()).Status.ShouldBe("Cancelled");
        (await other.PostJsonAsync($"/api/v1/experiences/{experience.Id}/bookings", new BookExperienceRequest(slot.Id, 1), Guid.NewGuid().ToString()))
            .StatusCode.ShouldBe(HttpStatusCode.Created, "the seats went back on sale");

        var draft = await (await host.PostJsonAsync("/api/v1/host/experiences", Request("Draftville"))).ReadAsync<ExperienceDetailDto>();
        (await api.CreateClient().GetAsync($"/api/v1/experiences/{draft.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await host.GetAsync($"/api/v1/experiences/{draft.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK, "the owner can preview it");
        (await guest.PostJsonAsync("/api/v1/host/experiences", Request("Nope"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden, "guests can't host");
    }

    [Fact]
    public async Task Everything_is_404_while_the_feature_flag_is_off()
    {
        using var off = api.WithWebHostBuilder(b => b.UseSetting("FeatureManagement:Experiences", "false"));
        (await off.CreateClient().GetAsync("/api/v1/experiences")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
