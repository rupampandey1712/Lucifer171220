using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using StaySphere.Contracts.Auth;
using StaySphere.IntegrationTests.Infrastructure;

namespace StaySphere.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class AuthTests(ApiFactory api)
{
    [Fact]
    public async Task Register_login_and_me()
    {
        var client = api.CreateClient();
        var email = $"{Guid.NewGuid():N}@test.local";

        (await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Str0ngPassword!", "Ana"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "Str0ngPassword!"));
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        login.Headers.GetValues("Set-Cookie").ShouldContain(c => c.StartsWith("ss_rt=") && c.Contains("httponly") && c.Contains("samesite=strict"));

        var auth = await login.ReadAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new("Bearer", auth.AccessToken);
        var me = await (await client.GetAsync("/api/v1/me")).ReadAsync<UserDto>();
        me.Email.ShouldBe(email);
        me.Roles.ShouldBe(["Guest"]);
    }

    [Fact]
    public async Task Duplicate_email_is_conflict_and_validation_errors_are_problem_details()
    {
        var client = api.CreateClient();
        var email = $"{Guid.NewGuid():N}@test.local";
        await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Str0ngPassword!", "Ana"));

        (await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Str0ngPassword!", "Ana"))).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var invalid = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest("nope", "weak", ""));
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await invalid.ReadAsync<ValidationProblemDetails>();
        problem.Errors.Keys.ShouldContain("password");
        problem.Errors.Keys.ShouldContain("email");
    }

    [Fact]
    public async Task Wrong_password_is_generic_401_and_account_locks_after_five_failures()
    {
        var client = api.CreateClient();
        var email = $"{Guid.NewGuid():N}@test.local";
        await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Str0ngPassword!", "Ana"));

        for (var i = 0; i < 5; i++)
            (await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "WrongPassword1"))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        (await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "Str0ngPassword!"))).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("nobody@test.local", "Whatever123A"))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_rotates_and_reusing_an_old_token_revokes_the_family()
    {
        var client = api.CreateClient(new() { HandleCookies = false });
        var email = $"{Guid.NewGuid():N}@test.local";
        var register = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Str0ngPassword!", "Ana"));
        var first = Cookie(register);

        var refreshed = await Refresh(client, first);
        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var second = Cookie(refreshed);
        second.ShouldNotBe(first);

        (await Refresh(client, first)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized, "replayed token");
        (await Refresh(client, second)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized, "family revoked after reuse");
    }

    [Fact]
    public async Task Refresh_requires_anti_forgery_header()
    {
        var client = api.CreateClient();
        (await client.PostAsync("/api/v1/auth/refresh", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static string Cookie(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("ss_rt=")).Split(';')[0];

    private static Task<HttpResponseMessage> Refresh(HttpClient client, string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        request.Headers.Add("Cookie", cookie);
        request.Headers.Add("X-Requested-With", "test");
        return client.SendAsync(request);
    }
}
