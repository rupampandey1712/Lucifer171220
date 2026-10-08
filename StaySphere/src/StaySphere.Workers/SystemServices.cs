using StaySphere.Application.Abstractions;

namespace StaySphere.Workers;

internal sealed class SystemUser : ICurrentUser
{
    public Guid? UserId => null;
    public bool IsAuthenticated => false;
    public IReadOnlyList<string> Roles => [];
    public string? IpAddress => null;
    public string? CorrelationId => System.Diagnostics.Activity.Current?.TraceId.ToString();
    public bool IsInRole(string role) => false;
}

internal sealed class NullRealtimeNotifier : IRealtimeNotifier, IPresenceTracker
{
    public Task SendToUserAsync(Guid userId, string method, object payload, CancellationToken cancellationToken) => Task.CompletedTask;
    public bool IsOnline(Guid userId) => false;
}
