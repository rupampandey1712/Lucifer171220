using System.Net;
using StaySphere.Contracts.Auth;

namespace StaySphere.ApiTests;

/// <summary>Edge behaviour as a real client sees it: through nginx, with real headers, status codes and problem documents.</summary>
public sealed class PlatformTests
{
    [ApiFact]
    public async Task Health_and_feature_flags_are_public()
    {
        var client = Api.Client();
        (await client.GetAsync("api/v1/features")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [ApiFact]
    public async Task Responses_carry_security_headers()
    {
        var response = await Api.Client().GetAsync("api/v1/features");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues("X-Content-Type-Options").ShouldContain("nosniff");
        response.Headers.GetValues("X-Frame-Options").ShouldContain("DENY");
        string.Join(";", response.Headers.GetValues("Content-Security-Policy")).ShouldContain("frame-ancestors 'none'");
    }

    [ApiFact]
    public async Task Protected_endpoints_return_401_problem_details()
    {
        var response = await Api.Client().GetAsync("api/v1/me");
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [ApiFact]
    public async Task Invalid_input_returns_a_400_problem_document()
    {
        var response = await Api.Client().PostAsync("api/v1/auth/register", new RegisterRequest("not-an-email", "short", ""));
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    [ApiFact]
    public async Task Sign_up_then_profile_round_trip()
    {
        var (client, auth) = await Api.NewUserAsync();
        var me = await (await client.GetAsync("api/v1/me")).ReadAsync<UserDto>();
        me.Id.ShouldBe(auth.User.Id);
        var consents = await (await client.GetAsync("api/v1/me/consents")).ReadAsync<List<ConsentDto>>();
        consents.ShouldContain(c => c.Purpose == "Terms" && c.Granted);
    }

    [ApiFact]
    public async Task Admin_endpoints_are_forbidden_to_guests()
    {
        var guest = await Api.DemoGuestAsync();
        (await guest.GetAsync("api/v1/admin/dashboard")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
