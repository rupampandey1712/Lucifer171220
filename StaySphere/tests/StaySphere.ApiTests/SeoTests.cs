using System.Net;
using StaySphere.Contracts;
using StaySphere.Contracts.Properties;

namespace StaySphere.ApiTests;

/// <summary>nginx dynamic rendering: crawlers get prerendered HTML at the same URL people use; people get the SPA.</summary>
public sealed class SeoTests
{
    private static async Task<Guid> AnyListingAsync() =>
        (await (await Api.Client().GetAsync("api/v1/search/properties?pageSize=1")).ReadAsync<PagedResult<PropertyCardDto>>()).Items[0].Id;

    private static Task<HttpResponseMessage> GetAsAsync(string url, string userAgent)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        return Api.Client().SendAsync(request);
    }

    [ApiFact]
    public async Task Crawlers_get_prerendered_listing_pages_and_people_get_the_spa()
    {
        var id = await AnyListingAsync();

        var bot = await (await GetAsAsync($"property/{id}", "Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)")).Content.ReadAsStringAsync();
        bot.ShouldContain("application/ld+json");
        bot.ShouldContain($"/property/{id}\">");

        var person = await GetAsAsync($"property/{id}", "Mozilla/5.0 (X11; Linux x86_64) Chrome/130.0");
        var html = await person.Content.ReadAsStringAsync();
        html.ShouldContain("<div id=\"root\">");
        html.ShouldNotContain("application/ld+json");
        person.Headers.GetValues("Content-Security-Policy").ShouldNotBeEmpty("SPA routes keep the security headers");
    }

    [ApiFact]
    public async Task Prerender_endpoint_is_not_directly_reachable_and_crawler_files_are()
    {
        (await Api.Client().GetAsync($"seo/property/{await AnyListingAsync()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await (await Api.Client().GetAsync("robots.txt")).Content.ReadAsStringAsync()).ShouldContain("Sitemap:");
        (await (await Api.Client().GetAsync("sitemap.xml")).Content.ReadAsStringAsync()).ShouldContain("<urlset");
    }
}
