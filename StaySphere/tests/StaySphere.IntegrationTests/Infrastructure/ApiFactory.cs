using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using StaySphere.Application.Abstractions;
using StaySphere.Application.Common;
using StaySphere.Contracts.Auth;
using StaySphere.Domain.Catalog;
using StaySphere.Domain.ValueObjects;
using StaySphere.Infrastructure.Persistence;
using Testcontainers.MsSql;

namespace StaySphere.IntegrationTests.Infrastructure;

/// <summary>Real SQL Server (Testcontainers) + the real API pipeline (WebApplicationFactory). One container per test run.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string WebhookSecret = "test-webhook-secret";

    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public async Task InitializeAsync()
    {
        await _sql.StartAsync();
        _ = Services; // boot the host → applies migrations
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _sql.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Sql"] = _sql.GetConnectionString().Replace("Database=master", "Database=StaySphereTests", StringComparison.Ordinal),
            ["Jwt:SigningKey"] = "integration-tests-signing-key-0123456789abcdef",
            ["Quotes:SigningKey"] = "integration-tests-quote-key-0123456789abcdef",
            ["Payments:Fake:WebhookSecret"] = WebhookSecret,
            ["Payments:Fake:WebhookUrl"] = "",
            ["Database:MigrateOnStartup"] = "true",
            ["Database:Seed"] = "false",
            ["Email:Provider"] = "Log",
            ["Workers:Jobs"] = "false",
            ["RateLimiting:Multiplier"] = "1000",
            ["Storage:LocalPath"] = Path.Combine(Path.GetTempPath(), "staysphere-tests-media"),
        };
        foreach (var (key, value) in settings) builder.UseSetting(key, value);
    }

    public async Task<T> WithScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider);
    }

    public Task WithScopeAsync(Func<IServiceProvider, Task> action) => WithScopeAsync<int>(async sp =>
    {
        await action(sp);
        return 0;
    });

    public async Task<(HttpClient Client, AuthResponse Auth)> RegisterAsync(string? name = null)
    {
        var client = CreateClient();
        var email = $"{Guid.NewGuid():N}@test.local";
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Str0ngPassword!", name ?? "Test User"));
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>(Json.Options))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return (client, auth);
    }

    public async Task<(HttpClient Client, AuthResponse Auth)> RegisterHostAsync()
    {
        var (client, _) = await RegisterAsync("Test Host");
        var response = await client.PostAsync("/api/v1/me/become-host", null);
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>(Json.Options))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return (client, auth);
    }

    /// <summary>Creates a published listing directly through the domain model (fast, no image processing needed).</summary>
    public Task<Guid> CreatePublishedPropertyAsync(Guid hostId, decimal price = 120m, string city = "Testville", int maxGuests = 4, bool instantBook = true) =>
        WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var now = DateTimeOffset.UtcNow;
            var p = Property.CreateDraft(hostId, PropertyType.Apartment, RoomType.EntirePlace, now);
            p.UpdateBasics($"Integration test flat in {city}", "A comfortable flat used by the automated integration test suite.", PropertyType.Apartment, RoomType.EntirePlace);
            p.UpdateRooms(maxGuests, 2, 2, 1);
            p.SetLocation(new Address("1 Test Road", null, city, null, "00000", "PT", "Portugal"), Coordinates.Create(38.7, -9.1).Value);
            p.UpdatePricing(price, 30m, "EUR", 0, 0, 0, 1);
            p.UpdateRules(false, false, false, null, new TimeOnly(15, 0), new TimeOnly(11, 0), CancellationPolicy.Flexible, instantBook);
            p.SetAmenities(["wifi"]);
            p.AddImage("https://example.test/1.jpg", "https://example.test/1_t.jpg", null, 1200, 800);
            p.Publish(now);
            db.Properties.Add(p);
            await db.SaveChangesAsync();
            return p.Id;
        });
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}

public static class HttpExtensions
{
    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Json.Options))!;

    public static Task<HttpResponseMessage> PostJsonAsync(this HttpClient client, string url, object body, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body, options: Json.Options) };
        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
        return client.SendAsync(request);
    }

    public static async Task<T> EventuallyAsync<T>(Func<Task<T>> probe, Func<T, bool> done, int timeoutMs = 15000)
    {
        var started = DateTime.UtcNow;
        while (true)
        {
            var value = await probe();
            if (done(value) || (DateTime.UtcNow - started).TotalMilliseconds > timeoutMs) return value;
            await Task.Delay(200);
        }
    }
}
