using System.Globalization;
using Microsoft.EntityFrameworkCore;
using StaySphere.Application.Abstractions;
using StaySphere.Application.Common;
using StaySphere.Contracts.Booking;
using StaySphere.Domain.Catalog;
using StaySphere.Domain.Common;
using StaySphere.Domain.Identity;
using StaySphere.Domain.Pricing;

namespace StaySphere.Application.Booking;

public interface IPromotionService
{
    /// <summary>Host: the host's own promotions. Admin: platform-wide promotions.</summary>
    Task<IReadOnlyList<PromotionDto>> ListMineAsync(bool platform, CancellationToken ct);
    Task<Result<PromotionDto>> CreateAsync(CreatePromotionRequest request, bool platform, CancellationToken ct);
    Task<Result> DeactivateAsync(Guid id, CancellationToken ct);
    /// <summary>Public: promotions currently on offer for a listing (shown on the listing page).</summary>
    Task<IReadOnlyList<PromotionDto>> ActiveForPropertyAsync(Guid propertyId, CancellationToken ct);
}

/// <summary>Loads the promotions that could apply to a listing. Shared by quoting and holding so both price identically.</summary>
public static class PromotionQueries
{
    public static Task<List<Promotion>> CandidatesAsync(IAppDbContext db, Property property, DateTimeOffset now, CancellationToken ct) =>
        db.Promotions.AsNoTracking()
            .Where(p => p.IsActive && p.ValidFrom <= now && p.ValidTo >= now
                        && (p.HostId == null || p.HostId == property.HostId)
                        && (p.PropertyId == null || p.PropertyId == property.Id))
            .ToListAsync(ct);
}

public sealed class PromotionService(IAppDbContext db, ICurrentUser currentUser, IAuditLogger audit, TimeProvider clock) : IPromotionService
{
    private const int MaxActivePerHost = 20;
    private static readonly Error NotFound = Error.NotFound("promotion.not_found", "Promotion not found.");

    public async Task<IReadOnlyList<PromotionDto>> ListMineAsync(bool platform, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var query = platform ? db.Promotions.Where(p => p.HostId == null) : db.Promotions.Where(p => p.HostId == userId);
        var list = await query.AsNoTracking().OrderByDescending(p => p.IsActive).ThenByDescending(p => p.CreatedAt).Take(100).ToListAsync(ct);
        return await ToDtosAsync(list, ct);
    }

    public async Task<Result<PromotionDto>> CreateAsync(CreatePromotionRequest r, bool platform, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        if (platform && !currentUser.IsInRole(Roles.Admin)) return Error.Forbidden("promotion.forbidden", "Only administrators can create platform promotions.");
        if (!Enum.TryParse<PromotionKind>(r.Kind, ignoreCase: true, out var kind) || !Enum.IsDefined(kind))
            return Error.Validation("promotion.kind", "Kind must be EarlyBird, LastMinute, LongStay or FlashSale.");

        Guid? hostId = platform ? null : userId;
        if (r.PropertyId is { } propertyId)
        {
            // Hosts may only target their own listings (404 for anyone else's, so listing ids aren't probed).
            var owner = await db.Properties.Where(p => p.Id == propertyId && p.DeletedAt == null).Select(p => (Guid?)p.HostId).FirstOrDefaultAsync(ct);
            if (owner is null || (!platform && owner != userId)) return Error.NotFound("property.not_found", "Listing not found.");
        }

        if (!platform && await db.Promotions.CountAsync(p => p.HostId == userId && p.IsActive, ct) >= MaxActivePerHost)
            return Error.Conflict("promotion.limit", $"You can have at most {MaxActivePerHost} active promotions.");

        var now = clock.GetUtcNow();
        var created = Promotion.Create(r.Name, kind, r.PercentOff, hostId, r.PropertyId, r.MinNights, r.DaysBeforeCheckIn,
            r.StayStart, r.StayEnd, r.ValidFrom ?? now, r.ValidTo, userId, now);
        if (created.IsFailure) return created.Error!;

        db.Promotions.Add(created.Value);
        audit.Record("promotion.created", nameof(Promotion), created.Value.Id.ToString(),
            new { kind = kind.ToString(), r.PercentOff, platform, r.PropertyId });
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([created.Value], ct))[0];
    }

    public async Task<Result> DeactivateAsync(Guid id, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var promotion = await db.Promotions.FirstOrDefaultAsync(p => p.Id == id, ct);
        var allowed = promotion is not null &&
                      (promotion.HostId == userId || (promotion.HostId is null && currentUser.IsInRole(Roles.Admin)));
        if (!allowed) return NotFound;
        promotion!.Deactivate(clock.GetUtcNow());
        audit.Record("promotion.deactivated", nameof(Promotion), id.ToString());
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<IReadOnlyList<PromotionDto>> ActiveForPropertyAsync(Guid propertyId, CancellationToken ct)
    {
        var property = await db.Properties.AsNoTracking().FirstOrDefaultAsync(p => p.Id == propertyId && p.DeletedAt == null, ct);
        if (property is null || !property.IsBookable) return [];
        var list = await PromotionQueries.CandidatesAsync(db, property, clock.GetUtcNow(), ct);
        return await ToDtosAsync(list.OrderByDescending(p => p.PercentOff).Take(3).ToList(), ct);
    }

    private async Task<List<PromotionDto>> ToDtosAsync(List<Promotion> list, CancellationToken ct)
    {
        var ids = list.Where(p => p.PropertyId != null).Select(p => p.PropertyId!.Value).Distinct().ToList();
        var titles = await db.Properties.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Title, ct);
        return list.Select(p => new PromotionDto(p.Id, p.Name, p.Kind.ToString(), p.PercentOff, p.HostId, p.PropertyId,
            p.PropertyId is { } pid && titles.TryGetValue(pid, out var t) ? t : null, p.MinNights, p.DaysBeforeCheckIn, p.StayStart, p.StayEnd,
            p.ValidFrom, p.ValidTo, p.IsActive, Describe(p))).ToList();
    }

    public static string Describe(Promotion p) => p.Kind switch
    {
        PromotionKind.EarlyBird => string.Create(CultureInfo.InvariantCulture, $"{p.PercentOff:0.#}% off when you book {p.DaysBeforeCheckIn}+ days ahead"),
        PromotionKind.LastMinute => string.Create(CultureInfo.InvariantCulture, $"{p.PercentOff:0.#}% off stays starting within {p.DaysBeforeCheckIn} days"),
        PromotionKind.LongStay => string.Create(CultureInfo.InvariantCulture, $"{p.PercentOff:0.#}% off stays of {p.MinNights}+ nights"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{p.PercentOff:0.#}% off stays starting {p.StayStart:d MMM} – {p.StayEnd:d MMM yyyy}"),
    };
}
