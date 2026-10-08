using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StaySphere.Application.Abstractions;
using StaySphere.Application.Catalog;
using StaySphere.Application.Common;
using StaySphere.Contracts;
using StaySphere.Contracts.Booking;
using StaySphere.Domain.Booking;
using StaySphere.Domain.Catalog;
using StaySphere.Domain.Common;
using StaySphere.Domain.Pricing;
using StaySphere.Domain.ValueObjects;

namespace StaySphere.Application.Booking;

public interface IAvailabilityService
{
    Task<AvailabilityDto?> GetAsync(Guid propertyId, DateOnly from, DateOnly to, CancellationToken ct);
    Task<Result> UpdateBlockedDatesAsync(Guid propertyId, BlockDatesRequest request, CancellationToken ct);
    Task<Result<IReadOnlyList<SeasonalPriceDto>>> GetSeasonalPricesAsync(Guid propertyId, CancellationToken ct);
    Task<Result<SeasonalPriceDto>> AddSeasonalPriceAsync(Guid propertyId, SeasonalPriceRequest request, CancellationToken ct);
    Task<Result> RemoveSeasonalPriceAsync(Guid propertyId, Guid seasonalPriceId, CancellationToken ct);
}

public interface IPricingService
{
    Task<Result<QuoteDto>> QuoteAsync(Guid propertyId, QuoteRequest request, CancellationToken ct);
}

public interface IReservationService
{
    Task<Result<ReservationDto>> CreateHoldAsync(CreateReservationRequest request, CancellationToken ct);
    Task<Result<ReservationDto>> GetAsync(Guid id, CancellationToken ct);
    Task<PagedResult<ReservationDto>> ListMineAsync(string? scope, int page, int pageSize, CancellationToken ct);
    Task<PagedResult<ReservationDto>> ListForHostAsync(string? scope, int page, int pageSize, CancellationToken ct);
    Task<Result<CancellationPreviewDto>> PreviewCancellationAsync(Guid id, CancellationToken ct);
    Task<Result<ReservationDto>> CancelAsync(Guid id, string? reason, CancellationToken ct);
}

public sealed class AvailabilityService(IAppDbContext db, ICurrentUser currentUser, ICacheService cache, TimeProvider clock) : IAvailabilityService
{
    public async Task<AvailabilityDto?> GetAsync(Guid propertyId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (to <= from || to.DayNumber - from.DayNumber > 400) to = from.AddDays(365);
        var property = await db.Properties.AsNoTracking().Where(p => p.Id == propertyId && p.DeletedAt == null)
            .Select(p => new { p.Id, p.MinNights }).FirstOrDefaultAsync(ct);
        if (property is null) return null;

        var booked = await db.ReservationNights.AsNoTracking()
            .Where(n => n.PropertyId == propertyId && n.IsActive && n.Night >= from && n.Night < to)
            .Select(n => n.Night).ToListAsync(ct);
        var blocked = await db.Properties.AsNoTracking().Where(p => p.Id == propertyId)
            .SelectMany(p => p.BlockedDates).Where(b => b.Date >= from && b.Date < to).Select(b => b.Date).ToListAsync(ct);

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var unavailable = booked.Concat(blocked).Where(d => d >= today).Distinct().Order().ToList();
        return new AvailabilityDto(propertyId, from, to, unavailable, property.MinNights);
    }

    public async Task<Result> UpdateBlockedDatesAsync(Guid propertyId, BlockDatesRequest request, CancellationToken ct)
    {
        var property = await db.Properties.Include(p => p.BlockedDates).FirstOrDefaultAsync(p => p.Id == propertyId && p.DeletedAt == null, ct);
        if (property is null) return Error.NotFound("property.not_found", "Listing not found.");
        if (!AccessRules.CanManageProperty(currentUser, property)) return Error.Forbidden("property.forbidden", "You can only manage your own listings.");

        var booked = await db.ReservationNights.Where(n => n.PropertyId == propertyId && n.IsActive && request.Block.Contains(n.Night))
            .Select(n => n.Night).ToListAsync(ct);
        if (booked.Count > 0)
            return Error.Conflict("availability.booked", $"These dates already have reservations: {string.Join(", ", booked.Order())}.");

        property.BlockDates(request.Block);
        property.UnblockDates(request.Unblock);
        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync(CacheKeys.Property(propertyId), ct);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<SeasonalPriceDto>>> GetSeasonalPricesAsync(Guid propertyId, CancellationToken ct)
    {
        var property = await db.Properties.AsNoTracking().Include(p => p.SeasonalPrices).FirstOrDefaultAsync(p => p.Id == propertyId, ct);
        if (property is null) return Error.NotFound("property.not_found", "Listing not found.");
        if (!AccessRules.CanManageProperty(currentUser, property)) return Error.Forbidden("property.forbidden", "You can only manage your own listings.");
        return property.SeasonalPrices.OrderBy(s => s.Start).Select(s => new SeasonalPriceDto(s.Id, s.Start, s.End, s.NightlyPrice)).ToList();
    }

    public async Task<Result<SeasonalPriceDto>> AddSeasonalPriceAsync(Guid propertyId, SeasonalPriceRequest request, CancellationToken ct)
    {
        var property = await db.Properties.Include(p => p.SeasonalPrices).FirstOrDefaultAsync(p => p.Id == propertyId && p.DeletedAt == null, ct);
        if (property is null) return Error.NotFound("property.not_found", "Listing not found.");
        if (!AccessRules.CanManageProperty(currentUser, property)) return Error.Forbidden("property.forbidden", "You can only manage your own listings.");
        var result = property.AddSeasonalPrice(request.Start, request.End, request.NightlyPrice);
        if (result.IsFailure) return result.Error!;
        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync(CacheKeys.Property(propertyId), ct);
        var s = property.SeasonalPrices.First(x => x.Start == request.Start);
        return new SeasonalPriceDto(s.Id, s.Start, s.End, s.NightlyPrice);
    }

    public async Task<Result> RemoveSeasonalPriceAsync(Guid propertyId, Guid seasonalPriceId, CancellationToken ct)
    {
        var property = await db.Properties.Include(p => p.SeasonalPrices).FirstOrDefaultAsync(p => p.Id == propertyId, ct);
        if (property is null) return Error.NotFound("property.not_found", "Listing not found.");
        if (!AccessRules.CanManageProperty(currentUser, property)) return Error.Forbidden("property.forbidden", "You can only manage your own listings.");
        property.RemoveSeasonalPrice(seasonalPriceId);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed class PricingService(IAppDbContext db, IQuoteTokenService quoteTokens, ITaxRateProvider taxes, TimeProvider clock) : IPricingService
{
    public static readonly TimeSpan QuoteLifetime = TimeSpan.FromMinutes(15);

    public async Task<Result<QuoteDto>> QuoteAsync(Guid propertyId, QuoteRequest request, CancellationToken ct)
    {
        var stay = DateRange.Create(request.CheckIn, request.CheckOut);
        if (stay.IsFailure) return stay.Error!;
        var property = await db.Properties.AsNoTracking().Include(p => p.SeasonalPrices).Include(p => p.BlockedDates)
            .FirstOrDefaultAsync(p => p.Id == propertyId && p.DeletedAt == null, ct);
        if (property is null || !property.IsBookable) return Error.NotFound("property.not_found", "Listing not found.");
        if (request.Guests < 1 || request.Guests > property.MaxGuests)
            return Error.Validation("quote.guests", $"This place allows 1 to {property.MaxGuests} guests.");

        Coupon? coupon = null;
        if (!string.IsNullOrWhiteSpace(request.CouponCode))
        {
            var code = request.CouponCode.Trim().ToUpperInvariant();
            coupon = await db.Coupons.AsNoTracking().FirstOrDefaultAsync(c => c.Code == code, ct);
            if (coupon is null) return Error.Validation("coupon.not_found", "That coupon code is not valid.");
        }

        var now = clock.GetUtcNow();
        var price = PriceCalculator.Calculate(property, stay.Value, coupon, PricingSettings.Default,
            taxes.GetTaxPercent(property.Address?.CountryCode ?? ""), now);
        if (price.IsFailure) return price.Error!;

        var s = stay.Value;
        var available = !property.BlockedDates.Any(b => s.Contains(b.Date)) &&
                        !await db.ReservationNights.AnyAsync(n => n.PropertyId == propertyId && n.IsActive && n.Night >= s.Start && n.Night < s.End, ct);

        var expires = now.Add(QuoteLifetime);
        var b = price.Value;
        var token = quoteTokens.Create(new QuoteTokenPayload(propertyId, s.Start, s.End, request.Guests, b.Total, b.Currency,
            coupon?.Code, expires));
        return new QuoteDto(propertyId, s.Start, s.End, request.Guests, b.Nights, b.Currency,
            b.NightlyRates.Select(r => new NightlyRateDto(r.Night, r.Amount, r.Reason)).ToList(), b.AverageNightly, b.Accommodation,
            b.Discount, b.CleaningFee, b.ServiceFee, b.Taxes, b.Total, token, expires, available);
    }
}

public sealed class ReservationService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IQuoteTokenService quoteTokens,
    ITaxRateProvider taxes,
    IAuditLogger audit,
    TimeProvider clock,
    ILogger<ReservationService> logger) : IReservationService
{
    private static readonly Error NotFound = Error.NotFound("reservation.not_found", "Reservation not found.");

    public async Task<Result<ReservationDto>> CreateHoldAsync(CreateReservationRequest request, CancellationToken ct)
    {
        var guestId = currentUser.RequireUserId();
        var now = clock.GetUtcNow();

        var stay = DateRange.Create(request.CheckIn, request.CheckOut);
        if (stay.IsFailure) return stay.Error!;

        // The quote token binds the price the guest saw to the exact stay; it is re-verified, never trusted blindly.
        var quote = quoteTokens.Read(request.QuoteToken);
        if (quote is null || quote.ExpiresAt < now || quote.PropertyId != request.PropertyId || quote.CheckIn != request.CheckIn ||
            quote.CheckOut != request.CheckOut || quote.Guests != request.Guests ||
            !string.Equals(quote.CouponCode, request.CouponCode?.Trim().ToUpperInvariant(), StringComparison.Ordinal))
            return Error.Conflict("reservation.quote_invalid", "Your price quote has expired or does not match. Please review the price again.");

        var property = await db.Properties.Include(p => p.SeasonalPrices).Include(p => p.BlockedDates)
            .FirstOrDefaultAsync(p => p.Id == request.PropertyId && p.DeletedAt == null, ct);
        if (property is null) return Error.NotFound("property.not_found", "Listing not found.");

        Coupon? coupon = null;
        if (quote.CouponCode is not null)
            coupon = await db.Coupons.FirstOrDefaultAsync(c => c.Code == quote.CouponCode, ct);

        var price = PriceCalculator.Calculate(property, stay.Value, coupon, PricingSettings.Default,
            taxes.GetTaxPercent(property.Address?.CountryCode ?? ""), now);
        if (price.IsFailure) return price.Error!;
        if (price.Value.Total != quote.Total)
            return Error.Conflict("reservation.price_changed", "The price for these dates has changed. Please review the new price.");

        var s = stay.Value;
        if (await db.ReservationNights.AnyAsync(n => n.PropertyId == property.Id && n.IsActive && n.Night >= s.Start && n.Night < s.End, ct))
            return DatesTaken;

        var hold = Reservation.Hold(property, guestId, s, request.Guests, price.Value, coupon?.Code, now);
        if (hold.IsFailure) return hold.Error!;

        db.Reservations.Add(hold.Value);
        coupon?.Redeem();
        audit.Record("reservation.held", nameof(Reservation), hold.Value.Id.ToString(),
            new { property.Id, request.CheckIn, request.CheckOut, hold.Value.TotalAmount });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (db.IsUniqueViolation(ex))
        {
            // Lost the race: another hold for an overlapping night committed first (filtered unique index).
            db.ResetTracking();
            logger.LogInformation("Double-booking prevented for property {PropertyId} {CheckIn}–{CheckOut}", property.Id, s.Start, s.End);
            return DatesTaken;
        }

        logger.LogInformation("Reservation hold created. ReservationId={ReservationId}, PropertyId={PropertyId}", hold.Value.Id, property.Id);
        return (await GetAsync(hold.Value.Id, ct)).Value;
    }

    private static Error DatesTaken => Error.Conflict("reservation.dates_unavailable", "Sorry, those dates were just booked. Please choose different dates.");

    public async Task<Result<ReservationDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var reservation = await db.Reservations.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (reservation is null || !AccessRules.CanViewReservation(currentUser, reservation)) return NotFound;
        return (await ProjectAsync(db.Reservations.Where(r => r.Id == id), ct)).Single();
    }

    public async Task<PagedResult<ReservationDto>> ListMineAsync(string? scope, int page, int pageSize, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        return await PageAsync(ApplyScope(db.Reservations.Where(r => r.GuestId == userId), scope), page, pageSize, ct);
    }

    public async Task<PagedResult<ReservationDto>> ListForHostAsync(string? scope, int page, int pageSize, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        return await PageAsync(ApplyScope(db.Reservations.Where(r => r.HostId == userId && r.Status != ReservationStatus.Expired && r.Status != ReservationStatus.Failed), scope),
            page, pageSize, ct);
    }

    public async Task<Result<CancellationPreviewDto>> PreviewCancellationAsync(Guid id, CancellationToken ct)
    {
        var reservation = await db.Reservations.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (reservation is null || !AccessRules.CanViewReservation(currentUser, reservation)) return NotFound;
        var quote = RefundFor(reservation);
        return new CancellationPreviewDto(id, quote.RefundAmount, quote.NonRefundable, reservation.Currency, quote.Explanation);
    }

    public async Task<Result<ReservationDto>> CancelAsync(Guid id, string? reason, CancellationToken ct)
    {
        var reservation = await db.Reservations.Include(r => r.NightRows).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (reservation is null || !AccessRules.CanViewReservation(currentUser, reservation)) return NotFound;
        var actor = currentUser.RequireUserId();
        if (actor != reservation.GuestId && actor != reservation.HostId && !currentUser.IsInRole(Domain.Identity.Roles.Admin))
            return Error.Forbidden("reservation.forbidden", "You cannot cancel this reservation.");

        var refund = RefundFor(reservation);
        var result = reservation.Cancel(actor, refund.RefundAmount, reason, clock.GetUtcNow());
        if (result.IsFailure) return result.Error!;
        audit.Record("reservation.cancelled", nameof(Reservation), id.ToString(), new { refund.RefundAmount, reason });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict("reservation.concurrency", "This reservation was just updated. Please refresh.");
        }

        return (await GetAsync(id, ct)).Value;
    }

    /// <summary>Hosts (and admins) cancelling always refund the guest in full; guests follow the listing's policy.</summary>
    private RefundQuote RefundFor(Reservation reservation) =>
        currentUser.UserId == reservation.HostId || currentUser.IsInRole(Domain.Identity.Roles.Admin)
            ? reservation.Status == ReservationStatus.Confirmed
                ? new RefundQuote(reservation.TotalAmount, 0, "Cancelled by the host: full refund.")
                : new RefundQuote(0, 0, "Nothing has been charged for this reservation.")
            : CancellationPolicyCalculator.Calculate(reservation, clock.GetUtcNow());

    private IQueryable<Reservation> ApplyScope(IQueryable<Reservation> query, string? scope)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        return scope?.ToLowerInvariant() switch
        {
            "upcoming" => query.Where(r => (r.Status == ReservationStatus.Confirmed || r.Status == ReservationStatus.Held || r.Status == ReservationStatus.PaymentPending) && r.CheckOut >= today)
                .OrderBy(r => r.CheckIn),
            "past" => query.Where(r => r.Status == ReservationStatus.Completed || (r.Status == ReservationStatus.Confirmed && r.CheckOut < today))
                .OrderByDescending(r => r.CheckIn),
            "cancelled" => query.Where(r => r.Status == ReservationStatus.Cancelled || r.Status == ReservationStatus.Refunded || r.Status == ReservationStatus.RefundPending)
                .OrderByDescending(r => r.UpdatedAt),
            _ => query.OrderByDescending(r => r.CreatedAt),
        };
    }

    private async Task<PagedResult<ReservationDto>> PageAsync(IQueryable<Reservation> query, int page, int pageSize, CancellationToken ct)
    {
        (page, pageSize) = Paging.Normalize(page, pageSize);
        var total = await query.CountAsync(ct);
        var items = await ProjectAsync(query.Skip((page - 1) * pageSize).Take(pageSize), ct);
        return new PagedResult<ReservationDto>(items, page, pageSize, total);
    }

    private async Task<List<ReservationDto>> ProjectAsync(IQueryable<Reservation> query, CancellationToken ct)
    {
        var rows = await query.AsNoTracking()
            .Select(r => new
            {
                R = r,
                Property = db.Properties.Where(p => p.Id == r.PropertyId).Select(p => new
                {
                    p.Title, City = p.Address != null ? p.Address.City : "", Country = p.Address != null ? p.Address.Country : "",
                    Image = p.Images.OrderBy(i => i.SortOrder).Select(i => i.ThumbnailUrl).FirstOrDefault(),
                }).First(),
                GuestName = db.Users.Where(u => u.Id == r.GuestId).Select(u => u.DisplayName).First(),
                HostName = db.Users.Where(u => u.Id == r.HostId).Select(u => u.DisplayName).First(),
                HasReview = db.Reviews.Any(v => v.ReservationId == r.Id),
            })
            .ToListAsync(ct);

        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        return rows.Select(x =>
        {
            var r = x.R;
            var canCancel = r.Status is ReservationStatus.Held or ReservationStatus.PaymentPending ||
                            (r.Status == ReservationStatus.Confirmed && r.CheckIn > today);
            var canReview = r.Status == ReservationStatus.Completed && !x.HasReview && r.GuestId == currentUser.UserId &&
                            now - new DateTimeOffset(r.CheckOut.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) <= Domain.Reviews.Review.ReviewWindow;
            return new ReservationDto(r.Id, r.PropertyId, x.Property.Title, x.Property.Image, x.Property.City, x.Property.Country,
                r.GuestId, x.GuestName, r.HostId, x.HostName, r.CheckIn, r.CheckOut, r.Guests, r.Nights,
                r.BaseAmount, r.CleaningFee, r.ServiceFee, r.Taxes, r.Discount, r.TotalAmount, r.Currency, r.Status.ToString(),
                r.CancellationPolicy.ToString(), r.HoldExpiresAt, r.CreatedAt, r.RefundAmount, canCancel, canReview, x.HasReview);
        }).ToList();
    }
}
