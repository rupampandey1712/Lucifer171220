using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using StaySphere.Application.Abstractions;
using StaySphere.Domain.Booking;
using StaySphere.Domain.Catalog;
using StaySphere.Domain.Engagement;
using StaySphere.Domain.Identity;
using StaySphere.Domain.Payments;
using StaySphere.Domain.Platform;
using StaySphere.Domain.Pricing;
using StaySphere.Domain.Reviews;
using StaySphere.Domain.Trust;

namespace StaySphere.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<UserToken> UserTokens => Set<UserToken>();
    public DbSet<Property> Properties => Set<Property>();
    public DbSet<Amenity> Amenities => Set<Amenity>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<ReservationNight> ReservationNights => Set<ReservationNight>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<PayoutAccount> PayoutAccounts => Set<PayoutAccount>();
    public DbSet<HostPayout> HostPayouts => Set<HostPayout>();
    public DbSet<WebhookEvent> WebhookEvents => Set<WebhookEvent>();
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Favorite> Favorites => Set<Favorite>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<SupportTicket> SupportTickets => Set<SupportTicket>();
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<FraudCheck> FraudChecks => Set<FraudCheck>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<AiPendingAction> AiPendingActions => Set<AiPendingAction>();

    public bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };

    public void ResetTracking() => ChangeTracker.Clear();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Ids are generated client-side (UUIDv7) by the domain. Telling EF they are never store-generated makes new
        // children added to a tracked aggregate's collection get INSERTed (not mistaken for existing rows).
        foreach (var entity in modelBuilder.Model.GetEntityTypes().Where(e => typeof(Domain.Common.Entity).IsAssignableFrom(e.ClrType)))
            entity.FindProperty(nameof(Domain.Common.Entity.Id))?.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<decimal>().HavePrecision(19, 4);
        configurationBuilder.Properties<string>().HaveMaxLength(400);
    }
}
