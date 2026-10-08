using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StaySphere.Application.Abstractions;
using StaySphere.Domain.Platform;

namespace StaySphere.Application.Common;

public static class TokenHasher
{
    public static string GenerateToken(int bytes = 32) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

public static class CacheKeys
{
    public static string Property(Guid id) => $"property:{id}";
    public static string Search(string hash) => $"search:{hash}";
    public static string Availability(Guid id) => $"availability:{id}";
    public const string Amenities = "amenities:all";
    public const string Destinations = "destinations:featured";
}

public interface IAuditLogger
{
    /// <summary>Adds an audit row to the current unit of work (saved atomically with the business change).</summary>
    void Record(string action, string entityType, string? entityId, object? details = null, Guid? actorOverride = null);
}

public sealed class AuditLogger(IAppDbContext db, ICurrentUser currentUser, TimeProvider clock) : IAuditLogger
{
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase) { "password", "token", "card", "secret" };

    public void Record(string action, string entityType, string? entityId, object? details = null, Guid? actorOverride = null)
    {
        string? json = null;
        if (details is not null)
        {
            var element = JsonSerializer.SerializeToElement(details, Json.Options);
            json = element.ValueKind == JsonValueKind.Object
                ? JsonSerializer.Serialize(element.EnumerateObject()
                    .Where(p => !SensitiveKeys.Any(k => p.Name.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    .ToDictionary(p => p.Name, p => p.Value), Json.Options)
                : element.GetRawText();
        }

        db.AuditLogs.Add(AuditLog.Create(actorOverride ?? currentUser.UserId, action, entityType, entityId, json,
            currentUser.IpAddress, currentUser.CorrelationId, clock.GetUtcNow()));
    }
}

public static class Paging
{
    public static (int Page, int PageSize) Normalize(int page, int pageSize, int max = 50) =>
        (Math.Max(1, page), Math.Clamp(pageSize, 1, max));
}
