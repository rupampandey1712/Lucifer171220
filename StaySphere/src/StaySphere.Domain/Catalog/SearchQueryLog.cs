using StaySphere.Domain.Common;

namespace StaySphere.Domain.Catalog;

/// <summary>
/// One anonymised search (no user id, IP or exact dates) for demand insights: top destinations and searches with no results.
/// Kept for <see cref="Retention"/> and then deleted.
/// </summary>
public sealed class SearchQueryLog : Entity
{
    public const int MaxLocationLength = 100;
    public static readonly TimeSpan Retention = TimeSpan.FromDays(90);

    private SearchQueryLog() { }

    /// <summary>Lower-cased, trimmed destination text, or null for map-only searches.</summary>
    public string? Location { get; private set; }
    public int? Guests { get; private set; }
    public int? Nights { get; private set; }
    public int ResultCount { get; private set; }
    public DateTimeOffset SearchedAt { get; private set; }

    public static SearchQueryLog Record(string? location, int? guests, int? nights, int resultCount, DateTimeOffset now)
    {
        var normalized = string.IsNullOrWhiteSpace(location) ? null : string.Join(' ', location.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (normalized?.Length > MaxLocationLength) normalized = normalized[..MaxLocationLength];
        return new SearchQueryLog
        {
            Location = normalized,
            Guests = guests is > 0 ? Math.Min(guests.Value, 50) : null,
            Nights = nights is > 0 ? Math.Min(nights.Value, 365) : null,
            ResultCount = resultCount,
            SearchedAt = now,
        };
    }
}
