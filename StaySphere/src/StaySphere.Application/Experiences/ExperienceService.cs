using Microsoft.EntityFrameworkCore;
using StaySphere.Application.Abstractions;
using StaySphere.Application.Common;
using StaySphere.Application.Engagement;
using StaySphere.Contracts.Experiences;
using StaySphere.Domain.Common;
using StaySphere.Domain.Experiences;
using StaySphere.Domain.Identity;

namespace StaySphere.Application.Experiences;

public interface IExperienceService
{
    Task<IReadOnlyList<ExperienceCardDto>> SearchAsync(string? city, string? category, DateOnly? date, int? guests, CancellationToken ct);
    Task<ExperienceDetailDto?> GetAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<ExperienceDetailDto>> ListMineAsync(CancellationToken ct);
    Task<Result<ExperienceDetailDto>> CreateAsync(CreateExperienceRequest request, CancellationToken ct);
    Task<Result<ExperienceSlotDto>> AddSlotAsync(Guid experienceId, AddExperienceSlotRequest request, CancellationToken ct);
    Task<Result> PublishAsync(Guid experienceId, CancellationToken ct);
    Task<Result<ExperienceBookingDto>> BookAsync(Guid experienceId, BookExperienceRequest request, CancellationToken ct);
    Task<IReadOnlyList<ExperienceBookingDto>> MyBookingsAsync(CancellationToken ct);
    Task<Result<ExperienceBookingDto>> CancelBookingAsync(Guid bookingId, CancellationToken ct);
}

public sealed class ExperienceService(IAppDbContext db, ICurrentUser currentUser, IAuditLogger audit, INotificationService notifications,
    TimeProvider clock) : IExperienceService
{
    /// <summary>Bookings close this long before a session starts, so the host knows who is coming.</summary>
    public static readonly TimeSpan BookingCutoff = TimeSpan.FromHours(1);

    private static readonly Error NotFound = Error.NotFound("experience.not_found", "Experience not found.");

    public async Task<IReadOnlyList<ExperienceCardDto>> SearchAsync(string? city, string? category, DateOnly? date, int? guests, CancellationToken ct)
    {
        var from = clock.GetUtcNow() + BookingCutoff;
        var q = db.Experiences.AsNoTracking().Where(e => e.Status == ExperienceStatus.Published);
        if (!string.IsNullOrWhiteSpace(city)) q = q.Where(e => e.City.StartsWith(city.Trim()));
        if (Enum.TryParse<ExperienceCategory>(category, true, out var c)) q = q.Where(e => e.Category == c);

        DateTimeOffset? dayStart = date is { } d ? new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null;
        var seats = Math.Max(guests ?? 1, 1);
        var rows = await q.Select(e => new
        {
            e.Id, e.Title, e.Category, e.City, e.CountryCode, e.DurationMinutes, e.PricePerGuest, e.Currency, e.ImageUrl,
            HostName = db.Users.Where(u => u.Id == e.HostId).Select(u => u.DisplayName).First(),
            NextSlot = e.Slots.Where(s => !s.Cancelled && s.StartsAt > from && s.Capacity - s.Booked >= seats &&
                    (dayStart == null || (s.StartsAt >= dayStart && s.StartsAt < dayStart.Value.AddDays(1))))
                .OrderBy(s => s.StartsAt).Select(s => (DateTimeOffset?)s.StartsAt).FirstOrDefault(),
        }).Where(x => x.NextSlot != null).OrderBy(x => x.NextSlot).Take(60).ToListAsync(ct);

        return rows.Select(r => new ExperienceCardDto(r.Id, r.Title, r.Category.ToString(), r.City, r.CountryCode, r.DurationMinutes, r.PricePerGuest,
            r.Currency, r.ImageUrl, r.HostName, r.NextSlot)).ToList();
    }

    public async Task<ExperienceDetailDto?> GetAsync(Guid id, CancellationToken ct)
    {
        var e = await db.Experiences.AsNoTracking().Include(x => x.Slots).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return null;
        var isOwner = currentUser.UserId == e.HostId || currentUser.IsInRole(Roles.Admin);
        if (e.Status != ExperienceStatus.Published && !isOwner) return null;
        return await ToDetailAsync(e, upcomingOnly: !isOwner, ct);
    }

    public async Task<IReadOnlyList<ExperienceDetailDto>> ListMineAsync(CancellationToken ct)
    {
        var hostId = currentUser.RequireUserId();
        var mine = await db.Experiences.AsNoTracking().Include(x => x.Slots).Where(x => x.HostId == hostId).OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
        var result = new List<ExperienceDetailDto>();
        foreach (var e in mine) result.Add(await ToDetailAsync(e, upcomingOnly: false, ct));
        return result;
    }

    public async Task<Result<ExperienceDetailDto>> CreateAsync(CreateExperienceRequest r, CancellationToken ct)
    {
        var hostId = currentUser.RequireUserId();
        if (!Enum.TryParse<ExperienceCategory>(r.Category, true, out var category)) return Error.Validation("experience.category", "Unknown category.");
        var created = Experience.Create(hostId, r.Title, r.Description, category, r.City, r.CountryCode, r.MeetingPoint, r.DurationMinutes,
            r.MaxGuests, r.PricePerGuest, r.Currency, r.ImageUrl, clock.GetUtcNow());
        if (created.IsFailure) return created.Error!;
        db.Experiences.Add(created.Value);
        audit.Record("experience.created", nameof(Experience), created.Value.Id.ToString());
        await db.SaveChangesAsync(ct);
        return await ToDetailAsync(created.Value, upcomingOnly: false, ct);
    }

    public async Task<Result<ExperienceSlotDto>> AddSlotAsync(Guid experienceId, AddExperienceSlotRequest request, CancellationToken ct)
    {
        var e = await OwnedAsync(experienceId, ct);
        if (e is null) return NotFound;
        var slot = e.AddSlot(request.StartsAt.ToUniversalTime(), request.Capacity, clock.GetUtcNow());
        if (slot.IsFailure) return slot.Error!;
        db.ExperienceSlots.Add(slot.Value); // owned through a backing field: add explicitly so EF inserts rather than updates
        await db.SaveChangesAsync(ct);
        return new ExperienceSlotDto(slot.Value.Id, slot.Value.StartsAt, slot.Value.Capacity, slot.Value.Remaining);
    }

    public async Task<Result> PublishAsync(Guid experienceId, CancellationToken ct)
    {
        var e = await OwnedAsync(experienceId, ct);
        if (e is null) return NotFound;
        var published = e.Publish(clock.GetUtcNow());
        if (published.IsFailure) return published;
        audit.Record("experience.published", nameof(Experience), e.Id.ToString());
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<ExperienceBookingDto>> BookAsync(Guid experienceId, BookExperienceRequest request, CancellationToken ct)
    {
        var guestId = currentUser.RequireUserId();
        var now = clock.GetUtcNow();
        var e = await db.Experiences.AsNoTracking().FirstOrDefaultAsync(x => x.Id == experienceId && x.Status == ExperienceStatus.Published, ct);
        if (e is null) return NotFound;
        if (e.HostId == guestId) return Error.Validation("experience.own", "You can't book your own experience.");
        if (request.Guests < 1 || request.Guests > e.MaxGuests) return Error.Validation("experience.guests", $"Book between 1 and {e.MaxGuests} guests.");
        var slot = await db.ExperienceSlots.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.SlotId && s.ExperienceId == experienceId, ct);
        if (slot is null || slot.Cancelled) return Error.NotFound("experience.slot_not_found", "That session isn't available.");
        if (slot.StartsAt <= now + BookingCutoff) return Error.Validation("experience.slot_closed", "Booking for this session has closed.");

        var booking = ExperienceBooking.Create(e, slot, guestId, request.Guests, now);
        var claimed = false;
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // The seat claim is one conditional UPDATE: two guests racing for the last seat can't both succeed.
            var updated = await db.ExperienceSlots
                .Where(s => s.Id == slot.Id && !s.Cancelled && s.Booked + request.Guests <= s.Capacity)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Booked, x => x.Booked + request.Guests), ct);
            claimed = updated == 1;
            if (!claimed) return;
            db.ExperienceBookings.Add(booking);
            audit.Record("experience.booked", nameof(ExperienceBooking), booking.Id.ToString(), new { experienceId, request.Guests });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
        if (!claimed) return Error.Conflict("experience.sold_out", "There aren't enough seats left in this session.");

        await notifications.NotifyAsync(e.HostId, "experience.booked", "New experience booking",
            $"{request.Guests} guest(s) booked {e.Title} on {slot.StartsAt:ddd d MMM, HH:mm} UTC.", "/host/experiences", ct);
        return ToBookingDto(booking, e, now);
    }

    public async Task<IReadOnlyList<ExperienceBookingDto>> MyBookingsAsync(CancellationToken ct)
    {
        var guestId = currentUser.RequireUserId();
        var now = clock.GetUtcNow();
        var rows = await db.ExperienceBookings.AsNoTracking().Where(b => b.GuestId == guestId).OrderByDescending(b => b.StartsAt)
            .Join(db.Experiences, b => b.ExperienceId, e => e.Id, (b, e) => new { b, e }).Take(100).ToListAsync(ct);
        return rows.Select(x => ToBookingDto(x.b, x.e, now)).ToList();
    }

    public async Task<Result<ExperienceBookingDto>> CancelBookingAsync(Guid bookingId, CancellationToken ct)
    {
        var guestId = currentUser.RequireUserId();
        var now = clock.GetUtcNow();
        Result result = Result.Success();
        ExperienceBooking? booking = null;
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            booking = await db.ExperienceBookings.FirstOrDefaultAsync(b => b.Id == bookingId && b.GuestId == guestId, ct);
            if (booking is null) { result = Error.NotFound("experience_booking.not_found", "Booking not found."); return; }
            result = booking.Cancel(now);
            if (result.IsFailure) return;
            await db.ExperienceSlots.Where(s => s.Id == booking.SlotId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Booked, x => x.Booked - booking.Guests), ct);
            audit.Record("experience.booking_cancelled", nameof(ExperienceBooking), booking.Id.ToString());
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
        if (result.IsFailure) return result.Error!;
        var e = await db.Experiences.AsNoTracking().FirstAsync(x => x.Id == booking!.ExperienceId, ct);
        await notifications.NotifyAsync(e.HostId, "experience.cancelled", "Experience booking cancelled",
            $"{booking!.Guests} guest(s) cancelled {e.Title} on {booking.StartsAt:ddd d MMM, HH:mm} UTC.", "/host/experiences", ct);
        return ToBookingDto(booking, e, now);
    }

    private Task<Experience?> OwnedAsync(Guid id, CancellationToken ct)
    {
        var hostId = currentUser.RequireUserId();
        return db.Experiences.Include(x => x.Slots).FirstOrDefaultAsync(x => x.Id == id && x.HostId == hostId, ct);
    }

    private async Task<ExperienceDetailDto> ToDetailAsync(Experience e, bool upcomingOnly, CancellationToken ct)
    {
        var hostName = await db.Users.AsNoTracking().Where(u => u.Id == e.HostId).Select(u => u.DisplayName).FirstAsync(ct);
        var from = clock.GetUtcNow() + BookingCutoff;
        var slots = e.Slots.Where(s => !s.Cancelled && (!upcomingOnly || s.StartsAt > from)).OrderBy(s => s.StartsAt)
            .Select(s => new ExperienceSlotDto(s.Id, s.StartsAt, s.Capacity, s.Remaining)).ToList();
        return new ExperienceDetailDto(e.Id, e.HostId, hostName, e.Title, e.Description, e.Category.ToString(), e.City, e.CountryCode, e.MeetingPoint,
            e.DurationMinutes, e.MaxGuests, e.PricePerGuest, e.Currency, e.ImageUrl, e.Status.ToString(), slots);
    }

    private static ExperienceBookingDto ToBookingDto(ExperienceBooking b, Experience e, DateTimeOffset now) =>
        new(b.Id, e.Id, e.Title, e.City, e.MeetingPoint, b.StartsAt, e.DurationMinutes, b.Guests, b.Total, b.Currency, b.Status.ToString(),
            b.Status == ExperienceBookingStatus.Confirmed && now <= b.StartsAt - ExperienceBooking.FreeCancellation);
}
