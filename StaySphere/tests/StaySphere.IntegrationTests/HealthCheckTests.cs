using System.Net;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StaySphere.Infrastructure.Health;

namespace StaySphere.IntegrationTests;

/// <summary>External-API health is derived from real calls, degrades (never fails) and recovers.</summary>
public sealed class HealthCheckTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Stub(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status));
    }

    private static async Task CallAsync(ExternalApiMonitor monitor, HttpStatusCode status)
    {
        using var client = new HttpClient(new ExternalApiTrackingHandler(monitor, "weather") { InnerHandler = new Stub(status) });
        using var _ = await client.GetAsync("http://weather.test/");
    }

    private static Task<HealthCheckResult> CheckAsync(ExternalApiMonitor monitor, Clock clock) =>
        new ExternalApisHealthCheck(monitor, clock).CheckHealthAsync(new HealthCheckContext());

    [Fact]
    public async Task Repeated_failures_degrade_and_a_success_recovers()
    {
        var clock = new Clock();
        var monitor = new ExternalApiMonitor(clock);

        (await CheckAsync(monitor, clock)).Status.ShouldBe(HealthStatus.Healthy);

        for (var i = 0; i < 3; i++) await CallAsync(monitor, HttpStatusCode.ServiceUnavailable);
        var degraded = await CheckAsync(monitor, clock);
        degraded.Status.ShouldBe(HealthStatus.Degraded, "a failing free API must never mark the app unhealthy");
        degraded.Description!.ShouldContain("weather");

        await CallAsync(monitor, HttpStatusCode.OK);
        (await CheckAsync(monitor, clock)).Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Old_failures_and_client_errors_dont_count()
    {
        var clock = new Clock();
        var monitor = new ExternalApiMonitor(clock);
        for (var i = 0; i < 3; i++) await CallAsync(monitor, HttpStatusCode.BadGateway);

        clock.Now = clock.Now.AddMinutes(20);
        (await CheckAsync(monitor, clock)).Status.ShouldBe(HealthStatus.Healthy, "failures older than 15 minutes are stale");

        for (var i = 0; i < 5; i++) await CallAsync(monitor, HttpStatusCode.NotFound);
        (await CheckAsync(monitor, clock)).Status.ShouldBe(HealthStatus.Healthy, "a 404 for one query is not an outage");
    }
}
