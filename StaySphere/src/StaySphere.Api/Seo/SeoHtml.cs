using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using StaySphere.Application.Catalog;

namespace StaySphere.Api.Seo;

/// <summary>
/// Server-rendered HTML for crawlers and link-preview bots (nginx routes their user agents here). Every piece of
/// user-generated text goes through <see cref="E"/> (HTML encoding) or the JSON serializer, which escapes &lt;, &gt; and &amp;,
/// so a listing title like <c>&lt;/script&gt;</c> can't break out of the page.
/// </summary>
public static class SeoHtml
{
    private static readonly JsonSerializerOptions LdJson = new() { Encoder = JavaScriptEncoder.Default, WriteIndented = false };

    private static string E(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);

    private static string Money(decimal amount, string currency) => $"{amount.ToString("0.##", CultureInfo.InvariantCulture)} {currency}";

    public static string Describe(SeoListingPage page)
    {
        var p = page.Property;
        var where = p.Address is null ? null : $"{p.Address.City}, {p.Address.Country}";
        var summary = $"{p.PropertyType} for up to {p.MaxGuests} guests{(where is null ? "" : $" in {where}")}: {p.Bedrooms} bedroom(s), {p.Beds} bed(s). {p.Description}";
        return summary.Length <= 160 ? summary : summary[..157].TrimEnd() + "…";
    }

    /// <summary>A schema.org node: C# anonymous types can't produce the "@type" key.</summary>
    private static Dictionary<string, object?> Node(string type, params (string Key, object? Value)[] props)
    {
        var node = new Dictionary<string, object?> { ["@type"] = type };
        foreach (var (key, value) in props) node[key] = value;
        return node;
    }

    public static Dictionary<string, object?> JsonLd(SeoListingPage page)
    {
        var p = page.Property;
        var ld = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "VacationRental",
            ["@id"] = page.CanonicalUrl,
            ["name"] = p.Title,
            ["description"] = p.Description,
            ["url"] = page.CanonicalUrl,
            ["image"] = p.Images.Select(i => i.Url).Take(8).ToList(),
            ["containsPlace"] = new Dictionary<string, object?>
            {
                ["@type"] = "Accommodation",
                ["occupancy"] = Node("QuantitativeValue", ("value", p.MaxGuests)),
                ["numberOfBedrooms"] = p.Bedrooms,
                ["amenityFeature"] = p.Amenities.Select(a => Node("LocationFeatureSpecification", ("name", a.Name), ("value", true))).ToList(),
            },
            ["offers"] = Node("Offer", ("price", p.BasePrice), ("priceCurrency", p.Currency), ("unitText", "night")),
        };
        if (p.Address is { } a)
            ld["address"] = Node("PostalAddress", ("addressLocality", a.City), ("addressRegion", a.Region), ("addressCountry", a.CountryCode));
        if (p.Latitude is { } lat && p.Longitude is { } lng)
            ld["geo"] = Node("GeoCoordinates", ("latitude", Math.Round(lat, 3)), ("longitude", Math.Round(lng, 3))); // ~100 m: never the exact door
        if (p.ReviewCount > 0)
            ld["aggregateRating"] = Node("AggregateRating", ("ratingValue", Math.Round(p.RatingAverage, 2)), ("reviewCount", p.ReviewCount), ("bestRating", 5));
        return ld;
    }

    public static string Listing(SeoListingPage page)
    {
        var p = page.Property;
        var description = Describe(page);
        var image = p.Images.Count > 0 ? p.Images[0].Url : null;
        var title = $"{p.Title} · StaySphere";
        var ld = JsonSerializer.Serialize(JsonLd(page), LdJson);

        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.Append($"<title>{E(title)}</title>");
        sb.Append($"<meta name=\"description\" content=\"{E(description)}\">");
        sb.Append($"<link rel=\"canonical\" href=\"{E(page.CanonicalUrl)}\">");
        sb.Append("<meta property=\"og:type\" content=\"website\"><meta property=\"og:site_name\" content=\"StaySphere\">");
        sb.Append($"<meta property=\"og:title\" content=\"{E(p.Title)}\"><meta property=\"og:description\" content=\"{E(description)}\">");
        sb.Append($"<meta property=\"og:url\" content=\"{E(page.CanonicalUrl)}\">");
        if (image is not null) sb.Append($"<meta property=\"og:image\" content=\"{E(image)}\"><meta name=\"twitter:card\" content=\"summary_large_image\">");
        sb.Append($"<script type=\"application/ld+json\">{ld}</script></head><body>");

        sb.Append($"<header><a href=\"{E(page.SiteUrl)}/\">StaySphere</a></header><main><article>");
        sb.Append($"<h1>{E(p.Title)}</h1>");
        if (p.Address is { } a) sb.Append($"<p>{E(a.City)}, {E(a.Country)}</p>");
        sb.Append($"<p>{E(p.PropertyType)} · {p.MaxGuests} guests · {p.Bedrooms} bedrooms · {p.Beds} beds · {p.Bathrooms.ToString("0.#", CultureInfo.InvariantCulture)} baths</p>");
        sb.Append($"<p>From {E(Money(p.BasePrice, p.Currency))} per night");
        if (p.ReviewCount > 0) sb.Append($" · rated {p.RatingAverage.ToString("0.00", CultureInfo.InvariantCulture)} from {p.ReviewCount} reviews");
        sb.Append($"</p><p>Hosted by {E(p.Host.DisplayName)}{(p.Host.Verified ? " (verified host)" : "")}</p>");
        foreach (var img in p.Images.Take(5)) sb.Append($"<img src=\"{E(img.Url)}\" alt=\"{E(p.Title)}\" width=\"{img.Width}\" height=\"{img.Height}\" loading=\"lazy\">");
        sb.Append("<h2>About this place</h2>");
        foreach (var para in p.Description.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) sb.Append($"<p>{E(para)}</p>");
        if (p.Amenities.Count > 0)
        {
            sb.Append("<h2>What this place offers</h2><ul>");
            foreach (var amenity in p.Amenities) sb.Append($"<li>{E(amenity.Name)}</li>");
            sb.Append("</ul>");
        }
        if (page.Reviews.Count > 0)
        {
            sb.Append("<h2>Reviews</h2>");
            foreach (var r in page.Reviews)
                sb.Append($"<blockquote><p>{E(r.Comment)}</p><footer>{E(r.GuestName)}, {r.CreatedAt:MMMM yyyy} · {r.Overall}/5</footer></blockquote>");
        }
        sb.Append($"<p><a href=\"{E(page.CanonicalUrl)}\">Check availability and book on StaySphere</a></p>");
        sb.Append("</article></main></body></html>");
        return sb.ToString();
    }

    public static string Sitemap(IEnumerable<SitemapEntry> entries)
    {
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
        foreach (var e in entries)
        {
            sb.Append($"<url><loc>{System.Security.SecurityElement.Escape(e.Url)}</loc>");
            if (e.LastModified is { } m) sb.Append($"<lastmod>{m.UtcDateTime:yyyy-MM-dd}</lastmod>");
            sb.Append("</url>");
        }
        return sb.Append("</urlset>").ToString();
    }

    public static string Robots(string siteUrl) => $"""
        User-agent: *
        Allow: /
        Disallow: /api/
        Disallow: /book/
        Disallow: /trips
        Disallow: /messages
        Disallow: /host
        Disallow: /admin
        Disallow: /account
        Disallow: /profile
        Sitemap: {siteUrl}/sitemap.xml

        """;
}
