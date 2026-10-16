using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StaySphere.Infrastructure.Caching;
using StaySphere.Infrastructure.External;

namespace StaySphere.IntegrationTests;

/// <summary>
/// Serves the WireMock mapping files used by docker-compose.wiremock.yml and runs the REAL live clients against them,
/// so the stubs and the parsers can't drift apart (and the profile is known to work before anyone starts it).
/// </summary>
public sealed class WireMockStubTests
{
    private static readonly string MappingsDir = FindMappings();

    private static string FindMappings()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "StaySphere.sln"))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("Repository root not found."), "infrastructure", "wiremock", "mappings");
    }

    /// <summary>Minimal WireMock: matches GET + urlPath and returns the mapping's jsonBody.</summary>
    private sealed class StubServer : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _bodies = Directory.GetFiles(MappingsDir, "*.json")
            .Select(f => JsonNode.Parse(File.ReadAllText(f))!)
            .ToDictionary(m => (string)m["request"]!["urlPath"]!, m => m["response"]!["jsonBody"]!.ToJsonString());

        public IReadOnlyCollection<string> Paths => _bodies.Keys;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_bodies.TryGetValue(request.RequestUri!.AbsolutePath, out var body)
                ? new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
    }

    private static DistributedCacheService Cache() =>
        new(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())), NullLogger<DistributedCacheService>.Instance);

    private static HttpClient Client(StubServer server, string prefix) => new(server, disposeHandler: false) { BaseAddress = new Uri($"http://wiremock:8080/{prefix}/") };

    [Fact]
    public void Every_mapping_is_valid_wiremock_json()
    {
        var files = Directory.GetFiles(MappingsDir, "*.json");
        files.Length.ShouldBe(4);
        foreach (var file in files)
        {
            var mapping = JsonNode.Parse(File.ReadAllText(file))!;
            ((string)mapping["request"]!["method"]!).ShouldBe("GET");
            ((int)mapping["response"]!["status"]!).ShouldBe(200);
            mapping["response"]!["jsonBody"].ShouldNotBeNull(file);
        }
    }

    [Fact]
    public async Task Live_clients_parse_every_stub()
    {
        var server = new StubServer();

        var geo = await new NominatimGeocodingService(Client(server, "nominatim"), Cache(), NullLogger<NominatimGeocodingService>.Instance)
            .SearchAsync("Lisbon", CancellationToken.None);
        geo.ShouldContain(r => r.City == "Lisbon" && r.CountryCode == "PT");

        var weather = await new OpenMeteoWeatherService(Client(server, "open-meteo"), Cache(), NullLogger<OpenMeteoWeatherService>.Instance)
            .GetForecastAsync(38.7, -9.1, CancellationToken.None);
        weather.ShouldNotBeNull();
        weather.Daily.Count.ShouldBe(7);
        weather.CurrentTempC.ShouldBe(18.4);

        var rates = await new FrankfurterCurrencyService(Client(server, "frankfurter"), Cache(), new MockCurrencyService(TimeProvider.System),
            NullLogger<FrankfurterCurrencyService>.Instance).GetRatesAsync("EUR", CancellationToken.None);
        rates.Rates["USD"].ShouldBe(1.09m);
        rates.Source.ShouldNotContain("mock", Case.Insensitive);

        var countries = await new RestCountriesService(Client(server, "restcountries"), Cache(), new MockCountryService(),
            NullLogger<RestCountriesService>.Instance).GetAllAsync(CancellationToken.None);
        countries.Count.ShouldBe(57, "the live list, not the built-in fallback");
        countries.ShouldContain(c => c.Code == "PT" && c.Currencies.Contains("EUR"));
    }
}
