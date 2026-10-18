using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StaySphere.Application.Abstractions;
using StaySphere.Application.Identity;
using StaySphere.Application.Reviews;
using StaySphere.Contracts.Properties;
using StaySphere.Contracts.Reviews;
using StaySphere.Domain.Catalog;

namespace StaySphere.Application.Catalog;

public sealed record SeoListingPage(PropertyDetailDto Property, IReadOnlyList<ReviewDto> Reviews, string CanonicalUrl, string SiteUrl);

public sealed record SitemapEntry(string Url, DateTimeOffset? LastModified);

/// <summary>Data for crawler-facing pages: prerendered listings, the sitemap and robots.txt. Only published listings are ever exposed.</summary>
public interface ISeoService
{
    string SiteUrl { get; }
    Task<SeoListingPage?> ListingAsync(Guid propertyId, CancellationToken ct);
    Task<IReadOnlyList<SitemapEntry>> SitemapAsync(CancellationToken ct);
}

public sealed class SeoService(IPropertyService properties, IReviewService reviews, IAppDbContext db, ICacheService cache, IOptions<AppOptions> options)
    : ISeoService
{
    /// <summary>The sitemap protocol caps one file at 50,000 URLs.</summary>
    public const int MaxSitemapUrls = 50_000;

    public string SiteUrl => options.Value.PublicWebUrl.TrimEnd('/');

    public async Task<SeoListingPage?> ListingAsync(Guid propertyId, CancellationToken ct)
    {
        var property = await properties.GetAsync(propertyId, ct);
        if (property is null || property.Status != nameof(PropertyStatus.Published)) return null;
        var recent = await reviews.ListForPropertyAsync(propertyId, 1, 5, ct);
        return new SeoListingPage(property, recent.Items, $"{SiteUrl}/property/{propertyId}", SiteUrl);
    }

    public Task<IReadOnlyList<SitemapEntry>> SitemapAsync(CancellationToken ct) =>
        cache.GetOrCreateAsync<IReadOnlyList<SitemapEntry>>("seo:sitemap", TimeSpan.FromHours(1), async c =>
        {
            var listings = await db.Properties.AsNoTracking()
                .Where(p => p.Status == PropertyStatus.Published && p.DeletedAt == null)
                .OrderByDescending(p => p.UpdatedAt).Take(MaxSitemapUrls - 2)
                .Select(p => new { p.Id, p.UpdatedAt }).ToListAsync(c);
            return [new SitemapEntry($"{SiteUrl}/", null), new SitemapEntry($"{SiteUrl}/search", null),
                .. listings.Select(l => new SitemapEntry($"{SiteUrl}/property/{l.Id}", l.UpdatedAt))];
        }, ct);
}
