using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using StaySphere.Contracts.Auth;

namespace StaySphere.ApiTests;

/// <summary>Runs only when STAYSPHERE_API_URL points at a running stack (e.g. http://localhost:5173), so plain `dotnet test` skips it.</summary>
public sealed class ApiFactAttribute : FactAttribute
{
    public ApiFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Api.BaseUrl)) Skip = "Set STAYSPHERE_API_URL to run black-box API tests against a running stack.";
    }
}

public static class Api
{
    public static readonly string? BaseUrl = Environment.GetEnvironmentVariable("STAYSPHERE_API_URL")?.TrimEnd('/');
    public const string DemoPassword = "Passw0rd!Demo";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static HttpClient Client() => new() { BaseAddress = new Uri(BaseUrl + "/") };

    // Sign-up and login are rate limited per IP (as they should be), so each test run creates one user and signs in once.
    private static readonly Lazy<Task<(HttpClient, AuthResponse)>> NewUser = new(RegisterAsync);
    private static readonly Lazy<Task<HttpClient>> DemoGuest = new(() => SignInAsync("guest@example.local"));

    /// <summary>A user registered by this run (fresh: no bookings, default consents).</summary>
    public static Task<(HttpClient Client, AuthResponse Auth)> NewUserAsync() => NewUser.Value;

    /// <summary>The seeded demo guest.</summary>
    public static Task<HttpClient> DemoGuestAsync() => DemoGuest.Value;

    public static async Task<HttpClient> SignInAsync(string email, string password = DemoPassword)
    {
        var client = Client();
        var response = await client.PostAsJsonAsync("api/v1/auth/login", new LoginRequest(email, password), Json);
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return client;
    }

    private static async Task<(HttpClient, AuthResponse)> RegisterAsync()
    {
        var client = Client();
        var response = await client.PostAsJsonAsync("api/v1/auth/register",
            new RegisterRequest($"api-{Guid.NewGuid():N}@example.test", "Sup3r-Secret-Pass!", "Black Box"), Json);
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return (client, auth);
    }

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            throw new ShouldAssertException($"{(int)response.StatusCode} {response.RequestMessage?.RequestUri}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    public static Task<HttpResponseMessage> PostAsync(this HttpClient client, string url, object body, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body, options: Json) };
        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
        return client.SendAsync(request);
    }
}
