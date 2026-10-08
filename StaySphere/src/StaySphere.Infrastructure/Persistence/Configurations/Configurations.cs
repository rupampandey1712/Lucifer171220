using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StaySphere.Domain.Booking;
using StaySphere.Domain.Catalog;
using StaySphere.Domain.Engagement;
using StaySphere.Domain.Identity;
using StaySphere.Domain.Payments;
using StaySphere.Domain.Platform;
using StaySphere.Domain.Pricing;
using StaySphere.Domain.Reviews;
using StaySphere.Domain.Trust;

namespace StaySphere.Infrastructure.Persistence.Configurations;

// One schema per bounded context keeps module data boundaries explicit and extractable (ADR-001).

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("Users", "identity");
        b.Property(x => x.Email).HasMaxLength(256);
        b.Property(x => x.NormalizedEmail).HasMaxLength(256);
        b.HasIndex(x => x.NormalizedEmail).IsUnique();
        b.Property(x => x.PasswordHash).HasMaxLength(512);
        b.Property(x => x.DisplayName).HasMaxLength(80);
        b.Property(x => x.Bio).HasMaxLength(1000);
        b.Property(x => x.AvatarUrl).HasMaxLength(1000);
        b.Property(x => x.PreferredCurrency).HasMaxLength(3).IsFixedLength();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.SecurityStamp).HasMaxLength(64);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasMany(x => x.Roles).WithOne().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Roles).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(x => x.DomainEvents);
    }
}

internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> b)
    {
        b.ToTable("UserRoles", "identity");
        b.HasKey(x => new { x.UserId, x.Role });
        b.Property(x => x.Role).HasMaxLength(20);
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("RefreshTokens", "identity");
        b.Property(x => x.TokenHash).HasMaxLength(64);
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => new { x.UserId, x.FamilyId });
        b.Property(x => x.CreatedByIp).HasMaxLength(64);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UserTokenConfiguration : IEntityTypeConfiguration<UserToken>
{
    public void Configure(EntityTypeBuilder<UserToken> b)
    {
        b.ToTable("UserTokens", "identity");
        b.Property(x => x.TokenHash).HasMaxLength(64);
        b.Property(x => x.Purpose).HasConversion<string>().HasMaxLength(30);
        b.HasIndex(x => new { x.UserId, x.Purpose, x.TokenHash });
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PropertyConfiguration : IEntityTypeConfiguration<Property>
{
    public void Configure(EntityTypeBuilder<Property> b)
    {
        b.ToTable("Properties", "catalog", t =>
        {
            t.HasCheckConstraint("CK_Properties_MaxGuests", "[MaxGuests] BETWEEN 1 AND 50");
            t.HasCheckConstraint("CK_Properties_BasePrice", "[BasePrice] >= 0");
            t.HasCheckConstraint("CK_Properties_CleaningFee", "[CleaningFee] >= 0");
        });
        b.Property(x => x.Title).HasMaxLength(120);
        b.Property(x => x.Description).HasMaxLength(5000);
        b.Property(x => x.HouseRules).HasMaxLength(2000);
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
        b.Property(x => x.PropertyType).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.RoomType).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.CancellationPolicy).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Bathrooms).HasPrecision(4, 1);
        b.Property(x => x.WeekendAdjustmentPercent).HasPrecision(6, 2);
        b.Property(x => x.WeeklyDiscountPercent).HasPrecision(5, 2);
        b.Property(x => x.MonthlyDiscountPercent).HasPrecision(5, 2);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.OwnsOne(x => x.Address, a =>
        {
            a.Property(p => p.Line1).HasMaxLength(200).HasColumnName("AddressLine1");
            a.Property(p => p.Line2).HasMaxLength(200).HasColumnName("AddressLine2");
            a.Property(p => p.City).HasMaxLength(100).HasColumnName("City");
            a.Property(p => p.Region).HasMaxLength(100).HasColumnName("Region");
            a.Property(p => p.PostalCode).HasMaxLength(20).HasColumnName("PostalCode");
            a.Property(p => p.CountryCode).HasMaxLength(2).HasColumnName("CountryCode");
            a.Property(p => p.Country).HasMaxLength(100).HasColumnName("Country");
            a.HasIndex(p => new { p.CountryCode, p.City });
        });
        b.HasOne<User>().WithMany().HasForeignKey(x => x.HostId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.Status, x.BasePrice });
        b.HasIndex(x => new { x.Status, x.RatingAverage });
        b.HasIndex(x => new { x.Latitude, x.Longitude });
        b.HasIndex(x => x.HostId);
        b.HasIndex(x => new { x.Status, x.PublishedAt });

        b.HasMany(x => x.Images).WithOne().HasForeignKey(i => i.PropertyId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Amenities).WithOne().HasForeignKey(a => a.PropertyId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.SeasonalPrices).WithOne().HasForeignKey(s => s.PropertyId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.BlockedDates).WithOne().HasForeignKey(s => s.PropertyId).OnDelete(DeleteBehavior.Cascade);
        foreach (var nav in new[] { nameof(Property.Images), nameof(Property.Amenities), nameof(Property.SeasonalPrices), nameof(Property.BlockedDates) })
            b.Navigation(nav).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(x => x.DomainEvents);
        b.Ignore(x => x.IsBookable);
    }
}

internal sealed class PropertyImageConfiguration : IEntityTypeConfiguration<PropertyImage>
{
    public void Configure(EntityTypeBuilder<PropertyImage> b)
    {
        b.ToTable("PropertyImages", "catalog");
        b.Property(x => x.Url).HasMaxLength(1000);
        b.Property(x => x.ThumbnailUrl).HasMaxLength(1000);
        b.Property(x => x.BlobKey).HasMaxLength(300);
        b.HasIndex(x => new { x.PropertyId, x.SortOrder });
    }
}

internal sealed class PropertyAmenityConfiguration : IEntityTypeConfiguration<PropertyAmenity>
{
    public void Configure(EntityTypeBuilder<PropertyAmenity> b)
    {
        b.ToTable("PropertyAmenities", "catalog");
        b.HasKey(x => new { x.PropertyId, x.AmenityCode });
        b.Property(x => x.AmenityCode).HasMaxLength(40);
        b.HasOne<Amenity>().WithMany().HasForeignKey(x => x.AmenityCode).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.AmenityCode);
    }
}

internal sealed class AmenityConfiguration : IEntityTypeConfiguration<Amenity>
{
    public void Configure(EntityTypeBuilder<Amenity> b)
    {
        b.ToTable("Amenities", "catalog");
        b.HasKey(x => x.Code);
        b.Property(x => x.Code).HasMaxLength(40);
        b.Property(x => x.Name).HasMaxLength(80);
        b.Property(x => x.Category).HasMaxLength(40);
        b.Property(x => x.Icon).HasMaxLength(40);
        b.HasData(AmenityCatalog.All);
    }
}

internal sealed class SeasonalPriceConfiguration : IEntityTypeConfiguration<SeasonalPrice>
{
    public void Configure(EntityTypeBuilder<SeasonalPrice> b)
    {
        b.ToTable("SeasonalPricing", "pricing", t => t.HasCheckConstraint("CK_SeasonalPricing_Dates", "[End] > [Start]"));
        b.HasIndex(x => new { x.PropertyId, x.Start });
    }
}

internal sealed class BlockedDateConfiguration : IEntityTypeConfiguration<BlockedDate>
{
    public void Configure(EntityTypeBuilder<BlockedDate> b)
    {
        b.ToTable("BlockedDates", "pricing");
        b.HasKey(x => new { x.PropertyId, x.Date });
    }
}

internal sealed class CouponConfiguration : IEntityTypeConfiguration<Coupon>
{
    public void Configure(EntityTypeBuilder<Coupon> b)
    {
        b.ToTable("Coupons", "pricing", t => t.HasCheckConstraint("CK_Coupons_Percent", "[PercentOff] BETWEEN 0 AND 100"));
        b.Property(x => x.Code).HasMaxLength(40);
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.PercentOff).HasPrecision(5, 2);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.Ignore(x => x.DomainEvents);
    }
}

internal sealed class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> b)
    {
        b.ToTable("Reservations", "booking", t =>
        {
            t.HasCheckConstraint("CK_Reservations_Dates", "[CheckOut] > [CheckIn]");
            t.HasCheckConstraint("CK_Reservations_Guests", "[Guests] >= 1");
            t.HasCheckConstraint("CK_Reservations_Total", "[TotalAmount] >= 0");
        });
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
        b.Property(x => x.CouponCode).HasMaxLength(40);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.CancellationPolicy).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.CancellationReason).HasMaxLength(500);
        b.Property(x => x.FailureReason).HasMaxLength(500);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne<Property>().WithMany().HasForeignKey(x => x.PropertyId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.GuestId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.GuestId, x.CheckIn });
        b.HasIndex(x => new { x.HostId, x.CheckIn });
        b.HasIndex(x => new { x.PropertyId, x.Status });
        b.HasIndex(x => new { x.Status, x.HoldExpiresAt }).HasFilter("[Status] IN ('Held','PaymentPending')");
        b.HasMany(x => x.NightRows).WithOne().HasForeignKey(n => n.ReservationId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.History).WithOne().HasForeignKey(h => h.ReservationId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.NightRows).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(x => x.History).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(x => x.DomainEvents);
        b.Ignore(x => x.HoldsInventory);
    }
}

internal sealed class ReservationNightConfiguration : IEntityTypeConfiguration<ReservationNight>
{
    public void Configure(EntityTypeBuilder<ReservationNight> b)
    {
        b.ToTable("ReservationNights", "booking");
        b.HasKey(x => new { x.ReservationId, x.Night });
        // THE double-booking guard: at most one active reservation may occupy a property-night.
        b.HasIndex(x => new { x.PropertyId, x.Night }).IsUnique().HasFilter("[IsActive] = 1").HasDatabaseName("UX_ReservationNights_ActivePropertyNight");
    }
}

internal sealed class ReservationStatusChangeConfiguration : IEntityTypeConfiguration<ReservationStatusChange>
{
    public void Configure(EntityTypeBuilder<ReservationStatusChange> b)
    {
        b.ToTable("ReservationStatusHistory", "booking");
        b.Property(x => x.From).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.To).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Reason).HasMaxLength(500);
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.ToTable("Payments", "payments", t => t.HasCheckConstraint("CK_Payments_Amount", "[Amount] >= 0"));
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Provider).HasMaxLength(40);
        b.Property(x => x.ProviderPaymentId).HasMaxLength(100);
        b.Property(x => x.CardLast4).HasMaxLength(4);
        b.Property(x => x.FailureReason).HasMaxLength(500);
        b.Property(x => x.IdempotencyKey).HasMaxLength(100);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.Provider, x.ProviderPaymentId }).IsUnique().HasFilter("[ProviderPaymentId] IS NOT NULL");
        b.HasIndex(x => new { x.PayerId, x.IdempotencyKey }).IsUnique();
        // At most one live (pending or succeeded) payment per reservation.
        b.HasIndex(x => x.ReservationId).IsUnique().HasFilter("[Status] IN ('Pending','Succeeded','PartiallyRefunded','Refunded')")
            .HasDatabaseName("UX_Payments_LiveReservation");
        b.HasIndex(x => new { x.Status, x.CreatedAt });
        b.HasOne<Reservation>().WithMany().HasForeignKey(x => x.ReservationId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Transactions).WithOne().HasForeignKey(t => t.PaymentId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Refunds).WithOne().HasForeignKey(t => t.PaymentId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Transactions).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(x => x.Refunds).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(x => x.DomainEvents);
        b.Ignore(x => x.RefundedAmount);
    }
}

internal sealed class PaymentTransactionConfiguration : IEntityTypeConfiguration<PaymentTransaction>
{
    public void Configure(EntityTypeBuilder<PaymentTransaction> b)
    {
        b.ToTable("PaymentTransactions", "payments");
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.ProviderReference).HasMaxLength(100);
    }
}

internal sealed class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> b)
    {
        b.ToTable("Refunds", "payments", t => t.HasCheckConstraint("CK_Refunds_Amount", "[Amount] > 0"));
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Reason).HasMaxLength(500);
        b.Property(x => x.ProviderRefundId).HasMaxLength(100);
    }
}

internal sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> b)
    {
        b.ToTable("LedgerEntries", "payments");
        b.Property(x => x.Account).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
        b.Property(x => x.Description).HasMaxLength(200);
        b.HasIndex(x => new { x.HostId, x.OccurredAt });
        b.HasIndex(x => x.ReservationId);
    }
}

internal sealed class WebhookEventConfiguration : IEntityTypeConfiguration<WebhookEvent>
{
    public void Configure(EntityTypeBuilder<WebhookEvent> b)
    {
        b.ToTable("WebhookEvents", "payments");
        b.Property(x => x.Provider).HasMaxLength(40);
        b.Property(x => x.EventId).HasMaxLength(100);
        b.Property(x => x.Type).HasMaxLength(60);
        b.Property(x => x.Payload).HasMaxLength(8000);
        b.HasIndex(x => new { x.Provider, x.EventId }).IsUnique(); // replay protection
    }
}

internal sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> b)
    {
        b.ToTable("Reviews", "reviews", t =>
        {
            foreach (var c in new[] { "Overall", "Cleanliness", "Accuracy", "Communication", "Location", "CheckIn", "Value" })
                t.HasCheckConstraint($"CK_Reviews_{c}", $"[{c}] BETWEEN 1 AND 5");
        });
        b.HasIndex(x => x.ReservationId).IsUnique(); // one review per stay
        b.HasIndex(x => new { x.PropertyId, x.Status, x.CreatedAt });
        b.Property(x => x.Comment).HasMaxLength(2000);
        b.Property(x => x.HostResponse).HasMaxLength(1000);
        b.Property(x => x.ModerationNote).HasMaxLength(500);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne<Reservation>().WithMany().HasForeignKey(x => x.ReservationId).OnDelete(DeleteBehavior.Restrict);
        b.Ignore(x => x.DomainEvents);
    }
}

internal sealed class FavoriteConfiguration : IEntityTypeConfiguration<Favorite>
{
    public void Configure(EntityTypeBuilder<Favorite> b)
    {
        b.ToTable("Favorites", "engagement");
        b.HasKey(x => new { x.UserId, x.PropertyId });
        b.HasOne<Property>().WithMany().HasForeignKey(x => x.PropertyId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> b)
    {
        b.ToTable("Conversations", "engagement");
        b.HasIndex(x => new { x.GuestId, x.LastMessageAt });
        b.HasIndex(x => new { x.HostId, x.LastMessageAt });
        b.HasIndex(x => new { x.PropertyId, x.GuestId }).IsUnique();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasMany(x => x.Messages).WithOne().HasForeignKey(m => m.ConversationId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Messages).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(x => x.DomainEvents);
    }
}

internal sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> b)
    {
        b.ToTable("Messages", "engagement");
        b.Property(x => x.Body).HasMaxLength(4000);
        b.HasIndex(x => new { x.ConversationId, x.SentAt });
    }
}

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ToTable("Notifications", "engagement");
        b.Property(x => x.Type).HasMaxLength(60);
        b.Property(x => x.Title).HasMaxLength(200);
        b.Property(x => x.Body).HasMaxLength(1000);
        b.Property(x => x.Link).HasMaxLength(300);
        b.HasIndex(x => new { x.UserId, x.IsRead, x.CreatedAt });
    }
}

internal sealed class SupportTicketConfiguration : IEntityTypeConfiguration<SupportTicket>
{
    public void Configure(EntityTypeBuilder<SupportTicket> b)
    {
        b.ToTable("SupportTickets", "trust");
        b.Property(x => x.Category).HasMaxLength(50);
        b.Property(x => x.Subject).HasMaxLength(200);
        b.Property(x => x.Description).HasMaxLength(4000);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Priority).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.Status, x.Priority });
        b.HasIndex(x => x.UserId);
        b.HasMany(x => x.Messages).WithOne().HasForeignKey(m => m.TicketId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Messages).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(x => x.DomainEvents);
    }
}

internal sealed class TicketMessageConfiguration : IEntityTypeConfiguration<TicketMessage>
{
    public void Configure(EntityTypeBuilder<TicketMessage> b)
    {
        b.ToTable("TicketMessages", "trust");
        b.Property(x => x.Body).HasMaxLength(4000);
    }
}

internal sealed class ReportConfiguration : IEntityTypeConfiguration<Report>
{
    public void Configure(EntityTypeBuilder<Report> b)
    {
        b.ToTable("Reports", "trust");
        b.Property(x => x.TargetType).HasMaxLength(30);
        b.Property(x => x.Reason).HasMaxLength(1000);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(x => new { x.Status, x.CreatedAt });
    }
}

internal sealed class FraudCheckConfiguration : IEntityTypeConfiguration<FraudCheck>
{
    public void Configure(EntityTypeBuilder<FraudCheck> b)
    {
        b.ToTable("FraudChecks", "trust");
        b.Property(x => x.SubjectType).HasMaxLength(30);
        b.Property(x => x.RuleCode).HasMaxLength(60);
        b.Property(x => x.Details).HasMaxLength(500);
        b.Property(x => x.Decision).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(x => new { x.SubjectId, x.RuleCode, x.CreatedAt });
    }
}

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("AuditLogs", "platform");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).UseIdentityColumn();
        b.Property(x => x.Action).HasMaxLength(80);
        b.Property(x => x.EntityType).HasMaxLength(60);
        b.Property(x => x.EntityId).HasMaxLength(60);
        b.Property(x => x.Details).HasMaxLength(4000);
        b.Property(x => x.IpAddress).HasMaxLength(64);
        b.Property(x => x.CorrelationId).HasMaxLength(64);
        b.HasIndex(x => new { x.Action, x.At });
        b.HasIndex(x => new { x.ActorId, x.At });
    }
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.ToTable("OutboxMessages", "platform");
        b.HasKey(x => x.Id);
        b.Property(x => x.Type).HasMaxLength(100);
        b.Property(x => x.Payload).HasMaxLength(-1);
        b.Property(x => x.CorrelationId).HasMaxLength(64);
        b.Property(x => x.LastError).HasMaxLength(2000);
        b.Property<DateTimeOffset?>("LockedUntil");
        b.HasIndex(x => x.OccurredAt).HasFilter("[ProcessedAt] IS NULL AND [FailedAt] IS NULL").HasDatabaseName("IX_OutboxMessages_Pending");
    }
}

internal sealed class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> b)
    {
        b.ToTable("InboxMessages", "platform");
        b.HasKey(x => new { x.MessageId, x.Consumer });
        b.Property(x => x.Consumer).HasMaxLength(100);
    }
}

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> b)
    {
        b.ToTable("IdempotencyRecords", "platform");
        b.HasKey(x => new { x.Scope, x.UserId, x.Key });
        b.Property(x => x.Scope).HasMaxLength(100);
        b.Property(x => x.Key).HasMaxLength(100);
        b.Property(x => x.RequestHash).HasMaxLength(64);
        b.Property(x => x.ResponseBody).HasMaxLength(-1);
        b.HasIndex(x => x.ExpiresAt);
        b.Ignore(x => x.IsCompleted);
    }
}

internal sealed class AiPendingActionConfiguration : IEntityTypeConfiguration<AiPendingAction>
{
    public void Configure(EntityTypeBuilder<AiPendingAction> b)
    {
        b.ToTable("AiPendingActions", "platform");
        b.HasKey(x => x.Id);
        b.Property(x => x.ToolName).HasMaxLength(60);
        b.Property(x => x.ArgumentsJson).HasMaxLength(2000);
        b.Property(x => x.Summary).HasMaxLength(500);
        b.Property(x => x.ResultJson).HasMaxLength(2000);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(x => new { x.UserId, x.CreatedAt });
    }
}

public static class AmenityCatalog
{
    public static readonly Amenity[] All =
    [
        new("wifi", "Wi-Fi", "Essentials", "wifi"),
        new("kitchen", "Kitchen", "Essentials", "chef-hat"),
        new("washer", "Washing machine", "Essentials", "washing-machine"),
        new("dryer", "Dryer", "Essentials", "wind"),
        new("air-conditioning", "Air conditioning", "Essentials", "snowflake"),
        new("heating", "Heating", "Essentials", "flame"),
        new("workspace", "Dedicated workspace", "Essentials", "laptop"),
        new("tv", "TV", "Essentials", "tv"),
        new("parking", "Free parking", "Parking", "car"),
        new("ev-charger", "EV charger", "Parking", "plug-zap"),
        new("pool", "Pool", "Outdoor", "waves"),
        new("hot-tub", "Hot tub", "Outdoor", "bath"),
        new("bbq", "BBQ grill", "Outdoor", "beef"),
        new("garden", "Garden", "Outdoor", "trees"),
        new("beachfront", "Beachfront", "Location", "umbrella"),
        new("lake-access", "Lake access", "Location", "sailboat"),
        new("ski-in-out", "Ski-in/ski-out", "Location", "mountain-snow"),
        new("gym", "Gym", "Facilities", "dumbbell"),
        new("elevator", "Elevator", "Facilities", "arrow-up-down"),
        new("fireplace", "Indoor fireplace", "Facilities", "flame-kindling"),
        new("crib", "Crib", "Family", "baby"),
        new("high-chair", "High chair", "Family", "baby"),
        new("step-free", "Step-free access", "Accessibility", "accessibility"),
        new("smoke-alarm", "Smoke alarm", "Safety", "siren"),
        new("first-aid", "First aid kit", "Safety", "cross"),
        new("self-check-in", "Self check-in", "Services", "key-round"),
    ];
}
