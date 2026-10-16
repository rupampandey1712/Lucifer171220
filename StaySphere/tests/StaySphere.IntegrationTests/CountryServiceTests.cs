using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StaySphere.Contracts.Search;
using StaySphere.Infrastructure.Caching;
using StaySphere.Infrastructure.External;
using StaySphere.IntegrationTests.Infrastructure;

namespace StaySphere.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class CountryServiceTests(ApiFactory api)
{
    private sealed class Stub(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(respond());
        }
    }

    private static (RestCountriesService Service, Stub Stub) Live(Func<HttpResponseMessage> respond)
    {
        var stub = new Stub(respond);
        var cache = new DistributedCacheService(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())), NullLogger<DistributedCacheService>.Instance);
        var http = new HttpClient(stub) { BaseAddress = new Uri("https://restcountries.test/") };
        return (new RestCountriesService(http, cache, new MockCountryService(), NullLogger<RestCountriesService>.Instance), stub);
    }

    private static HttpResponseMessage Json(object body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Countries_endpoint_works_offline_in_mock_mode()
    {
        var countries = await (await api.CreateClient().GetAsync("/api/v1/countries")).ReadAsync<List<CountryDto>>();
        countries.ShouldContain(c => c.Code == "PT" && c.Name == "Portugal" && c.Currencies.Contains("EUR"));
    }

    [Fact]
    public async Task Live_service_maps_sorts_and_caches_the_response()
    {
        var payload = Enumerable.Range(0, 60).Select(i => new
        {
            cca2 = $"{(char)('A' + i / 26)}{(char)('A' + i % 26)}".ToLowerInvariant(),
            name = new { common = $"Country {59 - i:00}" },
            flag = "🏳",
            currencies = new Dictionary<string, object> { ["EUR"] = new { name = "Euro" } },
        }).ToList();
        var (service, stub) = Live(() => Json(payload));

        var first = await service.GetAllAsync(CancellationToken.None);
        await service.GetAllAsync(CancellationToken.None);

        first.Count.ShouldBe(60);
        first[0].Name.ShouldBe("Country 00");
        first.ShouldAllBe(c => c.Code.Length == 2 && c.Code == c.Code.ToUpperInvariant());
        stub.Calls.ShouldBe(1, "cached for 7 days");
    }

    [Fact]
    public async Task Outages_and_suspiciously_small_responses_fall_back_without_being_cached()
    {
        var (broken, _) = Live(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        (await broken.GetAllAsync(CancellationToken.None)).ShouldContain(c => c.Code == "PT");

        var (tiny, stub) = Live(() => Json(new[] { new { cca2 = "pt", name = new { common = "Portugal" } } }));
        (await tiny.GetAllAsync(CancellationToken.None)).Count.ShouldBeGreaterThan(40, "the built-in list, not a 1-country response");
        await tiny.GetAllAsync(CancellationToken.None);
        stub.Calls.ShouldBe(2, "a bad response is not cached");
    }
}
