using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StaySphere.Application.Booking;
using StaySphere.Contracts;
using StaySphere.Contracts.Properties;
using StaySphere.Contracts.Search;
using StaySphere.Domain.Catalog;
using StaySphere.Infrastructure.Persistence;
using StaySphere.IntegrationTests.Infrastructure;

namespace StaySphere.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class SearchInsightsTests(ApiFactory api)
{
    [Fact]
    public async Task Searches_are_logged_anonymously_and_rare_terms_stay_hidden()
    {
        var city = $"Insightville{Guid.NewGuid():N}"[..20];
        var rare = $"Rareplace{Guid.NewGuid():N}"[..20];
        var anon = api.CreateClient();
        for (var i = 0; i < 3; i++) (await anon.GetAsync($"/api/v1/search/properties?location={city}&guests=2&page=1")).StatusCode.ShouldBe(HttpStatusCode.OK);
        await anon.GetAsync($"/api/v1/search/properties?location={city}&page=2"); // later pages aren't counted
        await anon.GetAsync($"/api/v1/search/properties?location={rare}");

        var logged = await api.WithScopeAsync(sp => sp.GetRequiredService<AppDbContext>().SearchQueryLogs.Where(l => l.Location == city.ToLowerInvariant()).ToListAsync());
        logged.Count.ShouldBe(3);
        logged.ShouldAllBe(l => l.Guests == 2 && l.ResultCount == 0);

        var (admin, _) = await api.RegisterAdminAsync();
        var insights = await (await admin.GetAsync("/api/v1/admin/search-insights?days=7")).ReadAsync<SearchInsightsDto>();
        insights.ZeroResultSearches.ShouldContain(t => t.Location == city.ToLowerInvariant() && t.Searches == 3);
        insights.ZeroResultSearches.ShouldNotContain(t => t.Location == rare.ToLowerInvariant(), "fewer than 3 searches is never shown");

        var (guest, _) = await api.RegisterAsync();
        (await guest.GetAsync("/api/v1/admin/search-insights")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Logs_older_than_90_days_are_deleted_by_cleanup()
    {
        var old = SearchQueryLog.Record("Ancient Rome", null, null, 1, DateTimeOffset.UtcNow.AddDays(-91));
        await api.WithScopeAsync(async sp => { var db = sp.GetRequiredService<AppDbContext>(); db.SearchQueryLogs.Add(old); await db.SaveChangesAsync(); });

        await api.WithScopeAsync(sp => sp.GetRequiredService<IBookingMaintenance>().CleanupAsync(CancellationToken.None));

        (await api.WithScopeAsync(sp => sp.GetRequiredService<AppDbContext>().SearchQueryLogs.AnyAsync(l => l.Id == old.Id))).ShouldBeFalse();
    }

    [Fact]
    public async Task Newest_sort_pages_with_a_keyset_cursor_without_gaps_or_duplicates()
    {
        var (_, host) = await api.RegisterHostAsync();
        var city = $"Cursorton{Guid.NewGuid():N}"[..18];
        for (var i = 0; i < 5; i++) await api.CreatePublishedPropertyAsync(host.User.Id, city: city);
        var client = api.CreateClient();

        var seen = new List<Guid>();
        string? cursor = null;
        do
        {
            var url = $"/api/v1/search/properties?location={city}&sort=Newest&pageSize=2" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            var page = await (await client.GetAsync(url)).ReadAsync<PagedResult<PropertyCardDto>>();
            seen.AddRange(page.Items.Select(p => p.Id));
            cursor = page.NextCursor;
        }
        while (cursor is not null && seen.Count < 20);

        seen.Count.ShouldBe(5);
        seen.Distinct().Count().ShouldBe(5);
    }
}
