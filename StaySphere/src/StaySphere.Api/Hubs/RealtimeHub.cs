using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using StaySphere.Application.Abstractions;
using StaySphere.Application.Engagement;

namespace StaySphere.Api.Hubs;

/// <summary>
/// Single authenticated hub for chat (messages, typing, read receipts, presence) and live notifications.
/// Scales out via the Redis backplane (or Azure SignalR Service) — see ADR-003.
/// </summary>
[Authorize]
public sealed class RealtimeHub(IMessagingService messaging, PresenceTracker presence) : Hub
{
    public override async Task OnConnectedAsync()
    {
        if (Context.UserIdentifier is { } id && Guid.TryParse(id, out var userId)) presence.Connected(userId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.UserIdentifier is { } id && Guid.TryParse(id, out var userId)) presence.Disconnected(userId);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task SendMessage(Guid conversationId, string body)
    {
        var result = await messaging.SendAsync(conversationId, body, Context.ConnectionAborted);
        if (result.IsFailure) throw new HubException(result.Error!.Message);
    }

    public async Task Typing(Guid conversationId)
    {
        var other = await messaging.GetOtherParticipantAsync(conversationId, Context.ConnectionAborted);
        if (other.IsSuccess)
            await Clients.User(other.Value.ToString()).SendAsync("Typing", new { conversationId, userId = Context.UserIdentifier }, Context.ConnectionAborted);
    }

    public async Task MarkRead(Guid conversationId) => await messaging.MarkReadAsync(conversationId, Context.ConnectionAborted);

    public bool IsOnline(Guid userId) => presence.IsOnline(userId);
}

public sealed class SubUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) => connection.User?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
}

/// <summary>Per-instance presence (connection counts). With multiple instances, move this to Redis.</summary>
public sealed class PresenceTracker : IPresenceTracker
{
    private readonly ConcurrentDictionary<Guid, int> _connections = new();

    public void Connected(Guid userId) => _connections.AddOrUpdate(userId, 1, (_, n) => n + 1);

    public void Disconnected(Guid userId)
    {
        if (_connections.AddOrUpdate(userId, 0, (_, n) => Math.Max(0, n - 1)) == 0) _connections.TryRemove(userId, out _);
    }

    public bool IsOnline(Guid userId) => _connections.ContainsKey(userId);
}

public sealed class SignalRRealtimeNotifier(IHubContext<RealtimeHub> hub) : IRealtimeNotifier
{
    public Task SendToUserAsync(Guid userId, string method, object payload, CancellationToken cancellationToken) =>
        hub.Clients.User(userId.ToString()).SendAsync(method, payload, cancellationToken);
}
