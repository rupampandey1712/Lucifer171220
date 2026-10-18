using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StaySphere.Api.Common;
using StaySphere.Api.Seo;
using StaySphere.Application.Catalog;

namespace StaySphere.Api.Controllers;

/// <summary>Crawler-facing pages. nginx sends bot user agents for /property/{id} here, and /sitemap.xml and /robots.txt for everyone.</summary>
[ApiController]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
[EnableRateLimiting(RateLimitPolicies.Anonymous)]
public sealed class SeoController(ISeoService seo) : ControllerBase
{
    [HttpGet("/seo/property/{id:guid}")]
    public async Task<IActionResult> Listing(Guid id, CancellationToken ct)
    {
        var page = await seo.ListingAsync(id, ct);
        if (page is null) return NotFound();
        Response.Headers.CacheControl = "public, max-age=300";
        return Content(SeoHtml.Listing(page), "text/html; charset=utf-8");
    }

    [HttpGet("/sitemap.xml")]
    public async Task<IActionResult> Sitemap(CancellationToken ct)
    {
        Response.Headers.CacheControl = "public, max-age=3600";
        return Content(SeoHtml.Sitemap(await seo.SitemapAsync(ct)), "application/xml; charset=utf-8");
    }

    [HttpGet("/robots.txt")]
    public IActionResult Robots()
    {
        Response.Headers.CacheControl = "public, max-age=86400";
        return Content(SeoHtml.Robots(seo.SiteUrl), "text/plain; charset=utf-8");
    }
}
