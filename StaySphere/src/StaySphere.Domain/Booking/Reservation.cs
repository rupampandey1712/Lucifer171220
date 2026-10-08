using StaySphere.Domain.Catalog;
using StaySphere.Domain.Common;
using StaySphere.Domain.Pricing;
using StaySphere.Domain.ValueObjects;

namespace StaySphere.Domain.Booking;

public enum ReservationStatus
{
    Held,
    PaymentPending,
    Confirmed,
    Cancelled,
    Completed,
    RefundPending,
    Refunded,
    Failed,
    Expired,
    /// <summary>Request-to-book: card authorized (not captured), waiting for the host to accept or decline.</summary>
    AwaitingApproval,
    /// <summary>Request-to-book declined by the host or not answered in time. Authorization voided, nothing charged.</summary>
    Declined,
}

public sealed class Reservation : AggregateRoot
{
    public static readonly TimeSpan HoldDuration = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan ApprovalWindow = TimeSpan.FromHours(24);

    private readonly List<ReservationNight> _nightRows = [];
    private readonly List<ReservationStatusChange> _history = [];

    private Reservation() { }

    public Guid PropertyId { get; private set; }
    public Guid GuestId { get; private set; }
    public Guid HostId { get; private set; }
    public DateOnly CheckIn { get; private set; }
    public DateOnly CheckOut { get; private set; }
    public int Guests { get; private set; }
    public int Nights { get; private set; }
    public decimal BaseAmount { get; private set; }
    public decimal CleaningFee { get; private set; }
    public decimal ServiceFee { get; private set; }
    public decimal Taxes { get; private set; }
    public decimal Discount { get; private set; }
    public decimal TotalAmount { get; private set; }
    public string Currency { get; private set; } = default!;
    public string? CouponCode { get; private set; }
    public CancellationPolicy CancellationPolicy { get; private set; }
    public ReservationStatus Status { get; private set; }
    public DateTimeOffset? HoldExpiresAt { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public decimal RefundAmount { get; private set; }
    public string? CancellationReason { get; private set; }
    public string? FailureReason { get; private set; }
    /// <summary>Snapshot of the listing's booking mode at hold time: true = host must accept (request-to-book).</summary>
    public bool RequiresApproval { get; private set; }
    public DateTimeOffset? ApprovalDeadline { get; private set; }
    public string? DeclineReason { get; private set; }

    public IReadOnlyCollection<ReservationNight> NightRows => _nightRows;
    public IReadOnlyCollection<ReservationStatusChange> History => _history;

    public bool HoldsInventory => Status is ReservationStatus.Held or ReservationStatus.PaymentPending or ReservationStatus.Confirmed
        or ReservationStatus.AwaitingApproval;

    /// <summary>
    /// Creates a 10-minute hold. One <see cref="ReservationNight"/> row per night is written; a filtered unique index on
    /// (PropertyId, Night) where IsActive = 1 makes overlapping holds/bookings impossible at the database level.
    /// </summary>
    public static Result<Reservation> Hold(Property property, Guid guestId, DateRange stay, int guests,
        PriceBreakdown price, string? couponCode, DateTimeOffset now)
    {
        if (!property.IsBookable)
            return Error.Conflict("reservation.property_unavailable", "This place is not accepting bookings.");
        if (property.HostId == guestId)
            return Error.Validation("reservation.own_property", "You cannot book your own listing.");
        if (guests < 1 || guests > property.MaxGuests)
            return Error.Validation("reservation.guests", $"This place allows 1 to {property.MaxGuests} guests.");
        if (stay.Start < DateOnly.FromDateTime(now.UtcDateTime))
            return Error.Validation("reservation.past", "Check-in cannot be in the past.");
        if (property.BlockedDates.Any(b => stay.Contains(b.Date)))
            return Error.Conflict("reservation.dates_blocked", "Some of those dates are not available.");

        var reservation = new Reservation
        {
            PropertyId = property.Id,
            GuestId = guestId,
            HostId = property.HostId,
            CheckIn = stay.Start,
            CheckOut = stay.End,
            Guests = guests,
            Nights = stay.Nights,
            BaseAmount = price.Accommodation,
            CleaningFee = price.CleaningFee,
            ServiceFee = price.ServiceFee,
            Taxes = price.Taxes,
            Discount = price.Discount,
            TotalAmount = price.Total,
            Currency = price.Currency,
            CouponCode = couponCode,
            CancellationPolicy = property.CancellationPolicy,
            RequiresApproval = !property.InstantBook,
            Status = ReservationStatus.Held,
            HoldExpiresAt = now.Add(HoldDuration),
            CreatedAt = now,
            UpdatedAt = now,
        };
        foreach (var night in stay.EachNight())
            reservation._nightRows.Add(new ReservationNight(reservation.Id, property.Id, night));
        reservation.Record(null, ReservationStatus.Held, now, guestId, null);
        reservation.Raise(new ReservationHeldDomainEvent(reservation.Id, property.Id, guestId));
        return reservation;
    }

    public Result MarkPaymentPending(DateTimeOffset now)
    {
        if (Status == ReservationStatus.PaymentPending) return Result.Success();
        if (Status != ReservationStatus.Held)
            return Error.Conflict("reservation.state", $"Cannot pay for a reservation that is {Status}.");
        if (HoldExpiresAt <= now)
            return Error.Conflict("reservation.hold_expired", "Your hold has expired. Please start again.");
        Transition(ReservationStatus.PaymentPending, now, GuestId, null);
        return Result.Success();
    }

    /// <summary>Request-to-book: the payment is authorized; the host now has <see cref="ApprovalWindow"/> to respond.</summary>
    public Result AwaitApproval(DateTimeOffset now)
    {
        if (Status == ReservationStatus.AwaitingApproval) return Result.Success();
        if (!RequiresApproval) return Error.Conflict("reservation.instant", "This reservation does not need host approval.");
        if (Status is not (ReservationStatus.PaymentPending or ReservationStatus.Held))
            return Error.Conflict("reservation.state", $"Cannot request approval for a reservation that is {Status}.");
        Transition(ReservationStatus.AwaitingApproval, now, GuestId, null);
        HoldExpiresAt = null;
        ApprovalDeadline = now.Add(ApprovalWindow);
        Raise(new ReservationRequestedDomainEvent(Id, PropertyId, GuestId, HostId, ApprovalDeadline.Value));
        return Result.Success();
    }

    /// <summary>Host declines (or the request times out). Dates are released and nothing is charged.</summary>
    public Result Decline(Guid? actorId, string? reason, DateTimeOffset now)
    {
        if (Status == ReservationStatus.Declined) return Result.Success();
        if (Status != ReservationStatus.AwaitingApproval)
            return Error.Conflict("reservation.state", $"Only pending requests can be declined (this one is {Status}).");
        if (actorId is { } a && a != HostId) return Error.Forbidden("reservation.not_host", "Only the host can respond to this request.");
        ReleaseNights();
        DeclineReason = reason ?? (actorId is null ? "The host did not respond in time." : null);
        ApprovalDeadline = null;
        Transition(ReservationStatus.Declined, now, actorId, DeclineReason);
        Raise(new ReservationDeclinedDomainEvent(Id, PropertyId, GuestId, HostId, actorId is null));
        return Result.Success();
    }

    public Result Confirm(DateTimeOffset now)
    {
        if (Status == ReservationStatus.Confirmed) return Result.Success();
        if (RequiresApproval && Status is ReservationStatus.PaymentPending or ReservationStatus.Held)
            return Error.Conflict("reservation.needs_approval", "The host has to accept this request first.");
        if (Status is not (ReservationStatus.PaymentPending or ReservationStatus.Held or ReservationStatus.AwaitingApproval))
            return Error.Conflict("reservation.state", $"Cannot confirm a reservation that is {Status}.");
        Transition(ReservationStatus.Confirmed, now, null, null);
        ConfirmedAt = now;
        HoldExpiresAt = null;
        ApprovalDeadline = null;
        Raise(new ReservationConfirmedDomainEvent(Id, PropertyId, GuestId, HostId, TotalAmount, Currency));
        return Result.Success();
    }

    public Result Fail(string reason, DateTimeOffset now)
    {
        if (Status is not (ReservationStatus.Held or ReservationStatus.PaymentPending))
            return Error.Conflict("reservation.state", $"Cannot fail a reservation that is {Status}.");
        FailureReason = reason;
        ReleaseNights();
        Transition(ReservationStatus.Failed, now, null, reason);
        Raise(new ReservationFailedDomainEvent(Id, GuestId, reason));
        return Result.Success();
    }

    public Result Expire(DateTimeOffset now)
    {
        if (Status is not (ReservationStatus.Held or ReservationStatus.PaymentPending))
            return Error.Conflict("reservation.state", $"Cannot expire a reservation that is {Status}.");
        ReleaseNights();
        Transition(ReservationStatus.Expired, now, null, "Hold expired");
        Raise(new ReservationExpiredDomainEvent(Id, GuestId));
        return Result.Success();
    }

    /// <summary>Cancels a reservation. Refund amount comes from <see cref="CancellationPolicyCalculator"/> on the server.</summary>
    public Result Cancel(Guid actorId, decimal refundAmount, string? reason, DateTimeOffset now)
    {
        if (Status is ReservationStatus.Held or ReservationStatus.PaymentPending or ReservationStatus.AwaitingApproval)
        {
            // Nothing was captured yet (an approval-pending authorization is voided by the payments handler).
            ReleaseNights();
            Transition(ReservationStatus.Cancelled, now, actorId, reason);
            CancelledAt = now;
            Raise(new ReservationCancelledDomainEvent(Id, PropertyId, GuestId, HostId, 0, Currency));
            return Result.Success();
        }

        if (Status != ReservationStatus.Confirmed)
            return Error.Conflict("reservation.state", $"A reservation that is {Status} cannot be cancelled.");
        if (refundAmount < 0 || refundAmount > TotalAmount)
            return Error.Validation("reservation.refund", "Invalid refund amount.");

        ReleaseNights();
        RefundAmount = refundAmount;
        CancellationReason = reason;
        CancelledAt = now;
        Transition(refundAmount > 0 ? ReservationStatus.RefundPending : ReservationStatus.Cancelled, now, actorId, reason);
        Raise(new ReservationCancelledDomainEvent(Id, PropertyId, GuestId, HostId, refundAmount, Currency));
        return Result.Success();
    }

    public Result MarkRefunded(DateTimeOffset now)
    {
        if (Status == ReservationStatus.Refunded) return Result.Success();
        if (Status != ReservationStatus.RefundPending)
            return Error.Conflict("reservation.state", $"Cannot refund a reservation that is {Status}.");
        Transition(ReservationStatus.Refunded, now, null, null);
        return Result.Success();
    }

    public Result Complete(DateTimeOffset now)
    {
        if (Status != ReservationStatus.Confirmed)
            return Error.Conflict("reservation.state", $"Cannot complete a reservation that is {Status}.");
        if (DateOnly.FromDateTime(now.UtcDateTime) < CheckOut)
            return Error.Conflict("reservation.not_finished", "The stay has not finished yet.");
        Transition(ReservationStatus.Completed, now, null, null);
        Raise(new ReservationCompletedDomainEvent(Id, PropertyId, GuestId, HostId));
        return Result.Success();
    }

    private void ReleaseNights()
    {
        foreach (var n in _nightRows) n.Release();
    }

    private void Transition(ReservationStatus to, DateTimeOffset now, Guid? actor, string? reason)
    {
        Record(Status, to, now, actor, reason);
        Status = to;
        UpdatedAt = now;
    }

    private void Record(ReservationStatus? from, ReservationStatus to, DateTimeOffset now, Guid? actor, string? reason) =>
        _history.Add(new ReservationStatusChange(Id, from, to, now, actor, reason));
}

/// <summary>One row per occupied night. The filtered unique index on (PropertyId, Night) WHERE IsActive=1 is the double-booking guard.</summary>
public sealed class ReservationNight
{
    private ReservationNight() { }

    internal ReservationNight(Guid reservationId, Guid propertyId, DateOnly night)
    {
        ReservationId = reservationId;
        PropertyId = propertyId;
        Night = night;
        IsActive = true;
    }

    public Guid ReservationId { get; private set; }
    public Guid PropertyId { get; private set; }
    public DateOnly Night { get; private set; }
    public bool IsActive { get; private set; }

    internal void Release() => IsActive = false;
}

public sealed class ReservationStatusChange : Entity
{
    private ReservationStatusChange() { }

    internal ReservationStatusChange(Guid reservationId, ReservationStatus? from, ReservationStatus to, DateTimeOffset at, Guid? actorId, string? reason)
    {
        ReservationId = reservationId;
        From = from;
        To = to;
        At = at;
        ActorId = actorId;
        Reason = reason;
    }

    public Guid ReservationId { get; private set; }
    public ReservationStatus? From { get; private set; }
    public ReservationStatus To { get; private set; }
    public DateTimeOffset At { get; private set; }
    public Guid? ActorId { get; private set; }
    public string? Reason { get; private set; }
}

public sealed record ReservationHeldDomainEvent(Guid ReservationId, Guid PropertyId, Guid GuestId) : IDomainEvent;
public sealed record ReservationConfirmedDomainEvent(Guid ReservationId, Guid PropertyId, Guid GuestId, Guid HostId, decimal Total, string Currency) : IDomainEvent;
public sealed record ReservationCancelledDomainEvent(Guid ReservationId, Guid PropertyId, Guid GuestId, Guid HostId, decimal RefundAmount, string Currency) : IDomainEvent;
public sealed record ReservationExpiredDomainEvent(Guid ReservationId, Guid GuestId) : IDomainEvent;
public sealed record ReservationFailedDomainEvent(Guid ReservationId, Guid GuestId, string Reason) : IDomainEvent;
public sealed record ReservationRequestedDomainEvent(Guid ReservationId, Guid PropertyId, Guid GuestId, Guid HostId, DateTimeOffset Deadline) : IDomainEvent;
public sealed record ReservationDeclinedDomainEvent(Guid ReservationId, Guid PropertyId, Guid GuestId, Guid HostId, bool Expired) : IDomainEvent;
public sealed record ReservationCompletedDomainEvent(Guid ReservationId, Guid PropertyId, Guid GuestId, Guid HostId) : IDomainEvent;
