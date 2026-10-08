using System.Net;
using StaySphere.Contracts;
using StaySphere.Contracts.Ai;
using StaySphere.Contracts.Properties;
using StaySphere.IntegrationTests.Infrastructure;

namespace StaySphere.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class PlatformTests(ApiFactory api)
{
    [Fact]
    public async Task Health_endpoints_report_status()
    {
        var client = api.CreateClient();
        (await client.GetAsync("/health/live")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Search_finds_published_listings_by_city_and_filters()
    {
        var (_, host) = await api.RegisterHostAsync();
        var city = "Searchton" + Guid.NewGuid().ToString("N")[..6];
        await api.CreatePublishedPropertyAsync(host.User.Id, 90m, city);
        await api.CreatePublishedPropertyAsync(host.User.Id, 300m, city);
        var client = api.CreateClient();

        var all = await (await client.GetAsync($"/api/v1/search/properties?location={city}")).ReadAsync<PagedResult<PropertyCardDto>>();
        var cheap = await (await client.GetAsync($"/api/v1/search/properties?location={city}&maxPrice=100&amenities=wifi")).ReadAsync<PagedResult<PropertyCardDto>>();

        all.TotalCount.ShouldBe(2);
        cheap.Items.ShouldHaveSingleItem().NightlyPrice.ShouldBe(90m);
    }

    [Fact]
    public async Task Unknown_resources_return_problem_details_with_trace_id()
    {
        var response = await api.CreateClient().GetAsync($"/api/v1/properties/{Guid.NewGuid()}");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        (await response.Content.ReadAsStringAsync()).ShouldContain("traceId");
    }

    [Fact]
    public async Task Admin_endpoints_are_forbidden_to_normal_users()
    {
        var (client, _) = await api.RegisterAsync();
        (await client.GetAsync("/api/v1/admin/dashboard")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await api.CreateClient().GetAsync("/api/v1/admin/dashboard")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Ai_assistant_proposes_but_never_books_without_explicit_confirmation()
    {
        var (_, host) = await api.RegisterHostAsync();
        var city = "Aiville" + Guid.NewGuid().ToString("N")[..5];
        await api.CreatePublishedPropertyAsync(host.User.Id, 100m, city);
        var (client, guest) = await api.RegisterAsync();

        var search = await (await client.PostJsonAsync("/api/v1/ai/messages", new AssistantMessageRequest(null, $"Find me a place in {city} for 2 guests"))).ReadAsync<AssistantReplyDto>();
        search.Properties.ShouldNotBeEmpty();
        search.ToolsUsed.ShouldContain("SearchProperties");

        var checkIn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(400);
        var book = await (await client.PostJsonAsync("/api/v1/ai/messages",
            new AssistantMessageRequest(search.ConversationId, $"Book the first one from {checkIn:yyyy-MM-dd} to {checkIn.AddDays(2):yyyy-MM-dd} for 2 guests"))).ReadAsync<AssistantReplyDto>();
        book.PendingAction.ShouldNotBeNull();

        var before = await (await client.GetAsync("/api/v1/reservations")).ReadAsync<PagedResult<Contracts.Booking.ReservationDto>>();
        before.TotalCount.ShouldBe(0, "proposal alone must not create a reservation");

        var (stranger, _) = await api.RegisterAsync();
        (await stranger.PostJsonAsync($"/api/v1/ai/actions/{book.PendingAction!.Id}/confirm", new ConfirmActionRequest(true)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound, "only the owner can confirm");

        var confirm = await (await client.PostJsonAsync($"/api/v1/ai/actions/{book.PendingAction.Id}/confirm", new ConfirmActionRequest(true))).ReadAsync<ConfirmActionResultDto>();
        confirm.ReservationId.ShouldNotBeNull();
        (await client.PostJsonAsync($"/api/v1/ai/actions/{book.PendingAction.Id}/confirm", new ConfirmActionRequest(true)))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict, "an action executes once");
        _ = guest;
    }
}
