using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StaySphere.Application.Abstractions;
using StaySphere.Contracts.Search;

namespace StaySphere.Infrastructure.External;

public sealed class ExternalServicesOptions
{
    public const string Section = "ExternalServices";
    /// <summary>Mock (no network) or Live (OpenStreetMap Nominatim, Open-Meteo, Frankfurter).</summary>
    public string Mode { get; set; } = "Mock";
    public string NominatimBaseUrl { get; set; } = "https://nominatim.openstreetmap.org/";
    public string OpenMeteoBaseUrl { get; set; } = "https://api.open-meteo.com/";
    public string FrankfurterBaseUrl { get; set; } = "https://api.frankfurter.app/";
    /// <summary>Nominatim's usage policy requires an identifying User-Agent and ≤ 1 request/second.</summary>
    public string UserAgent { get; set; } = "StaySphere/1.0 (dev; contact: dev@staysphere.local)";
    public Dictionary<string, decimal> TaxRates { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public decimal DefaultTaxPercent { get; set; } = 10m;
}

/// <summary>OpenStreetMap Nominatim with caching (30 days) and a process-wide 1 req/s throttle.</summary>
public sealed class NominatimGeocodingService(HttpClient http, ICacheService cache, ILogger<NominatimGeocodingService> logger) : IGeocodingService
{
    private static readonly SemaphoreSlim Throttle = new(1, 1);
    private static DateTime _lastCall = DateTime.MinValue;

    public async Task<IReadOnlyList<GeocodeResultDto>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var q = query.Trim().ToLowerInvariant();
        if (q.Length < 2) return [];
        return await cache.GetOrCreateAsync<IReadOnlyList<GeocodeResultDto>>($"geocode:{q}", TimeSpan.FromDays(30), async ct =>
        {
            await Throttle.WaitAsync(ct);
            try
            {
                var wait = _lastCall.AddSeconds(1.1) - DateTime.UtcNow;
                if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
                _lastCall = DateTime.UtcNow;
                var items = await http.GetFromJsonAsync<List<NominatimItem>>(
                    $"search?format=jsonv2&addressdetails=1&limit=5&q={Uri.EscapeDataString(q)}", ct) ?? [];
                return items.Select(i => new GeocodeResultDto(i.DisplayName,
                    double.Parse(i.Lat, CultureInfo.InvariantCulture), double.Parse(i.Lon, CultureInfo.InvariantCulture),
                    i.Address?.City ?? i.Address?.Town ?? i.Address?.Village, i.Address?.Country, i.Address?.CountryCode?.ToUpperInvariant())).ToList();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                logger.LogWarning(ex, "Geocoding failed; returning no results");
                return [];
            }
            finally
            {
                Throttle.Release();
            }
        }, cancellationToken);
    }

    private sealed record NominatimItem(
        [property: JsonPropertyName("display_name")] string DisplayName, string Lat, string Lon, NominatimAddress? Address);

    private sealed record NominatimAddress(string? City, string? Town, string? Village, string? Country,
        [property: JsonPropertyName("country_code")] string? CountryCode);
}

public sealed class MockGeocodingService : IGeocodingService
{
    private static readonly GeocodeResultDto[] Places =
    [
        new("London, Greater London, England, United Kingdom", 51.5074, -0.1278, "London", "United Kingdom", "GB"),
        new("Paris, Île-de-France, France", 48.8566, 2.3522, "Paris", "France", "FR"),
        new("Lisbon, Portugal", 38.7223, -9.1393, "Lisbon", "Portugal", "PT"),
        new("Barcelona, Catalonia, Spain", 41.3874, 2.1686, "Barcelona", "Spain", "ES"),
        new("Rome, Lazio, Italy", 41.9028, 12.4964, "Rome", "Italy", "IT"),
        new("Amsterdam, North Holland, Netherlands", 52.3676, 4.9041, "Amsterdam", "Netherlands", "NL"),
        new("New York, United States", 40.7128, -74.0060, "New York", "United States", "US"),
        new("Goa, India", 15.2993, 74.1240, "Goa", "India", "IN"),
        new("Tokyo, Japan", 35.6762, 139.6503, "Tokyo", "Japan", "JP"),
        new("Bali, Indonesia", -8.3405, 115.0920, "Bali", "Indonesia", "ID"),
        new("Cape Town, South Africa", -33.9249, 18.4241, "Cape Town", "South Africa", "ZA"),
        new("Sydney, Australia", -33.8688, 151.2093, "Sydney", "Australia", "AU"),
    ];

    public Task<IReadOnlyList<GeocodeResultDto>> SearchAsync(string query, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GeocodeResultDto>>(Places.Where(p => p.DisplayName.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)).ToList());
}

/// <summary>Open-Meteo (free, no key). Cached 30 minutes per ~1km grid cell; never called on every page view.</summary>
public sealed class OpenMeteoWeatherService(HttpClient http, ICacheService cache, ILogger<OpenMeteoWeatherService> logger) : IWeatherService
{
    public async Task<WeatherResult?> GetForecastAsync(double latitude, double longitude, CancellationToken cancellationToken)
    {
        var lat = Math.Round(latitude, 2);
        var lon = Math.Round(longitude, 2);
        return await cache.GetOrCreateAsync<WeatherResult?>(string.Create(CultureInfo.InvariantCulture, $"weather:{lat}:{lon}"), TimeSpan.FromMinutes(30), async ct =>
        {
            try
            {
                var url = string.Create(CultureInfo.InvariantCulture,
                    $"v1/forecast?latitude={lat}&longitude={lon}&current=temperature_2m,weather_code&daily=weather_code,temperature_2m_max,temperature_2m_min&forecast_days=7&timezone=auto");
                var r = await http.GetFromJsonAsync<OpenMeteoResponse>(url, ct);
                if (r?.Daily is null) return null;
                var days = r.Daily.Time.Select((t, i) => new WeatherDay(DateOnly.Parse(t, CultureInfo.InvariantCulture), r.Daily.Min[i], r.Daily.Max[i],
                    r.Daily.Code[i], WeatherCodes.Describe(r.Daily.Code[i]))).ToList();
                return new WeatherResult(lat, lon, r.Current?.Temperature, r.Current is null ? null : WeatherCodes.Describe(r.Current.Code), days, "open-meteo");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                logger.LogWarning(ex, "Weather lookup failed; widget will be hidden");
                return null;
            }
        }, cancellationToken);
    }

    private sealed record OpenMeteoResponse(OpenMeteoCurrent? Current, OpenMeteoDaily? Daily);

    private sealed record OpenMeteoCurrent([property: JsonPropertyName("temperature_2m")] double Temperature, [property: JsonPropertyName("weather_code")] int Code);

    private sealed record OpenMeteoDaily(
        List<string> Time,
        [property: JsonPropertyName("weather_code")] List<int> Code,
        [property: JsonPropertyName("temperature_2m_max")] List<double> Max,
        [property: JsonPropertyName("temperature_2m_min")] List<double> Min);
}

public sealed class MockWeatherService(TimeProvider clock) : IWeatherService
{
    public Task<WeatherResult?> GetForecastAsync(double latitude, double longitude, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var baseTemp = 28 - Math.Abs(latitude) / 3;
        var codes = new[] { 0, 1, 2, 3, 61, 2, 0 };
        var days = Enumerable.Range(0, 7).Select(i => new WeatherDay(today.AddDays(i), Math.Round(baseTemp - 6 + i % 3, 1),
            Math.Round(baseTemp + i % 4, 1), codes[i], WeatherCodes.Describe(codes[i]))).ToList();
        return Task.FromResult<WeatherResult?>(new WeatherResult(latitude, longitude, Math.Round(baseTemp, 1), "Mainly clear", days, "mock"));
    }
}

public static class WeatherCodes
{
    public static string Describe(int code) => code switch
    {
        0 => "Clear sky",
        1 => "Mainly clear",
        2 => "Partly cloudy",
        3 => "Overcast",
        45 or 48 => "Fog",
        >= 51 and <= 57 => "Drizzle",
        >= 61 and <= 67 => "Rain",
        >= 71 and <= 77 => "Snow",
        >= 80 and <= 82 => "Rain showers",
        >= 95 => "Thunderstorm",
        _ => "Mixed",
    };
}

/// <summary>Frankfurter (ECB reference rates, free, no key). Cached 6 hours.</summary>
public sealed class FrankfurterCurrencyService(HttpClient http, ICacheService cache, MockCurrencyService fallback, ILogger<FrankfurterCurrencyService> logger) : ICurrencyService
{
    public async Task<ExchangeRates> GetRatesAsync(string baseCurrency, CancellationToken cancellationToken)
    {
        var b = baseCurrency.Trim().ToUpperInvariant();
        // Only ISO-4217-shaped codes reach the URL and the cache key (no query injection, no unbounded cache keys).
        if (b.Length != 3 || !b.All(char.IsAsciiLetterUpper)) return await fallback.GetRatesAsync("USD", cancellationToken);
        return await cache.GetOrCreateAsync($"fx:{b}", TimeSpan.FromHours(6), async ct =>
        {
            try
            {
                var r = await http.GetFromJsonAsync<FrankfurterResponse>($"latest?from={b}", ct);
                if (r is null) return await fallback.GetRatesAsync(b, ct);
                var rates = new Dictionary<string, decimal>(r.Rates) { [b] = 1m };
                return new ExchangeRates(b, DateOnly.Parse(r.Date, CultureInfo.InvariantCulture), rates, "frankfurter");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                logger.LogWarning(ex, "FX lookup failed; using static fallback rates");
                return await fallback.GetRatesAsync(b, ct);
            }
        }, cancellationToken);
    }

    private sealed record FrankfurterResponse(string Date, Dictionary<string, decimal> Rates);
}

public sealed class MockCurrencyService(TimeProvider clock) : ICurrencyService
{
    // Static reference rates per 1 USD (approximate; for development only).
    private static readonly Dictionary<string, decimal> PerUsd = new()
    {
        ["USD"] = 1m, ["EUR"] = 0.92m, ["GBP"] = 0.79m, ["INR"] = 83.2m, ["JPY"] = 150m, ["AUD"] = 1.52m, ["CAD"] = 1.36m,
        ["CHF"] = 0.88m, ["IDR"] = 15600m, ["ZAR"] = 18.4m, ["BRL"] = 5.0m, ["MXN"] = 17.1m, ["THB"] = 35.8m,
    };

    public Task<ExchangeRates> GetRatesAsync(string baseCurrency, CancellationToken cancellationToken)
    {
        var b = baseCurrency.ToUpperInvariant();
        var basePerUsd = PerUsd.GetValueOrDefault(b, 1m);
        var rates = PerUsd.ToDictionary(kv => kv.Key, kv => decimal.Round(kv.Value / basePerUsd, 6));
        return Task.FromResult(new ExchangeRates(b, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), rates, "static"));
    }
}

public sealed class ConfigTaxRateProvider(IOptions<ExternalServicesOptions> options) : ITaxRateProvider
{
    public decimal GetTaxPercent(string countryCode) =>
        options.Value.TaxRates.TryGetValue(countryCode, out var rate) ? rate : options.Value.DefaultTaxPercent;
}
