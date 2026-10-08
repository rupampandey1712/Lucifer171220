using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using StaySphere.Application.Abstractions;
using StaySphere.Application.Common;

namespace StaySphere.Infrastructure.Caching;

/// <summary>
/// JSON cache-aside over IDistributedCache (Redis in docker/Azure, in-memory otherwise).
/// Fails OPEN: if Redis is down the app keeps working against SQL, with a short circuit-breaker to avoid
/// paying a timeout on every request.
/// </summary>
public sealed class DistributedCacheService(IDistributedCache cache, ILogger<DistributedCacheService> logger) : ICacheService
{
    private static long _openUntilTicks;

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken)
    {
        if (IsOpen) return default;
        try
        {
            var bytes = await cache.GetAsync(key, cancellationToken);
            return bytes is null ? default : JsonSerializer.Deserialize<T>(bytes, Json.Options);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trip(ex);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken)
    {
        if (IsOpen) return;
        try
        {
            await cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(value, Json.Options),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trip(ex);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        if (IsOpen) return;
        try
        {
            await cache.RemoveAsync(key, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trip(ex);
        }
    }

    private static bool IsOpen => DateTime.UtcNow.Ticks < Interlocked.Read(ref _openUntilTicks);

    private void Trip(Exception ex)
    {
        Interlocked.Exchange(ref _openUntilTicks, DateTime.UtcNow.AddSeconds(30).Ticks);
        logger.LogWarning(ex, "Cache unavailable; bypassing for 30s");
    }
}
