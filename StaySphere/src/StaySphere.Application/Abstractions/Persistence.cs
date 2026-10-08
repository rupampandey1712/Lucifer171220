using Microsoft.EntityFrameworkCore;
using StaySphere.Domain.Booking;
using StaySphere.Domain.Catalog;
using StaySphere.Domain.Engagement;
using StaySphere.Domain.Identity;
using StaySphere.Domain.Payments;
using StaySphere.Domain.Platform;
using StaySphere.Domain.Pricing;
using StaySphere.Domain.Reviews;
using StaySphere.Domain.Trust;

namespace StaySphere.Application.Abstractions;

/// <summary>
/// Unit of work over the relational store. Application services query through LINQ (provider-neutral) and
/// persist through <see cref="SaveChangesAsync"/>; the infrastructure implementation adds outbox, audit
/// timestamps and concurrency handling. See ADR-008.
/// </summary>
public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<UserToken> UserTokens { get; }
    DbSet<Property> Properties { get; }
    DbSet<Amenity> Amenities { get; }
    DbSet<Reservation> Reservations { get; }
    DbSet<ReservationNight> ReservationNights { get; }
    DbSet<Payment> Payments { get; }
    DbSet<LedgerEntry> LedgerEntries { get; }
    DbSet<WebhookEvent> WebhookEvents { get; }
    DbSet<Coupon> Coupons { get; }
    DbSet<Review> Reviews { get; }
    DbSet<Favorite> Favorites { get; }
    DbSet<Conversation> Conversations { get; }
    DbSet<Message> Messages { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<SupportTicket> SupportTickets { get; }
    DbSet<Report> Reports { get; }
    DbSet<FraudCheck> FraudChecks { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<OutboxMessage> OutboxMessages { get; }
    DbSet<InboxMessage> InboxMessages { get; }
    DbSet<IdempotencyRecord> IdempotencyRecords { get; }
    DbSet<AiPendingAction> AiPendingActions { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>True when the exception is a unique-index violation (e.g. the double-booking guard).</summary>
    bool IsUniqueViolation(DbUpdateException exception);

    /// <summary>Discards tracked changes after a failed save so the context can be reused.</summary>
    void ResetTracking();
}

public interface ICurrentUser
{
    Guid? UserId { get; }
    bool IsAuthenticated { get; }
    IReadOnlyList<string> Roles { get; }
    string? IpAddress { get; }
    string? CorrelationId { get; }
    bool IsInRole(string role);
}

public static class CurrentUserExtensions
{
    public static Guid RequireUserId(this ICurrentUser user) =>
        user.UserId ?? throw new UnauthorizedAccessException("An authenticated user is required.");

    public static bool IsStaff(this ICurrentUser user) =>
        user.IsInRole(Domain.Identity.Roles.Admin) || user.IsInRole(Domain.Identity.Roles.Support);
}
