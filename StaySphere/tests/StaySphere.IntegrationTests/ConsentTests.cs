using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using StaySphere.Contracts.Auth;
using StaySphere.IntegrationTests.Infrastructure;

namespace StaySphere.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class ConsentTests(ApiFactory api)
{
    [Fact]
    public async Task Registration_records_required_consents_and_optional_ones_are_off()
    {
        var (client, _) = await api.RegisterAsync();
        var consents = await (await client.GetAsync("/api/v1/me/consents")).ReadAsync<List<ConsentDto>>();

        consents.Single(c => c.Purpose == "Terms").ShouldSatisfyAllConditions(c => c.Granted.ShouldBeTrue(), c => c.Required.ShouldBeTrue());
        consents.Single(c => c.Purpose == "Privacy").Granted.ShouldBeTrue();
        consents.Single(c => c.Purpose == "MarketingEmails").ShouldSatisfyAllConditions(c => c.Granted.ShouldBeFalse(), c => c.RecordedAt.ShouldBeNull());
    }

    [Fact]
    public async Task Optional_consent_can_be_granted_and_withdrawn_and_history_is_exported()
    {
        var (client, _) = await api.RegisterAsync();
        (await (await client.PutAsJsonAsync("/api/v1/me/consents/MarketingEmails", new UpdateConsentRequest(true))).ReadAsync<ConsentDto>()).Granted.ShouldBeTrue();
        (await (await client.PutAsJsonAsync("/api/v1/me/consents/MarketingEmails", new UpdateConsentRequest(false))).ReadAsync<ConsentDto>()).Granted.ShouldBeFalse();

        var export = await (await client.GetAsync("/api/v1/me/export")).ReadAsync<JsonElement>();
        export.GetProperty("consents").EnumerateArray().Count(c => c.GetProperty("purpose").GetString() == "MarketingEmails").ShouldBe(2, "append-only history");
    }

    [Fact]
    public async Task Required_consents_cannot_be_withdrawn_and_unknown_purposes_are_404()
    {
        var (client, _) = await api.RegisterAsync();
        (await client.PutAsJsonAsync("/api/v1/me/consents/Terms", new UpdateConsentRequest(false))).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await client.PutAsJsonAsync("/api/v1/me/consents/Nonsense", new UpdateConsentRequest(true))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await api.CreateClient().GetAsync("/api/v1/me/consents")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
