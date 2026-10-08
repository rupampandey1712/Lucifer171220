namespace StaySphere.Domain.Common;

public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.CreateVersion7();
}

/// <summary>Aggregate root: carries domain events, audit timestamps and an optimistic-concurrency token.</summary>
public abstract class AggregateRoot : Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents;

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}

/// <summary>Something meaningful that happened inside an aggregate. Mapped to integration events by the outbox.</summary>
public interface IDomainEvent;
