using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using StaySphere.Infrastructure.Messaging;

namespace StaySphere.Infrastructure.Health;

/// <summary>
/// Remembers the outcome of real calls to each external API. Health checks read this instead of probing free public APIs
/// (Nominatim, Open-Meteo, Frankfurter) themselves, which would hammer them on every probe.
/// </summary>
public sealed class ExternalApiMonitor(TimeProvider clock)
{
    public sealed record Status(DateTimeOffset? LastSuccess, DateTimeOffset? LastFailure, int ConsecutiveFailures, string? LastError);

    private readonly ConcurrentDictionary<string, Status> _statuses = new();

    public IReadOnlyDictionary<string, Status> Snapshot => _statuses;

    public void Success(string api) =>
        _statuses.AddOrUpdate(api, _ => new Status(clock.GetUtcNow(), null, 0, null), (_, s) => s with { LastSuccess = clock.GetUtcNow(), ConsecutiveFailures = 0 });

    public void Failure(string api, string error) =>
        _statuses.AddOrUpdate(api, _ => new Status(null, clock.GetUtcNow(), 1, error),
            (_, s) => s with { LastFailure = clock.GetUtcNow(), ConsecutiveFailures = s.ConsecutiveFailures + 1, LastError = error });
}

/// <summary>Records each external call's final outcome (after retries) for <see cref="ExternalApisHealthCheck"/>.</summary>
public sealed class ExternalApiTrackingHandler(ExternalApiMonitor monitor, string api) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            if ((int)response.StatusCode >= 500 || response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                monitor.Failure(api, $"HTTP {(int)response.StatusCode}");
            else
                monitor.Success(api);
            return response;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            monitor.Failure(api, ex.GetType().Name);
            throw;
        }
    }
}

/// <summary>
/// Degraded (never Unhealthy) when an external API has failed repeatedly in the last 15 minutes: the app keeps working
/// (cached data, fallbacks), so this must not take instances out of rotation. Not tagged "ready" for the same reason.
/// </summary>
public sealed class ExternalApisHealthCheck(ExternalApiMonitor monitor, TimeProvider clock) : IHealthCheck
{
    private const int FailureThreshold = 3;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var data = monitor.Snapshot.ToDictionary(kv => kv.Key, kv => (object)new
        {
            kv.Value.LastSuccess, kv.Value.LastFailure, kv.Value.ConsecutiveFailures, kv.Value.LastError,
        });
        var failing = monitor.Snapshot
            .Where(kv => kv.Value.ConsecutiveFailures >= FailureThreshold && kv.Value.LastFailure > now - Window)
            .Select(kv => kv.Key).ToList();
        return Task.FromResult(failing.Count == 0
            ? HealthCheckResult.Healthy(monitor.Snapshot.Count == 0 ? "No external calls yet." : "External APIs responding.", data)
            : HealthCheckResult.Degraded($"Failing: {string.Join(", ", failing)} (fallbacks in use).", data: data));
    }
}

/// <summary>Peeks the workers' subscription: proves connectivity and Listen rights without consuming or sending anything.</summary>
public sealed class ServiceBusHealthCheck(ServiceBusClient client, IOptions<MessagingOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var receiver = client.CreateReceiver(options.Value.TopicName, options.Value.SubscriptionName);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            await receiver.PeekMessageAsync(cancellationToken: timeout.Token);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "Service Bus is unreachable.", ex);
        }
    }
}
