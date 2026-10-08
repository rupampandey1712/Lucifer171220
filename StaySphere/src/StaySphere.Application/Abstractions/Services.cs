using StaySphere.Contracts.Search;
using StaySphere.Domain.Identity;

namespace StaySphere.Application.Abstractions;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string hash, string password);
}

public interface IJwtTokenService
{
    (string Token, DateTimeOffset ExpiresAt) CreateAccessToken(User user);
}

public sealed record EmailMessage(string To, string Subject, string HtmlBody);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

public sealed record StoredFile(string Key, string Url);

/// <summary>Blob storage abstraction (Azure Blob Storage in production, Azurite or local disk in development).</summary>
public interface IFileStorageService
{
    Task<StoredFile> UploadAsync(string container, string key, Stream content, string contentType, CancellationToken cancellationToken);
    Task<Stream?> DownloadAsync(string container, string key, CancellationToken cancellationToken);
    Task DeleteAsync(string container, string key, CancellationToken cancellationToken);
    string GetReadUrl(string container, string key);
}

public sealed record ProcessedImage(byte[] Full, byte[] Thumbnail, string ContentType, string Extension, int Width, int Height);

public interface IImageProcessor
{
    /// <summary>Validates by magic bytes and decoding, enforces limits, re-encodes (strips metadata) and creates a thumbnail.</summary>
    Task<Domain.Common.Result<ProcessedImage>> ProcessAsync(Stream input, CancellationToken cancellationToken);
}

public interface IMalwareScanner
{
    Task<bool> IsCleanAsync(byte[] content, CancellationToken cancellationToken);
}

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken);
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken);
    Task RemoveAsync(string key, CancellationToken cancellationToken);

    async Task<T> GetOrCreateAsync<T>(string key, TimeSpan ttl, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken)
    {
        var cached = await GetAsync<T>(key, cancellationToken);
        if (cached is not null) return cached;
        var value = await factory(cancellationToken);
        if (value is not null) await SetAsync(key, value, ttl, cancellationToken);
        return value;
    }
}

public interface IGeocodingService
{
    Task<IReadOnlyList<GeocodeResultDto>> SearchAsync(string query, CancellationToken cancellationToken);
}

public sealed record WeatherDay(DateOnly Date, double MinC, double MaxC, int WeatherCode, string Summary);

public sealed record WeatherResult(double Latitude, double Longitude, double? CurrentTempC, string? CurrentSummary, IReadOnlyList<WeatherDay> Daily, string Source);

public interface IWeatherService
{
    Task<WeatherResult?> GetForecastAsync(double latitude, double longitude, CancellationToken cancellationToken);
}

public sealed record ExchangeRates(string Base, DateOnly Date, IReadOnlyDictionary<string, decimal> Rates, string Source);

public interface ICurrencyService
{
    Task<ExchangeRates> GetRatesAsync(string baseCurrency, CancellationToken cancellationToken);
}

public interface ITaxRateProvider
{
    decimal GetTaxPercent(string countryCode);
}

public sealed record QuoteTokenPayload(Guid PropertyId, DateOnly CheckIn, DateOnly CheckOut, int Guests, decimal Total, string Currency,
    string? CouponCode, DateTimeOffset ExpiresAt);

/// <summary>Signs server-computed quotes so the client cannot tamper with prices between quote and booking.</summary>
public interface IQuoteTokenService
{
    string Create(QuoteTokenPayload payload);
    QuoteTokenPayload? Read(string token);
}

/// <summary>Pushes real-time events to connected clients (SignalR in the API process; no-op elsewhere).</summary>
public interface IRealtimeNotifier
{
    Task SendToUserAsync(Guid userId, string method, object payload, CancellationToken cancellationToken);
}

public interface IPresenceTracker
{
    bool IsOnline(Guid userId);
}

public sealed record OutboundMessage(Guid MessageId, string Type, string Payload, string? CorrelationId);

/// <summary>Transport for integration events (Azure Service Bus or in-memory).</summary>
public interface IMessageBus
{
    Task PublishAsync(OutboundMessage message, CancellationToken cancellationToken);
}
