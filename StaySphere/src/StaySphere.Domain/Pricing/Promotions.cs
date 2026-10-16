using StaySphere.Domain.Catalog;
using StaySphere.Domain.Common;
using StaySphere.Domain.ValueObjects;

namespace StaySphere.Domain.Pricing;

public enum PromotionKind
{
    /// <summary>Booked at least <see cref="Promotion.DaysBeforeCheckIn"/> days ahead.</summary>
    EarlyBird,
    /// <summary>Booked at most <see cref="Promotion.DaysBeforeCheckIn"/> days ahead.</summary>
    LastMinute,
    /// <summary>Stays of at least <see cref="Promotion.MinNights"/> nights.</summary>
    LongStay,
    /// <summary>Stays that start inside the stay window.</summary>
    FlashSale,
}

/// <summary>
/// An automatic discount (no code needed). Hosts create promotions for one listing or all their listings; admins create
/// platform-wide ones (<see cref="HostId"/> = null). The price calculator applies the single best eligible promotion; promotions
/// never stack with each other, but do combine with length-of-stay discounts and coupons.
/// </summary>
public sealed class Promotion : AggregateRoot
{
    public const decimal MaxPercentOff = 50m;

    private Promotion() { }

    public string Name { get; private set; } = default!;
    public PromotionKind Kind { get; private set; }
    public decimal PercentOff { get; private set; }
    /// <summary>Null = platform-wide (admin promotion).</summary>
    public Guid? HostId { get; private set; }
    /// <summary>Null = every listing of <see cref="HostId"/> (or every listing for platform promotions).</summary>
    public Guid? PropertyId { get; private set; }
    public int? MinNights { get; private set; }
    public int? DaysBeforeCheckIn { get; private set; }
    /// <summary>Optional window the stay must start in (flash sales, seasonal deals).</summary>
    public DateOnly? StayStart { get; private set; }
    public DateOnly? StayEnd { get; private set; }
    /// <summary>When the promotion can be booked.</summary>
    public DateTimeOffset ValidFrom { get; private set; }
    public DateTimeOffset ValidTo { get; private set; }
    public bool IsActive { get; private set; } = true;
    public Guid CreatedBy { get; private set; }

    public static Result<Promotion> Create(string name, PromotionKind kind, decimal percentOff, Guid? hostId, Guid? propertyId,
        int? minNights, int? daysBeforeCheckIn, DateOnly? stayStart, DateOnly? stayEnd, DateTimeOffset validFrom, DateTimeOffset validTo,
        Guid createdBy, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 80)
            return Error.Validation("promotion.name", "Give the promotion a name of up to 80 characters.");
        if (percentOff is <= 0 or > MaxPercentOff)
            return Error.Validation("promotion.percent", $"Discount must be between 1% and {MaxPercentOff:0}%.");
        if (validTo <= validFrom) return Error.Validation("promotion.window", "The promotion must end after it starts.");
        if (validTo < now) return Error.Validation("promotion.expired", "The promotion would already be over.");
        if (stayStart is not null && stayEnd is not null && stayEnd <= stayStart)
            return Error.Validation("promotion.stay_window", "The stay window must end after it starts.");

        var missing = kind switch
        {
            PromotionKind.EarlyBird or PromotionKind.LastMinute when daysBeforeCheckIn is not (>= 0 and <= 365) =>
                "Set how many days before check-in (0–365).",
            PromotionKind.LongStay when minNights is not (>= 2 and <= 90) => "Set the minimum number of nights (2–90).",
            PromotionKind.FlashSale when stayStart is null || stayEnd is null => "Set the stay window for the flash sale.",
            _ => null,
        };
        if (missing is not null) return Error.Validation("promotion.rule", missing);

        return new Promotion
        {
            Name = name.Trim(), Kind = kind, PercentOff = percentOff, HostId = hostId, PropertyId = propertyId,
            MinNights = minNights, DaysBeforeCheckIn = daysBeforeCheckIn, StayStart = stayStart, StayEnd = stayEnd,
            ValidFrom = validFrom, ValidTo = validTo, CreatedBy = createdBy, CreatedAt = now, UpdatedAt = now,
        };
    }

    public bool AppliesTo(Property property, DateRange stay, DateTimeOffset now)
    {
        if (!IsActive || now < ValidFrom || now > ValidTo) return false;
        if (HostId is not null && HostId != property.HostId) return false;
        if (PropertyId is not null && PropertyId != property.Id) return false;
        if (MinNights is not null && stay.Nights < MinNights) return false;
        if (StayStart is not null && stay.Start < StayStart) return false;
        if (StayEnd is not null && stay.Start >= StayEnd) return false;

        var daysAhead = stay.Start.DayNumber - DateOnly.FromDateTime(now.UtcDateTime).DayNumber;
        return Kind switch
        {
            PromotionKind.EarlyBird => daysAhead >= DaysBeforeCheckIn,
            PromotionKind.LastMinute => daysAhead >= 0 && daysAhead <= DaysBeforeCheckIn,
            _ => true,
        };
    }

    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        UpdatedAt = now;
    }
}
