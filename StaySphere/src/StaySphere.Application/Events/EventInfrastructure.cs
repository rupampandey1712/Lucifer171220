using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StaySphere.Application.Abstractions;
using StaySphere.Application.Common;
using StaySphere.Contracts.Events;
using StaySphere.Domain.Booking;
using StaySphere.Domain.Catalog;
using StaySphere.Domain.Common;
using StaySphere.Domain.Engagement;
using StaySphere.Domain.Identity;
using StaySphere.Domain.Payments;
using StaySphere.Domain.Platform;
using StaySphere.Domain.Reviews;

namespace StaySphere.Application.Events;

/// <summary>Domain events (in-process, inside an aggregate) are translated to versioned integration events for the outbox.</summary>
public static class DomainEventMapper
{
    public static IIntegrationEvent? Map(IDomainEvent e) => e switch
    {
        UserRegisteredDomainEvent x => new UserRegisteredEvent(x.UserId, x.Email, x.DisplayName),
        EmailVerifiedDomainEvent x => new EmailVerifiedEvent(x.UserId, x.Email),
        UserSuspendedDomainEvent x => new UserSuspendedEvent(x.UserId),
        PropertyPublishedDomainEvent x => new PropertyPublishedEvent(x.PropertyId, x.HostId),
        PropertyChangedDomainEvent x => new PropertyChangedEvent(x.PropertyId, x.Change),
        ReservationHeldDomainEvent x => new ReservationHeldEvent(x.ReservationId, x.PropertyId, x.GuestId),
        ReservationConfirmedDomainEvent x => new ReservationConfirmedEvent(x.ReservationId, x.PropertyId, x.GuestId, x.HostId, x.Total, x.Currency),
        ReservationCancelledDomainEvent x => new ReservationCancelledEvent(x.ReservationId, x.PropertyId, x.GuestId, x.HostId, x.RefundAmount, x.Currency),
        ReservationExpiredDomainEvent x => new ReservationExpiredEvent(x.ReservationId, x.GuestId),
        ReservationFailedDomainEvent x => new ReservationFailedEvent(x.ReservationId, x.GuestId, x.Reason),
        ReservationRequestedDomainEvent x => new ReservationRequestedEvent(x.ReservationId, x.PropertyId, x.GuestId, x.HostId, x.Deadline),
        ReservationDeclinedDomainEvent x => new ReservationDeclinedEvent(x.ReservationId, x.PropertyId, x.GuestId, x.HostId, x.Expired),
        PayoutPaidDomainEvent x => new PayoutPaidEvent(x.PayoutId, x.HostId, x.Amount, x.Currency),
        ReservationCompletedDomainEvent x => new ReservationCompletedEvent(x.ReservationId, x.PropertyId, x.GuestId, x.HostId),
        PaymentSucceededDomainEvent x => new PaymentSucceededEvent(x.PaymentId, x.ReservationId, x.PayerId, x.Amount, x.Currency),
        PaymentFailedDomainEvent x => new PaymentFailedEvent(x.PaymentId, x.ReservationId, x.PayerId, x.Reason),
        RefundCompletedDomainEvent x => new RefundCompletedEvent(x.PaymentId, x.ReservationId, x.PayerId, x.Amount, x.Currency),
        ReviewCreatedDomainEvent x => new ReviewCreatedEvent(x.ReviewId, x.PropertyId, x.HostId, x.Overall),
        MessageSentDomainEvent x => new MessageSentEvent(x.ConversationId, x.MessageId, x.SenderId, x.RecipientId),
        _ => null,
    };
}

public static class IntegrationEventTypes
{
    private static readonly Dictionary<string, Type> ByName = typeof(IIntegrationEvent).Assembly.GetTypes()
        .Where(t => typeof(IIntegrationEvent).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false })
        .ToDictionary(t => t.Name);

    public static Type? Resolve(string name) => ByName.GetValueOrDefault(name);
}

public interface IIntegrationEventHandler<in TEvent> where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent @event, CancellationToken ct);
}

/// <summary>
/// Dispatches a received integration event to every registered handler. Each handler runs at most once per message
/// thanks to the inbox table (at-least-once delivery + idempotent consumers = effectively once).
/// </summary>
public sealed class IntegrationEventDispatcher(IServiceScopeFactory scopes, TimeProvider clock, ILogger<IntegrationEventDispatcher> logger)
{
    public async Task DispatchAsync(Guid messageId, string type, string payload, CancellationToken ct)
    {
        var eventType = IntegrationEventTypes.Resolve(type);
        if (eventType is null)
        {
            logger.LogWarning("Unknown integration event type {Type}; ignoring message {MessageId}", type, messageId);
            return;
        }

        var @event = (IIntegrationEvent)JsonSerializer.Deserialize(payload, eventType, Json.Options)!;
        var handlerType = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);

        int handlerCount;
        using (var probe = scopes.CreateScope())
            handlerCount = probe.ServiceProvider.GetServices(handlerType).Count();

        for (var i = 0; i < handlerCount; i++)
        {
            // Fresh scope (and DbContext) per handler so one handler's failure cannot poison another's unit of work.
            using var scope = scopes.CreateScope();
            var handler = scope.ServiceProvider.GetServices(handlerType).ElementAt(i)!;
            var consumer = handler.GetType().Name;
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            if (await db.InboxMessages.AnyAsync(m => m.MessageId == messageId && m.Consumer == consumer, ct)) continue;

            await (Task)handlerType.GetMethod(nameof(IIntegrationEventHandler<IIntegrationEvent>.HandleAsync))!.Invoke(handler, [@event, ct])!;

            db.InboxMessages.Add(new InboxMessage(messageId, consumer, clock.GetUtcNow()));
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (db.IsUniqueViolation(ex))
            {
                db.ResetTracking();
            }
        }
    }
}
