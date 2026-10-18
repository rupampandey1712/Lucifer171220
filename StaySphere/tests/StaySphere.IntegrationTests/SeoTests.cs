using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StaySphere.Infrastructure.Persistence;
using StaySphere.IntegrationTests.Infrastructure;

namespace StaySphere.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed partial class SeoTests(ApiFactory api)
{
    [GeneratedRegex("<script type=\"application/ld\\+json\">(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex LdJson();

    [Fact]
    public async Task Listing_pages_are_prerendered_with_metadata_and_structured_data()
    {
        var (_, host) = await api.RegisterHostAsync();
        var id = await api.CreatePublishedPropertyAsync(host.User.Id, price: 140m, city: "Seoville");

        var response = await api.CreateClient().GetAsync($"/seo/property/{id}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
        var html = await response.Content.ReadAsStringAsync();

        html.ShouldContain($"<link rel=\"canonical\" href=\"http://localhost:5173/property/{id}\">");
        html.ShouldContain("<meta name=\"description\"");
        html.ShouldContain("<meta property=\"og:title\"");
        using var ld = JsonDocument.Parse(LdJson().Match(html).Groups[1].Value);
        ld.RootElement.GetProperty("@type").GetString().ShouldBe("VacationRental");
        ld.RootElement.GetProperty("offers").GetProperty("price").GetDecimal().ShouldBe(140m);
        ld.RootElement.GetProperty("address").GetProperty("addressLocality").GetString().ShouldBe("Seoville");
    }

    [Fact]
    public async Task Hostile_listing_text_is_escaped_in_html_and_json_ld()
    {
        var (_, host) = await api.RegisterHostAsync();
        var id = await api.CreatePublishedPropertyAsync(host.User.Id);
        const string evil = "</script><script>alert('x')</script>\"><img src=x onerror=alert(1)>";
        await api.WithScopeAsync(sp => sp.GetRequiredService<AppDbContext>().Database
            .ExecuteSqlInterpolatedAsync($"UPDATE [catalog].[Properties] SET Title = {evil}, Description = {evil} WHERE Id = {id}"));

        var html = await (await api.CreateClient().GetAsync($"/seo/property/{id}")).Content.ReadAsStringAsync();

        html.ShouldNotContain("<script>alert");
        html.ShouldNotContain("<img src=x");
        Regex.Count(html, "</script>").ShouldBe(1, "only the JSON-LD block's own closing tag");
        using var ld = JsonDocument.Parse(LdJson().Match(html).Groups[1].Value);
        ld.RootElement.GetProperty("name").GetString().ShouldBe(evil, "escaped for HTML, intact as data");
    }

    [Fact]
    public async Task Unpublished_listings_are_not_rendered_or_listed()
    {
        var (hostClient, host) = await api.RegisterHostAsync();
        var id = await api.CreatePublishedPropertyAsync(host.User.Id);
        (await hostClient.PostJsonAsync($"/api/v1/properties/{id}/unpublish", new { })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await api.CreateClient().GetAsync($"/seo/property/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await api.CreateClient().GetAsync($"/seo/property/{Guid.NewGuid()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Sitemap_and_robots_point_crawlers_at_public_pages_only()
    {
        var sitemap = await (await api.CreateClient().GetAsync("/sitemap.xml")).Content.ReadAsStringAsync();
        sitemap.ShouldStartWith("<?xml");
        sitemap.ShouldContain("<loc>http://localhost:5173/search</loc>");
        sitemap.ShouldContain("<loc>http://localhost:5173/property/");

        var robots = await (await api.CreateClient().GetAsync("/robots.txt")).Content.ReadAsStringAsync();
        robots.ShouldContain("Disallow: /api/");
        robots.ShouldContain("Disallow: /admin");
        robots.ShouldContain("Sitemap: http://localhost:5173/sitemap.xml");
    }
}
