using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StaySphere.Application.Abstractions;
using StaySphere.Application.Common;
using StaySphere.Contracts;
using StaySphere.Contracts.Properties;
using StaySphere.Contracts.Search;
using StaySphere.Domain.Catalog;

namespace StaySphere.Application.Search;

/// <summary>
/// Search engine port. The default implementation queries the relational store; an Azure AI Search /
/// OpenSearch provider can replace it without touching callers.
/// </summary>
public interface ISearchProvider
{
    Task<PagedResult<PropertyCardDto>> SearchAsync(SearchPropertiesQuery query, CancellationToken ct);
}

public interface ISearchService
{
    Task<PagedResult<PropertyCardDto>> SearchAsync(SearchPropertiesQuery query, CancellationToken ct);
    Task<IReadOnlyList<DestinationDto>> GetFeaturedDestinationsAsync(CancellationToken ct);
    Task<IReadOnlyList<string>> SuggestAsync(string prefix, CancellationToken ct);
    Task<IReadOnlyList<PropertyCardDto>> SimilarAsync(Guid propertyId, CancellationToken ct);
}

public sealed class SearchService(ISearchProvider provider, IAppDbContext db, ICacheService cache) : ISearchService
{
    public Task<PagedResult<PropertyCardDto>> SearchAsync(SearchPropertiesQuery query, CancellationToken ct)
    {
        var key = CacheKeys.Search(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(query, Json.Options))))[..32]);
        return cache.GetOrCreateAsync(key, TimeSpan.FromSeconds(60), c => provider.SearchAsync(query, c), ct);
    }

    public Task<IReadOnlyList<DestinationDto>> GetFeaturedDestinationsAsync(CancellationToken ct) =>
        cache.GetOrCreateAsync<IReadOnlyList<DestinationDto>>(CacheKeys.Destinations, TimeSpan.FromMinutes(30), async c =>
        {
            var rows = await db.Properties.AsNoTracking()
                .Where(p => p.Status == PropertyStatus.Published && p.DeletedAt == null && p.Address != null)
                .GroupBy(p => new { p.Address!.City, p.Address.Country })
                .Select(g => new
                {
                    g.Key.City, g.Key.Country, Count = g.Count(),
                    Lat = g.Average(p => p.Latitude ?? 0), Lng = g.Average(p => p.Longitude ?? 0),
                    Image = g.OrderByDescending(p => p.RatingAverage).SelectMany(p => p.Images.OrderBy(i => i.SortOrder).Select(i => i.Url)).FirstOrDefault(),
                })
                .OrderByDescending(x => x.Count).Take(16).ToListAsync(c);
            return rows.Select(r => new DestinationDto(r.City, r.Country, r.Count, r.Lat, r.Lng, r.Image)).ToList();
        }, ct);

    public async Task<IReadOnlyList<string>> SuggestAsync(string prefix, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(prefix) || prefix.Length < 2) return [];
        var p = prefix.Trim();
        return await db.Properties.AsNoTracking()
            .Where(x => x.Status == PropertyStatus.Published && x.DeletedAt == null && x.Address != null &&
                        (x.Address.City.StartsWith(p) || x.Address.Country.StartsWith(p)))
            .Select(x => x.Address!.City + ", " + x.Address.Country)
            .Distinct().OrderBy(x => x).Take(8).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PropertyCardDto>> SimilarAsync(Guid propertyId, CancellationToken ct)
    {
        var source = await db.Properties.AsNoTracking().Where(p => p.Id == propertyId)
            .Select(p => new { p.Latitude, p.Longitude, p.BasePrice, p.MaxGuests }).FirstOrDefaultAsync(ct);
        if (source?.Latitude is null) return [];
        var result = await provider.SearchAsync(new SearchPropertiesQuery
        {
            Latitude = source.Latitude,
            Longitude = source.Longitude,
            RadiusKm = 50,
            MinPrice = source.BasePrice * 0.5m,
            MaxPrice = source.BasePrice * 1.8m,
            Sort = SearchSort.Distance,
            PageSize = 7,
        }, ct);
        return result.Items.Where(i => i.Id != propertyId).Take(6).ToList();
    }
}

public sealed class DatabaseSearchProvider(IAppDbContext db) : ISearchProvider
{
    private const double KmPerDegree = 111.32;

    public async Task<PagedResult<PropertyCardDto>> SearchAsync(SearchPropertiesQuery q, CancellationToken ct)
    {
        var (page, pageSize) = Paging.Normalize(TryDecodePageCursor(q.Cursor) ?? q.Page, q.PageSize);
        var query = db.Properties.AsNoTracking().Where(p => p.Status == PropertyStatus.Published && p.DeletedAt == null);

        if (!string.IsNullOrWhiteSpace(q.Location) && q.Latitude is null)
        {
            // "Lisbon, Portugal" → match on the first meaningful segment against city / region / country.
            var term = q.Location.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? q.Location.Trim();
            query = query.Where(p => p.Address != null &&
                (p.Address.City.Contains(term) || p.Address.Country.Contains(term) || (p.Address.Region != null && p.Address.Region.Contains(term))));
        }

        double? cosLat = null;
        if (q.Latitude is { } lat && q.Longitude is { } lng)
        {
            cosLat = Math.Cos(lat * Math.PI / 180);
            var radius = Math.Clamp(q.RadiusKm ?? 30, 1, 500);
            var dLat = radius / KmPerDegree;
            var dLng = radius / (KmPerDegree * Math.Max(cosLat.Value, 0.01));
            query = query.Where(p => p.Latitude >= lat - dLat && p.Latitude <= lat + dLat && p.Longitude >= lng - dLng && p.Longitude <= lng + dLng);
        }

        if (q is { South: { } s, North: { } n, West: { } w, East: { } e })
            query = query.Where(p => p.Latitude >= s && p.Latitude <= n && p.Longitude >= w && p.Longitude <= e);

        int? nights = null;
        if (q is { CheckIn: { } ci, CheckOut: { } co } && co > ci)
        {
            nights = co.DayNumber - ci.DayNumber;
            query = query.Where(p =>
                !db.ReservationNights.Any(rn => rn.PropertyId == p.Id && rn.IsActive && rn.Night >= ci && rn.Night < co) &&
                !p.BlockedDates.Any(b => b.Date >= ci && b.Date < co) &&
                p.MinNights <= co.DayNumber - ci.DayNumber);
        }

        if (q.Guests is > 0) query = query.Where(p => p.MaxGuests >= q.Guests);
        if (q.PropertyTypes is { Count: > 0 })
        {
            var types = q.PropertyTypes.Select(t => Enum.TryParse<PropertyType>(t, true, out var v) ? v : (PropertyType?)null)
                .Where(t => t is not null).Select(t => t!.Value).ToList();
            query = query.Where(p => types.Contains(p.PropertyType));
        }

        if (q.RoomType is not null && Enum.TryParse<RoomType>(q.RoomType, true, out var room)) query = query.Where(p => p.RoomType == room);
        if (q.MinPrice is { } min) query = query.Where(p => p.BasePrice >= min);
        if (q.MaxPrice is { } max) query = query.Where(p => p.BasePrice <= max);
        if (q.Bedrooms is > 0) query = query.Where(p => p.Bedrooms >= q.Bedrooms);
        if (q.Beds is > 0) query = query.Where(p => p.Beds >= q.Beds);
        if (q.Bathrooms is > 0) query = query.Where(p => p.Bathrooms >= q.Bathrooms);
        if (q.MinRating is > 0) query = query.Where(p => p.RatingAverage >= q.MinRating);
        if (q.InstantBook == true) query = query.Where(p => p.InstantBook);
        if (q.PetsAllowed == true) query = query.Where(p => p.PetsAllowed);
        foreach (var code in q.Amenities ?? [])
        {
            var c = code.ToLowerInvariant();
            query = query.Where(p => p.Amenities.Any(a => a.AmenityCode == c));
        }

        var total = await query.CountAsync(ct);

        var (ordered, useKeyset) = ApplySort(query, q, cosLat);
        if (useKeyset && TryDecodeCursor(q.Cursor, out var cursorDate, out var cursorId))
            ordered = ordered.Where(p => p.PublishedAt < cursorDate || (p.PublishedAt == cursorDate && p.Id.CompareTo(cursorId) < 0))
                .OrderByDescending(p => p.PublishedAt).ThenByDescending(p => p.Id);
        else
            ordered = (IOrderedQueryable<Property>)ordered.Skip((page - 1) * pageSize);

        var rows = await ordered.Take(pageSize)
            .Select(p => new
            {
                p.Id, p.Title, p.PropertyType, p.RoomType, City = p.Address!.City, Country = p.Address.Country,
                p.Latitude, p.Longitude, p.BasePrice, p.CleaningFee, p.Currency, p.RatingAverage, p.ReviewCount,
                p.MaxGuests, p.Bedrooms, p.Beds, p.InstantBook, p.PublishedAt,
                Images = p.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).Take(5).ToList(),
            })
            .ToListAsync(ct);

        var items = rows.Select(r => new PropertyCardDto(r.Id, r.Title, r.PropertyType.ToString(), r.RoomType.ToString(), r.City, r.Country,
            r.Latitude, r.Longitude, r.BasePrice, r.Currency,
            nights is null ? null : r.BasePrice * nights.Value + r.CleaningFee,
            r.RatingAverage, r.ReviewCount, r.MaxGuests, r.Bedrooms, r.Beds, r.InstantBook, r.Images,
            q.Latitude is { } la && q.Longitude is { } lo && r.Latitude is { } rl && r.Longitude is { } rlo
                ? Math.Round(Haversine(la, lo, rl, rlo), 1) : null)).ToList();

        string? next = null;
        if (rows.Count == pageSize)
            next = useKeyset && rows[^1].PublishedAt is { } last
                ? EncodeCursor(last, rows[^1].Id)
                : Convert.ToBase64String(Encoding.UTF8.GetBytes($"page:{page + 1}"));

        return new PagedResult<PropertyCardDto>(items, page, pageSize, total, next);
    }

    private static (IOrderedQueryable<Property> Query, bool Keyset) ApplySort(IQueryable<Property> query, SearchPropertiesQuery q, double? cosLat) =>
        q.Sort switch
        {
            SearchSort.PriceLowHigh => (query.OrderBy(p => p.BasePrice).ThenBy(p => p.Id), false),
            SearchSort.PriceHighLow => (query.OrderByDescending(p => p.BasePrice).ThenBy(p => p.Id), false),
            SearchSort.Rating => (query.OrderByDescending(p => p.RatingAverage).ThenByDescending(p => p.ReviewCount).ThenBy(p => p.Id), false),
            SearchSort.MostReviewed => (query.OrderByDescending(p => p.ReviewCount).ThenBy(p => p.Id), false),
            SearchSort.Newest => (query.OrderByDescending(p => p.PublishedAt).ThenByDescending(p => p.Id), true),
            SearchSort.Distance when q.Latitude is { } lat && q.Longitude is { } lng && cosLat is { } c =>
                (query.OrderBy(p => (p.Latitude!.Value - lat) * (p.Latitude.Value - lat) +
                                    (p.Longitude!.Value - lng) * c * (p.Longitude.Value - lng) * c).ThenBy(p => p.Id), false),
            // Recommended: Bayesian average so a single 5★ review does not beat 200 reviews at 4.9★.
            _ => (query.OrderByDescending(p => (p.RatingAverage * p.ReviewCount + 4.5 * 5) / (p.ReviewCount + 5)).ThenBy(p => p.Id), false),
        };

    private static string EncodeCursor(DateTimeOffset publishedAt, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{publishedAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}|{id}"));

    private static int? TryDecodePageCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return null;
        try
        {
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            return text.StartsWith("page:", StringComparison.Ordinal) && int.TryParse(text[5..], CultureInfo.InvariantCulture, out var p) ? p : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static bool TryDecodeCursor(string? cursor, out DateTimeOffset publishedAt, out Guid id)
    {
        publishedAt = default;
        id = default;
        if (string.IsNullOrWhiteSpace(cursor)) return false;
        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|');
            if (parts.Length != 2 || !long.TryParse(parts[0], CultureInfo.InvariantCulture, out var ticks) || !Guid.TryParse(parts[1], out id))
                return false;
            publishedAt = new DateTimeOffset(ticks, TimeSpan.Zero);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static double Haversine(double lat1, double lon1, double lat2, double lon2)
    {
        static double Rad(double d) => d * Math.PI / 180;
        var a = Math.Pow(Math.Sin(Rad(lat2 - lat1) / 2), 2) + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Pow(Math.Sin(Rad(lon2 - lon1) / 2), 2);
        return 2 * 6371 * Math.Asin(Math.Sqrt(a));
    }
}
