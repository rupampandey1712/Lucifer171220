using System.Net;
using StaySphere.Contracts.Ai;
using StaySphere.Contracts.Booking;
using StaySphere.Contracts.Payments;
using StaySphere.IntegrationTests.Infrastructure;

namespace StaySphere.IntegrationTests;

/// <summary>The specialised agents: who may use each one, what they can see, and the confirmation gate for cancellations.</summary>
[Collection(ApiCollection.Name)]
public sealed class AiAgentsTests(ApiFactory api)
{
    private static int _offset = 900;

    private static (DateOnly, DateOnly) NextDates()
    {
        var start = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(Interlocked.Add(ref _offset, 10));
        return (start, start.AddDays(3));
    }

    private static Task<HttpResponseMessage> AskAsync(HttpClient client, string agent, string message, Guid? conversation = null) =>
        client.PostJsonAsync("/api/v1/ai/messages", new AssistantMessageRequest(conversation, message, agent));

    private static async Task<ReservationDto> BookAsync(HttpClient guest, Guid propertyId, DateOnly ci, DateOnly co)
    {
        var quote = await (await guest.PostJsonAsync($"/api/v1/properties/{propertyId}/quote", new QuoteRequest(ci, co, 2, null))).ReadAsync<QuoteDto>();
        var held = await (await guest.PostJsonAsync("/api/v1/reservations", new CreateReservationRequest(propertyId, ci, co, 2, quote.QuoteToken, null),
            Guid.NewGuid().ToString())).ReadAsync<ReservationDto>();
        (await guest.PostJsonAsync($"/api/v1/reservations/{held.Id}/payment", new PayReservationRequest("tok_4242"), Guid.NewGuid().ToString()))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        return await (await guest.GetAsync($"/api/v1/reservations/{held.Id}")).ReadAsync<ReservationDto>();
    }

    [Fact]
    public async Task Each_role_sees_only_the_agents_it_may_use()
    {
        static async Task<string[]> KeysAsync(HttpClient c) =>
            (await (await c.GetAsync("/api/v1/ai/agents")).ReadAsync<List<AgentInfoDto>>()).Select(a => a.Key).Order().ToArray();

        (await KeysAsync(api.CreateClient())).ShouldBe(["recommendation", "travel"]);
        (await KeysAsync((await api.RegisterAsync()).Client)).ShouldBe(["recommendation", "support", "travel"]);
        (await KeysAsync((await api.RegisterHostAsync()).Client)).ShouldBe(["host", "recommendation", "support", "travel"]);
        (await KeysAsync((await api.RegisterAdminAsync()).Client)).ShouldBe(["admin", "host", "recommendation", "support", "travel"]);
    }

    [Fact]
    public async Task Privileged_agents_are_refused_server_side()
    {
        var (guest, _) = await api.RegisterAsync();
        (await AskAsync(guest, "host", "show my listings")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await AskAsync(guest, "admin", "platform overview")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await AskAsync(api.CreateClient(), "support", "my trips")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await AskAsync(guest, "nonsense", "hi")).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Host_agent_lists_only_the_hosts_own_pending_requests()
    {
        var (hostClient, host) = await api.RegisterHostAsync();
        var city = "Hostville" + Guid.NewGuid().ToString("N")[..6];
        var propertyId = await api.CreatePublishedPropertyAsync(host.User.Id, city: city, instantBook: false);
        var (guest, _) = await api.RegisterAsync();
        var (ci, co) = NextDates();
        (await BookAsync(guest, propertyId, ci, co)).Status.ShouldBe("AwaitingApproval");

        var reply = await (await AskAsync(hostClient, "host", "do I have any booking requests?")).ReadAsync<AssistantReplyDto>();

        reply.Agent.ShouldBe("host");
        reply.ToolsUsed.ShouldBe([nameof(Application.Ai.AssistantToolbox.GetHostReservations)]);
        reply.Reply.ShouldContain(city);

        var (otherHost, _) = await api.RegisterHostAsync();
        var other = await (await AskAsync(otherHost, "host", "do I have any booking requests?")).ReadAsync<AssistantReplyDto>();
        other.Reply.ShouldNotContain(city);
    }

    [Fact]
    public async Task Support_agent_only_cancels_after_the_user_confirms()
    {
        var (_, host) = await api.RegisterHostAsync();
        var propertyId = await api.CreatePublishedPropertyAsync(host.User.Id);
        var (guest, _) = await api.RegisterAsync();
        var (ci, co) = NextDates();
        var booking = await BookAsync(guest, propertyId, ci, co);
        booking.Status.ShouldBe("Confirmed");

        var refund = await (await AskAsync(guest, "support", "what refund would I get?")).ReadAsync<AssistantReplyDto>();
        refund.Reply.ShouldContain("back");
        refund.PendingAction.ShouldBeNull("explaining a refund must not create an action");

        var reply = await (await AskAsync(guest, "support", "please cancel my trip")).ReadAsync<AssistantReplyDto>();
        reply.PendingAction.ShouldNotBeNull();
        reply.PendingAction.Kind.ShouldBe("reservation_cancellation");
        (await (await guest.GetAsync($"/api/v1/reservations/{booking.Id}")).ReadAsync<ReservationDto>()).Status.ShouldBe("Confirmed", "nothing changes before confirmation");

        // Someone else can't confirm it.
        var (stranger, _) = await api.RegisterAsync();
        (await stranger.PostJsonAsync($"/api/v1/ai/actions/{reply.PendingAction.Id}/confirm", new ConfirmActionRequest(true)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var confirm = await (await guest.PostJsonAsync($"/api/v1/ai/actions/{reply.PendingAction.Id}/confirm", new ConfirmActionRequest(true)))
            .ReadAsync<ConfirmActionResultDto>();
        confirm.Status.ShouldBe("Approved");
        (await (await guest.GetAsync($"/api/v1/reservations/{booking.Id}")).ReadAsync<ReservationDto>()).Status.ShouldBeOneOf("Cancelled", "RefundPending", "Refunded");

        (await guest.PostJsonAsync($"/api/v1/ai/actions/{reply.PendingAction.Id}/confirm", new ConfirmActionRequest(true)))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict, "an action runs at most once");
    }

    [Fact]
    public async Task Admin_agent_answers_with_platform_metrics()
    {
        var (admin, _) = await api.RegisterAdminAsync();
        var reply = await (await AskAsync(admin, "admin", "give me a platform overview")).ReadAsync<AssistantReplyDto>();

        reply.ToolsUsed.ShouldBe([nameof(Application.Ai.AssistantToolbox.GetPlatformStats)]);
        reply.Reply.ShouldContain("users");
        reply.Reply.ShouldContain("Cancellation rate");
    }

    [Fact]
    public async Task Recommendation_agent_suggests_similar_stays()
    {
        var (_, host) = await api.RegisterHostAsync();
        var city = "Recoville" + Guid.NewGuid().ToString("N")[..6];
        await api.CreatePublishedPropertyAsync(host.User.Id, 100m, city);
        await api.CreatePublishedPropertyAsync(host.User.Id, 110m, city);
        var client = api.CreateClient();

        var first = await (await AskAsync(client, "recommendation", $"places in {city}")).ReadAsync<AssistantReplyDto>();
        first.Properties.Count.ShouldBe(2);
        var similar = await (await AskAsync(client, "recommendation", "show me more like the first one", first.ConversationId)).ReadAsync<AssistantReplyDto>();

        similar.ToolsUsed.ShouldContain(nameof(Application.Ai.AssistantToolbox.RecommendSimilar));
        similar.Reply.ShouldContain("you may also like");
    }
}
