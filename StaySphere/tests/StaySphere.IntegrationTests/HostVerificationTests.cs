using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StaySphere.Contracts;
using StaySphere.Contracts.Auth;
using StaySphere.Contracts.Properties;
using StaySphere.Infrastructure.Persistence;
using StaySphere.IntegrationTests.Infrastructure;

namespace StaySphere.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class HostVerificationTests(ApiFactory api)
{
    private Task ConfirmEmailAsync(Guid userId) => api.WithScopeAsync(sp => sp.GetRequiredService<AppDbContext>().Database
        .ExecuteSqlInterpolatedAsync($"UPDATE [identity].[Users] SET EmailConfirmed = 1 WHERE Id = {userId}"));

    [Fact]
    public async Task Host_requests_admin_approves_and_guests_see_the_badge()
    {
        var (host, hostAuth) = await api.RegisterHostAsync();
        await ConfirmEmailAsync(hostAuth.User.Id);
        var propertyId = await api.CreatePublishedPropertyAsync(hostAuth.User.Id);

        (await (await host.GetAsync("/api/v1/host/verification")).ReadAsync<HostVerificationDto>()).Status.ShouldBe("None");
        var pending = await (await host.PostJsonAsync("/api/v1/host/verification", new RequestHostVerificationRequest("Ana Maria Silva", "Superhost elsewhere")))
            .ReadAsync<HostVerificationDto>();
        pending.Status.ShouldBe("Pending");
        (await host.PostJsonAsync("/api/v1/host/verification", new RequestHostVerificationRequest("Ana Maria Silva", null))).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var (admin, _) = await api.RegisterAdminAsync();
        var queue = await (await admin.GetAsync("/api/v1/admin/host-verifications?pageSize=100")).ReadAsync<PagedResult<HostVerificationDto>>();
        queue.Items.ShouldContain(v => v.UserId == hostAuth.User.Id);
        var approved = await (await admin.PostJsonAsync($"/api/v1/admin/host-verifications/{hostAuth.User.Id}", new ReviewHostVerificationRequest(true, null)))
            .ReadAsync<HostVerificationDto>();
        approved.Status.ShouldBe("Verified");
        approved.VerifiedAt.ShouldNotBeNull();

        var detail = await (await api.CreateClient().GetAsync($"/api/v1/properties/{propertyId}")).ReadAsync<PropertyDetailDto>();
        detail.Host.Verified.ShouldBeTrue();
    }

    [Fact]
    public async Task Rejection_needs_a_reason_and_the_host_can_try_again()
    {
        var (host, hostAuth) = await api.RegisterHostAsync();
        await ConfirmEmailAsync(hostAuth.User.Id);
        await host.PostJsonAsync("/api/v1/host/verification", new RequestHostVerificationRequest("Ana Maria Silva", null));
        var (admin, _) = await api.RegisterAdminAsync();
        var url = $"/api/v1/admin/host-verifications/{hostAuth.User.Id}";

        (await admin.PostJsonAsync(url, new ReviewHostVerificationRequest(false, null))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var rejected = await (await admin.PostJsonAsync(url, new ReviewHostVerificationRequest(false, "Name doesn't match the payout account"))).ReadAsync<HostVerificationDto>();
        rejected.Status.ShouldBe("Rejected");
        rejected.Note.ShouldBe("Name doesn't match the payout account");

        (await host.PostJsonAsync("/api/v1/host/verification", new RequestHostVerificationRequest("Ana M. Silva", null))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Guests_unconfirmed_hosts_and_non_admins_are_refused()
    {
        var (guest, _) = await api.RegisterAsync();
        (await guest.PostJsonAsync("/api/v1/host/verification", new RequestHostVerificationRequest("Some Guest", null))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await guest.GetAsync("/api/v1/admin/host-verifications")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var (host, _) = await api.RegisterHostAsync();
        (await host.PostJsonAsync("/api/v1/host/verification", new RequestHostVerificationRequest("Unconfirmed Host", null))).StatusCode
            .ShouldBe(HttpStatusCode.UnprocessableEntity);
    }
}
