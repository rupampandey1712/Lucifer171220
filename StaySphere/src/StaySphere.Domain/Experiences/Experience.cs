using StaySphere.Domain.Common;

namespace StaySphere.Domain.Experiences;

public enum ExperienceCategory
{
    Food,
    Culture,
    Outdoors,
    Wellness,
    Art,
    Nightlife,
}

public enum ExperienceStatus
{
    Draft,
    Published,
    Unlisted,
}

/// <summary>
/// A hosted activity (a food tour, a pottery class) sold per guest in scheduled <see cref="ExperienceSlot"/>s.
/// The price is the host's per-guest price; totals are always computed here, never taken from the client.
/// </summary>
public sealed class Experience : AggregateRoot
{
    public const int MinDurationMinutes = 30;
    public const int MaxDurationMinutes = 12 * 60;
    public const int MaxGroupSize = 30;

    private readonly List<ExperienceSlot> _slots = [];

    private Experience() { }

    public Guid HostId { get; private set; }
    public string Title { get; private set; } = default!;
    public string Description { get; private set; } = default!;
    public ExperienceCategory Category { get; private set; }
    public string City { get; private set; } = default!;
    public string CountryCode { get; private set; } = default!;
    public string MeetingPoint { get; private set; } = default!;
    public int DurationMinutes { get; private set; }
    public int MaxGuests { get; private set; }
    public decimal PricePerGuest { get; private set; }
    public string Currency { get; private set; } = "USD";
    public string? ImageUrl { get; private set; }
    public ExperienceStatus Status { get; private set; } = ExperienceStatus.Draft;
    public IReadOnlyCollection<ExperienceSlot> Slots => _slots;

    public static Result<Experience> Create(Guid hostId, string title, string description, ExperienceCategory category, string city,
        string countryCode, string meetingPoint, int durationMinutes, int maxGuests, decimal pricePerGuest, string currency, string? imageUrl,
        DateTimeOffset now)
    {
        if (durationMinutes is < MinDurationMinutes or > MaxDurationMinutes)
            return Error.Validation("experience.duration", $"Experiences last between {MinDurationMinutes} minutes and {MaxDurationMinutes / 60} hours.");
        if (maxGuests is < 1 or > MaxGroupSize) return Error.Validation("experience.group", $"Groups are 1 to {MaxGroupSize} guests.");
        if (pricePerGuest <= 0) return Error.Validation("experience.price", "Set a price per guest.");
        return new Experience
        {
            HostId = hostId, Title = title.Trim(), Description = description.Trim(), Category = category, City = city.Trim(),
            CountryCode = countryCode.ToUpperInvariant(), MeetingPoint = meetingPoint.Trim(), DurationMinutes = durationMinutes,
            MaxGuests = maxGuests, PricePerGuest = decimal.Round(pricePerGuest, 2), Currency = currency.ToUpperInvariant(), ImageUrl = imageUrl,
            CreatedAt = now, UpdatedAt = now,
        };
    }

    public Result Publish(DateTimeOffset now)
    {
        if (!_slots.Any(s => s.StartsAt > now && !s.Cancelled))
            return Error.Validation("experience.no_slots", "Add at least one upcoming time slot before publishing.");
        Status = ExperienceStatus.Published;
        return Result.Success();
    }

    public void Unlist() => Status = ExperienceStatus.Unlisted;

    /// <summary>Schedules a session. Capacity defaults to the group size; sessions may not overlap (one host, one place).</summary>
    public Result<ExperienceSlot> AddSlot(DateTimeOffset startsAt, int? capacity, DateTimeOffset now)
    {
        if (startsAt <= now.AddHours(2)) return Error.Validation("experience.slot_past", "Slots must start at least 2 hours from now.");
        if (startsAt > now.AddDays(365)) return Error.Validation("experience.slot_far", "Slots can be scheduled up to a year ahead.");
        var seats = capacity ?? MaxGuests;
        if (seats < 1 || seats > MaxGuests) return Error.Validation("experience.slot_capacity", $"Capacity must be between 1 and {MaxGuests}.");
        var endsAt = startsAt.AddMinutes(DurationMinutes);
        if (_slots.Any(s => !s.Cancelled && s.StartsAt < endsAt && startsAt < s.StartsAt.AddMinutes(DurationMinutes)))
            return Error.Conflict("experience.slot_overlap", "This time overlaps another session.");
        var slot = new ExperienceSlot(Id, startsAt, seats);
        _slots.Add(slot);
        return slot;
    }

    public decimal TotalFor(int guests) => decimal.Round(PricePerGuest * guests, 2);
}

/// <summary>
/// One scheduled session. <see cref="Booked"/> is only ever changed by a single conditional UPDATE
/// (<c>Booked + n &lt;= Capacity</c>), so concurrent bookings can't oversell it.
/// </summary>
public sealed class ExperienceSlot : Entity
{
    private ExperienceSlot() { }

    internal ExperienceSlot(Guid experienceId, DateTimeOffset startsAt, int capacity)
    {
        ExperienceId = experienceId;
        StartsAt = startsAt;
        Capacity = capacity;
    }

    public Guid ExperienceId { get; private set; }
    public DateTimeOffset StartsAt { get; private set; }
    public int Capacity { get; private set; }
    public int Booked { get; private set; }
    public bool Cancelled { get; private set; }
    public int Remaining => Math.Max(0, Capacity - Booked);
}

public enum ExperienceBookingStatus
{
    Confirmed,
    Cancelled,
}

/// <summary>A guest's seats in one slot. The price is fixed at booking time.</summary>
public sealed class ExperienceBooking : AggregateRoot
{
    /// <summary>Free cancellation until this long before the start.</summary>
    public static readonly TimeSpan FreeCancellation = TimeSpan.FromHours(24);

    private ExperienceBooking() { }

    public Guid ExperienceId { get; private set; }
    public Guid SlotId { get; private set; }
    public Guid GuestId { get; private set; }
    public Guid HostId { get; private set; }
    public int Guests { get; private set; }
    public decimal Total { get; private set; }
    public string Currency { get; private set; } = default!;
    public DateTimeOffset StartsAt { get; private set; }
    public ExperienceBookingStatus Status { get; private set; } = ExperienceBookingStatus.Confirmed;
    public DateTimeOffset? CancelledAt { get; private set; }

    public static ExperienceBooking Create(Experience experience, ExperienceSlot slot, Guid guestId, int guests, DateTimeOffset now) => new()
    {
        ExperienceId = experience.Id, SlotId = slot.Id, GuestId = guestId, HostId = experience.HostId, Guests = guests,
        Total = experience.TotalFor(guests), Currency = experience.Currency, StartsAt = slot.StartsAt, CreatedAt = now, UpdatedAt = now,
    };

    public Result Cancel(DateTimeOffset now)
    {
        if (Status == ExperienceBookingStatus.Cancelled) return Error.Conflict("experience_booking.cancelled", "This booking is already cancelled.");
        if (now > StartsAt - FreeCancellation)
            return Error.Validation("experience_booking.too_late", "Bookings can be cancelled up to 24 hours before the start.");
        Status = ExperienceBookingStatus.Cancelled;
        CancelledAt = now;
        return Result.Success();
    }
}
