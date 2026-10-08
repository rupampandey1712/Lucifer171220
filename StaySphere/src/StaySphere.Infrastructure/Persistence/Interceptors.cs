using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using StaySphere.Application.Common;
using StaySphere.Application.Events;
using StaySphere.Domain.Common;
using StaySphere.Domain.Platform;

namespace StaySphere.Infrastructure.Persistence;

/// <summary>
/// Converts domain events raised by aggregates into <see cref="OutboxMessage"/> rows inside the SAME SaveChanges
/// (and therefore the same transaction) as the state change. Nothing is published before commit.
/// Also maintains audit timestamps.
/// </summary>
public sealed class OutboxAndAuditInterceptor(TimeProvider clock) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context) Apply(context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is { } context) Apply(context);
        return base.SavingChanges(eventData, result);
    }

    private void Apply(DbContext context)
    {
        var now = clock.GetUtcNow();
        var correlationId = Activity.Current?.TraceId.ToString();

        foreach (var entry in context.ChangeTracker.Entries<AggregateRoot>())
        {
            if (entry.State == EntityState.Added && entry.Entity.CreatedAt == default) entry.Entity.CreatedAt = now;
            if (entry.State is EntityState.Added or EntityState.Modified) entry.Entity.UpdatedAt = now;
            // Changes to owned/child collections must still bump the aggregate's rowversion for optimistic concurrency.
            if (entry.State == EntityState.Unchanged && HasChangedChildren(entry)) entry.State = EntityState.Modified;
        }

        var aggregates = context.ChangeTracker.Entries<AggregateRoot>().Select(e => e.Entity).Where(e => e.DomainEvents.Count > 0).ToList();
        foreach (var aggregate in aggregates)
        {
            foreach (var domainEvent in aggregate.DomainEvents)
            {
                var integrationEvent = DomainEventMapper.Map(domainEvent);
                if (integrationEvent is null) continue;
                context.Set<OutboxMessage>().Add(OutboxMessage.Create(integrationEvent.GetType().Name,
                    JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType(), Json.Options), correlationId, now));
            }

            aggregate.ClearDomainEvents();
        }
    }

    private static bool HasChangedChildren(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry) =>
        entry.Navigations.Any(n => n is Microsoft.EntityFrameworkCore.ChangeTracking.CollectionEntry { CurrentValue: not null } c &&
            c.CurrentValue.Cast<object>().Any(child => entry.Context.Entry(child).State is EntityState.Added or EntityState.Modified or EntityState.Deleted));
}
