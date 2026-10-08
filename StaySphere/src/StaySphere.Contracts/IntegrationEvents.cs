namespace StaySphere.Contracts.Events;

/// <summary>Integration events cross module boundaries through the outbox and the message bus. Versioned by type name.</summary>
public interface IIntegrationEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAt { get; }
}

public abstract record IntegrationEvent : IIntegrationEvent
{
    public Guid EventId { get; init; } = Guid.CreateVersion7();
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record UserRegisteredEvent(Guid UserId, string Email, string DisplayName) : IntegrationEvent;
public sealed record EmailVerifiedEvent(Guid UserId, string Email) : IntegrationEvent;
public sealed record UserSuspendedEvent(Guid UserId) : IntegrationEvent;
public sealed record PropertyPublishedEvent(Guid PropertyId, Guid HostId) : IntegrationEvent;
public sealed record PropertyChangedEvent(Guid PropertyId, string Change) : IntegrationEvent;
public sealed record ReservationHeldEvent(Guid ReservationId, Guid PropertyId, Guid GuestId) : IntegrationEvent;
public sealed record ReservationConfirmedEvent(Guid ReservationId, Guid PropertyId, Guid GuestId, Guid HostId, decimal Total, string Currency) : IntegrationEvent;
public sealed record ReservationCancelledEvent(Guid ReservationId, Guid PropertyId, Guid GuestId, Guid HostId, decimal RefundAmount, string Currency) : IntegrationEvent;
public sealed record ReservationExpiredEvent(Guid ReservationId, Guid GuestId) : IntegrationEvent;
public sealed record ReservationFailedEvent(Guid ReservationId, Guid GuestId, string Reason) : IntegrationEvent;
public sealed record ReservationCompletedEvent(Guid ReservationId, Guid PropertyId, Guid GuestId, Guid HostId) : IntegrationEvent;
public sealed record PaymentSucceededEvent(Guid PaymentId, Guid ReservationId, Guid PayerId, decimal Amount, string Currency) : IntegrationEvent;
public sealed record PaymentFailedEvent(Guid PaymentId, Guid ReservationId, Guid PayerId, string Reason) : IntegrationEvent;
public sealed record RefundCompletedEvent(Guid PaymentId, Guid ReservationId, Guid PayerId, decimal Amount, string Currency) : IntegrationEvent;
public sealed record ReviewCreatedEvent(Guid ReviewId, Guid PropertyId, Guid HostId, int Overall) : IntegrationEvent;
public sealed record MessageSentEvent(Guid ConversationId, Guid MessageId, Guid SenderId, Guid RecipientId) : IntegrationEvent;
