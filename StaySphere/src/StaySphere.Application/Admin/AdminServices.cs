using Microsoft.EntityFrameworkCore;
using StaySphere.Application.Abstractions;
using StaySphere.Application.Common;
using StaySphere.Contracts;
using StaySphere.Contracts.Admin;
using StaySphere.Contracts.Booking;
using StaySphere.Contracts.Payments;
using StaySphere.Domain.Booking;
using StaySphere.Domain.Catalog;
using StaySphere.Domain.Common;
using StaySphere.Domain.Identity;
using StaySphere.Domain.Payments;
using StaySphere.Domain.Pricing;
using StaySphere.Domain.Trust;

namespace StaySphere.Application.Admin;

public interface IAdminService
{
    Task<AdminDashboardDto> DashboardAsync(CancellationToken ct);
    Task<PagedResult<AdminUserDto>> UsersAsync(string? search, int page, int pageSize, CancellationToken ct);
    Task<Result> SetUserSuspendedAsync(Guid userId, bool suspended, CancellationToken ct);
    Task<Result> SetUserRoleAsync(Guid userId, string role, CancellationToken ct);
    Task<PagedResult<AdminPropertyDto>> PropertiesAsync(string? search, string? status, int page, int pageSize, CancellationToken ct);
    Task<Result> SuspendPropertyAsync(Guid propertyId, CancellationToken ct);
    Task<PagedResult<ReservationDto>> ReservationsAsync(string? status, int page, int pageSize, CancellationToken ct);
    Task<PagedResult<PaymentDto>> PaymentsAsync(string? status, int page, int pageSize, CancellationToken ct);
    Task<PagedResult<ReportDto>> ReportsAsync(string? status, int page, int pageSize, CancellationToken ct);
    Task<Result> ResolveReportAsync(Guid reportId, bool actioned, CancellationToken ct);
    Task<PagedResult<FraudAlertDto>> FraudAlertsAsync(int page, int pageSize, CancellationToken ct);
    Task<Result> AcknowledgeFraudAlertAsync(Guid id, CancellationToken ct);
    Task<PagedResult<AuditLogDto>> AuditAsync(string? action, Guid? actorId, int page, int pageSize, CancellationToken ct);
    Task<IReadOnlyList<CouponDto>> CouponsAsync(CancellationToken ct);
    Task<Result<CouponDto>> CreateCouponAsync(CreateCouponRequest request, CancellationToken ct);
    Task<Result> DeactivateCouponAsync(Guid id, CancellationToken ct);
}

public sealed class AdminService(IAppDbContext db, IAuditLogger audit, ICacheService cache, TimeProvider clock) : IAdminService
{
    public async Task<AdminDashboardDto> DashboardAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var since = now.AddDays(-30);
        var confirmedStatuses = new[] { ReservationStatus.Confirmed, ReservationStatus.Completed, ReservationStatus.RefundPending, ReservationStatus.Refunded, ReservationStatus.Cancelled };

        var users = await db.Users.CountAsync(ct);
        var hosts = await db.Users.CountAsync(u => u.Roles.Any(r => r.Role == Roles.Host), ct);
        var properties = await db.Properties.CountAsync(p => p.DeletedAt == null, ct);
        var published = await db.Properties.CountAsync(p => p.Status == PropertyStatus.Published && p.DeletedAt == null, ct);
        var bookings = await db.Reservations.CountAsync(r => confirmedStatuses.Contains(r.Status), ct);
        var cancelled = await db.Reservations.CountAsync(r => r.Status == ReservationStatus.Cancelled || r.Status == ReservationStatus.Refunded || r.Status == ReservationStatus.RefundPending, ct);
        var revenue = await db.LedgerEntries.Where(l => l.Account == LedgerAccount.PlatformFee).SumAsync(l => (decimal?)l.Amount, ct) ?? 0;
        var refunds = -(await db.LedgerEntries.Where(l => l.Account == LedgerAccount.Refund).SumAsync(l => (decimal?)l.Amount, ct) ?? 0);

        var dailyBookings = await db.Reservations.Where(r => r.ConfirmedAt >= since)
            .GroupBy(r => r.ConfirmedAt!.Value.Date).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var dailyRevenue = await db.LedgerEntries.Where(l => l.Account == LedgerAccount.PlatformFee && l.OccurredAt >= since)
            .GroupBy(l => l.OccurredAt.Date).Select(g => new { g.Key, Sum = g.Sum(x => x.Amount) }).ToListAsync(ct);
        var newUsers = await db.Users.Where(u => u.CreatedAt >= since)
            .GroupBy(u => u.CreatedAt.Date).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var newProps = await db.Properties.Where(p => p.CreatedAt >= since)
            .GroupBy(p => p.CreatedAt.Date).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);

        IReadOnlyList<TimePointDto> Series(Func<DateTime, decimal> valueFor) =>
            Enumerable.Range(0, 30).Select(i => DateOnly.FromDateTime(since.UtcDateTime).AddDays(i + 1))
                .Select(d => new TimePointDto(d, valueFor(d.ToDateTime(TimeOnly.MinValue)))).ToList();

        return new AdminDashboardDto(users, hosts, properties, published, bookings, revenue, refunds,
            await db.Reports.CountAsync(r => r.Status == ReportStatus.Open, ct),
            await db.SupportTickets.CountAsync(t => t.Status != TicketStatus.Closed && t.Status != TicketStatus.Resolved, ct),
            await db.FraudChecks.CountAsync(f => !f.Acknowledged, ct),
            bookings + cancelled == 0 ? 0 : Math.Round(cancelled / (double)(bookings), 3),
            Series(d => dailyBookings.FirstOrDefault(x => x.Key == d)?.Count ?? 0),
            Series(d => dailyRevenue.FirstOrDefault(x => x.Key == d)?.Sum ?? 0),
            Series(d => newUsers.FirstOrDefault(x => x.Key == d)?.Count ?? 0),
            Series(d => newProps.FirstOrDefault(x => x.Key == d)?.Count ?? 0),
            "USD");
    }

    public async Task<PagedResult<AdminUserDto>> UsersAsync(string? search, int page, int pageSize, CancellationToken ct)
    {
        (page, pageSize) = Paging.Normalize(page, pageSize, 100);
        var q = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(u => u.Email.Contains(search) || u.DisplayName.Contains(search));
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(u => u.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(u => new AdminUserDto(u.Id, u.Email, u.DisplayName, u.Roles.Select(r => r.Role).ToList(), u.Status.ToString(),
                u.EmailConfirmed, u.CreatedAt, u.LastLoginAt)).ToListAsync(ct);
        return new PagedResult<AdminUserDto>(items, page, pageSize, total);
    }

    public async Task<Result> SetUserSuspendedAsync(Guid userId, bool suspended, CancellationToken ct)
    {
        var user = await db.Users.Include(u => u.Roles).FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return Error.NotFound("user.not_found", "User not found.");
        if (user.IsInRole(Roles.Admin) && suspended) return Error.Conflict("user.admin", "Administrators cannot be suspended here.");
        var now = clock.GetUtcNow();
        if (suspended)
        {
            user.Suspend();
            await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ForEachAsync(t => t.Revoke(now), ct);
        }
        else
        {
            user.Reinstate();
        }

        audit.Record(suspended ? "admin.user_suspended" : "admin.user_reinstated", nameof(User), userId.ToString());
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> SetUserRoleAsync(Guid userId, string role, CancellationToken ct)
    {
        if (!Roles.All.Contains(role)) return Error.Validation("user.role", "Unknown role.");
        var user = await db.Users.Include(u => u.Roles).FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return Error.NotFound("user.not_found", "User not found.");
        user.AddRole(role);
        audit.Record("admin.role_granted", nameof(User), userId.ToString(), new { role });
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<PagedResult<AdminPropertyDto>> PropertiesAsync(string? search, string? status, int page, int pageSize, CancellationToken ct)
    {
        (page, pageSize) = Paging.Normalize(page, pageSize, 100);
        var q = db.Properties.AsNoTracking().Where(p => p.DeletedAt == null);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(p => p.Title.Contains(search) || (p.Address != null && p.Address.City.Contains(search)));
        if (Enum.TryParse<PropertyStatus>(status, true, out var s)) q = q.Where(p => p.Status == s);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(p => p.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new AdminPropertyDto(p.Id, p.Title, p.HostId, db.Users.Where(u => u.Id == p.HostId).Select(u => u.DisplayName).First(),
                p.Status.ToString(), p.Address != null ? p.Address.City : "", p.BasePrice, p.Currency, p.ReviewCount, p.RatingAverage, p.CreatedAt))
            .ToListAsync(ct);
        return new PagedResult<AdminPropertyDto>(items, page, pageSize, total);
    }

    public async Task<Result> SuspendPropertyAsync(Guid propertyId, CancellationToken ct)
    {
        var property = await db.Properties.FirstOrDefaultAsync(p => p.Id == propertyId, ct);
        if (property is null) return Error.NotFound("property.not_found", "Listing not found.");
        property.Suspend();
        audit.Record("admin.property_suspended", nameof(Property), propertyId.ToString());
        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync(CacheKeys.Property(propertyId), ct);
        return Result.Success();
    }

    public async Task<PagedResult<ReservationDto>> ReservationsAsync(string? status, int page, int pageSize, CancellationToken ct)
    {
        (page, pageSize) = Paging.Normalize(page, pageSize, 100);
        var q = db.Reservations.AsNoTracking();
        if (Enum.TryParse<ReservationStatus>(status, true, out var s)) q = q.Where(r => r.Status == s);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(r => r.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new ReservationDto(r.Id, r.PropertyId,
                db.Properties.Where(p => p.Id == r.PropertyId).Select(p => p.Title).First(), null,
                db.Properties.Where(p => p.Id == r.PropertyId).Select(p => p.Address!.City).First(), "",
                r.GuestId, db.Users.Where(u => u.Id == r.GuestId).Select(u => u.DisplayName).First(),
                r.HostId, db.Users.Where(u => u.Id == r.HostId).Select(u => u.DisplayName).First(),
                r.CheckIn, r.CheckOut, r.Guests, r.Nights, r.BaseAmount, r.CleaningFee, r.ServiceFee, r.Taxes, r.Discount, r.TotalAmount,
                r.Currency, r.Status.ToString(), r.CancellationPolicy.ToString(), r.HoldExpiresAt, r.CreatedAt, r.RefundAmount, false, false, false))
            .ToListAsync(ct);
        return new PagedResult<ReservationDto>(items, page, pageSize, total);
    }

    public async Task<PagedResult<PaymentDto>> PaymentsAsync(string? status, int page, int pageSize, CancellationToken ct)
    {
        (page, pageSize) = Paging.Normalize(page, pageSize, 100);
        var q = db.Payments.AsNoTracking();
        if (Enum.TryParse<PaymentStatus>(status, true, out var s)) q = q.Where(p => p.Status == s);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(p => p.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new PaymentDto(p.Id, p.ReservationId, p.Amount, p.Currency, p.Status.ToString(), p.Provider, p.CardLast4, p.FailureReason,
                p.Refunds.Where(r => r.Status == RefundStatus.Succeeded).Sum(r => r.Amount), p.CreatedAt,
                db.Reservations.Where(r => r.Id == p.ReservationId).Select(r => r.Status.ToString()).First()))
            .ToListAsync(ct);
        return new PagedResult<PaymentDto>(items, page, pageSize, total);
    }

    public async Task<PagedResult<ReportDto>> ReportsAsync(string? status, int page, int pageSize, CancellationToken ct)
    {
        (page, pageSize) = Paging.Normalize(page, pageSize, 100);
        var q = db.Reports.AsNoTracking();
        if (Enum.TryParse<ReportStatus>(status, true, out var s)) q = q.Where(r => r.Status == s);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(r => r.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new ReportDto(r.Id, r.ReporterId, db.Users.Where(u => u.Id == r.ReporterId).Select(u => u.DisplayName).First(),
                r.TargetType, r.TargetId, r.Reason, r.Status.ToString(), r.CreatedAt)).ToListAsync(ct);
        return new PagedResult<ReportDto>(items, page, pageSize, total);
    }

    public async Task<Result> ResolveReportAsync(Guid reportId, bool actioned, CancellationToken ct)
    {
        var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == reportId, ct);
        if (report is null) return Error.NotFound("report.not_found", "Report not found.");
        report.Resolve(actioned);
        audit.Record("admin.report_resolved", nameof(Report), reportId.ToString(), new { actioned });
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<PagedResult<FraudAlertDto>> FraudAlertsAsync(int page, int pageSize, CancellationToken ct)
    {
        (page, pageSize) = Paging.Normalize(page, pageSize, 100);
        var q = db.FraudChecks.AsNoTracking();
        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(f => f.Acknowledged).ThenByDescending(f => f.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(f => new FraudAlertDto(f.Id, f.SubjectType, f.SubjectId, f.UserId, f.RuleCode, f.Score, f.Decision.ToString(), f.Details,
                f.Acknowledged, f.CreatedAt)).ToListAsync(ct);
        return new PagedResult<FraudAlertDto>(items, page, pageSize, total);
    }

    public async Task<Result> AcknowledgeFraudAlertAsync(Guid id, CancellationToken ct)
    {
        var alert = await db.FraudChecks.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (alert is null) return Error.NotFound("fraud.not_found", "Alert not found.");
        alert.Acknowledge();
        audit.Record("admin.fraud_acknowledged", nameof(FraudCheck), id.ToString());
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<PagedResult<AuditLogDto>> AuditAsync(string? action, Guid? actorId, int page, int pageSize, CancellationToken ct)
    {
        (page, pageSize) = Paging.Normalize(page, pageSize, 100);
        var q = db.AuditLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(action)) q = q.Where(a => a.Action.StartsWith(action));
        if (actorId is not null) q = q.Where(a => a.ActorId == actorId);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(a => a.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new AuditLogDto(a.Id, a.ActorId, db.Users.Where(u => u.Id == a.ActorId).Select(u => u.DisplayName).FirstOrDefault(),
                a.Action, a.EntityType, a.EntityId, a.Details, a.IpAddress, a.CorrelationId, a.At)).ToListAsync(ct);
        return new PagedResult<AuditLogDto>(items, page, pageSize, total);
    }

    public async Task<IReadOnlyList<CouponDto>> CouponsAsync(CancellationToken ct) =>
        await db.Coupons.AsNoTracking().OrderByDescending(c => c.ValidTo)
            .Select(c => new CouponDto(c.Id, c.Code, c.PercentOff, c.AmountOff, c.Currency, c.MinimumSpend, c.ValidFrom, c.ValidTo,
                c.MaxRedemptions, c.Redemptions, c.IsActive)).ToListAsync(ct);

    public async Task<Result<CouponDto>> CreateCouponAsync(CreateCouponRequest r, CancellationToken ct)
    {
        var code = r.Code.Trim().ToUpperInvariant();
        if (await db.Coupons.AnyAsync(c => c.Code == code, ct)) return Error.Conflict("coupon.exists", "A coupon with that code already exists.");
        if (r.ValidTo <= r.ValidFrom) return Error.Validation("coupon.dates", "End must be after start.");
        var coupon = Coupon.Create(code, r.PercentOff, r.AmountOff, r.Currency, r.MinimumSpend, r.ValidFrom, r.ValidTo, r.MaxRedemptions);
        db.Coupons.Add(coupon);
        audit.Record("admin.coupon_created", nameof(Coupon), coupon.Id.ToString(), new { code });
        await db.SaveChangesAsync(ct);
        return new CouponDto(coupon.Id, coupon.Code, coupon.PercentOff, coupon.AmountOff, coupon.Currency, coupon.MinimumSpend, coupon.ValidFrom,
            coupon.ValidTo, coupon.MaxRedemptions, 0, true);
    }

    public async Task<Result> DeactivateCouponAsync(Guid id, CancellationToken ct)
    {
        var coupon = await db.Coupons.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (coupon is null) return Error.NotFound("coupon.not_found", "Coupon not found.");
        coupon.Deactivate();
        audit.Record("admin.coupon_deactivated", nameof(Coupon), id.ToString());
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public interface IHostDashboardService
{
    Task<HostDashboardDto> GetAsync(CancellationToken ct);
}

public sealed class HostDashboardService(IAppDbContext db, ICurrentUser currentUser, TimeProvider clock) : IHostDashboardService
{
    public async Task<HostDashboardDto> GetAsync(CancellationToken ct)
    {
        var hostId = currentUser.RequireUserId();
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var yearAgo = today.AddMonths(-11);
        var monthStart = new DateOnly(yearAgo.Year, yearAgo.Month, 1);
        var bookedStatuses = new[] { ReservationStatus.Confirmed, ReservationStatus.Completed };

        var reservations = await db.Reservations.AsNoTracking()
            .Where(r => r.HostId == hostId && bookedStatuses.Contains(r.Status))
            .Select(r => new { r.PropertyId, r.CheckIn, r.CheckOut, r.Nights, r.BaseAmount, r.Currency }).ToListAsync(ct);
        var earnings = await db.LedgerEntries.AsNoTracking().Where(l => l.HostId == hostId && l.Account == LedgerAccount.HostEarning)
            .Select(l => new { l.ReservationId, l.Amount, l.OccurredAt }).ToListAsync(ct);
        var properties = await db.Properties.AsNoTracking().Where(p => p.HostId == hostId && p.DeletedAt == null)
            .Select(p => new { p.Id, p.Title, p.RatingAverage, p.ReviewCount, p.Status }).ToListAsync(ct);
        var reservationProperty = await db.Reservations.AsNoTracking().Where(r => r.HostId == hostId)
            .Select(r => new { r.Id, r.PropertyId }).ToDictionaryAsync(r => r.Id, r => r.PropertyId, ct);

        var last90 = today.AddDays(-90);
        var publishedCount = Math.Max(1, properties.Count(p => p.Status == PropertyStatus.Published));
        var bookedNights90 = reservations.Sum(r => Overlap(r.CheckIn, r.CheckOut, last90, today));
        double occupancy = Math.Round(bookedNights90 / (90.0 * publishedCount), 3);

        var months = Enumerable.Range(0, 12).Select(i => monthStart.AddMonths(i)).ToList();
        var rated = properties.Where(p => p.ReviewCount > 0).ToList();

        return new HostDashboardDto(
            reservations.Count,
            earnings.Sum(e => e.Amount),
            reservations.Count(r => r.CheckIn >= today),
            occupancy,
            reservations.Count == 0 ? 0 : decimal.Round(reservations.Sum(r => r.BaseAmount) / Math.Max(1, reservations.Sum(r => r.Nights)), 2),
            rated.Count == 0 ? 0 : Math.Round(rated.Sum(p => p.RatingAverage * p.ReviewCount) / rated.Sum(p => p.ReviewCount), 2),
            reservations.FirstOrDefault()?.Currency ?? "USD",
            months.Select(m => new TimePointDto(m, earnings.Where(e => e.OccurredAt.Year == m.Year && e.OccurredAt.Month == m.Month).Sum(e => e.Amount))).ToList(),
            months.Select(m => new TimePointDto(m, reservations.Count(r => r.CheckIn.Year == m.Year && r.CheckIn.Month == m.Month))).ToList(),
            properties.Select(p => new PropertyPerformanceDto(p.Id, p.Title,
                reservations.Count(r => r.PropertyId == p.Id),
                earnings.Where(e => reservationProperty.GetValueOrDefault(e.ReservationId) == p.Id).Sum(e => e.Amount),
                Math.Round(reservations.Where(r => r.PropertyId == p.Id).Sum(r => Overlap(r.CheckIn, r.CheckOut, last90, today)) / 90.0, 3),
                p.RatingAverage)).OrderByDescending(p => p.Revenue).ToList());
    }

    private static int Overlap(DateOnly start, DateOnly end, DateOnly from, DateOnly to) =>
        Math.Max(0, Math.Min(end.DayNumber, to.DayNumber) - Math.Max(start.DayNumber, from.DayNumber));
}

/// <summary>Basic, extensible risk rules. A foundation for a fraud system — not a production fraud engine.</summary>
public interface IFraudService
{
    Task EvaluateUserAsync(Guid userId, string trigger, CancellationToken ct);
}

public sealed class FraudService(IAppDbContext db, TimeProvider clock) : IFraudService
{
    public async Task EvaluateUserAsync(Guid userId, string trigger, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var hourAgo = now.AddHours(-1);

        var failedPayments = await db.Payments.CountAsync(p => p.PayerId == userId && p.Status == PaymentStatus.Failed && p.CreatedAt >= hourAgo, ct);
        if (failedPayments >= 3)
            await RaiseOnceAsync("User", userId, userId, "REPEATED_PAYMENT_FAILURES", 70, RiskDecision.Review,
                $"{failedPayments} failed payments in the last hour.", ct);

        var holds = await db.Reservations.CountAsync(r => r.GuestId == userId && r.CreatedAt >= hourAgo, ct);
        if (holds >= 6)
            await RaiseOnceAsync("User", userId, userId, "BOOKING_VELOCITY", 60, RiskDecision.Review,
                $"{holds} reservation attempts in the last hour.", ct);

        var monthAgo = now.AddDays(-30);
        var cancellations = await db.Reservations.CountAsync(r => r.GuestId == userId && r.CancelledAt >= monthAgo, ct);
        if (cancellations >= 3)
            await RaiseOnceAsync("User", userId, userId, "RAPID_CANCELLATIONS", 50, RiskDecision.Review,
                $"{cancellations} cancellations in the last 30 days.", ct);

        if (trigger == "registered")
        {
            var ip = await db.AuditLogs.Where(a => a.Action == "user.registered" && a.ActorId == userId).Select(a => a.IpAddress).FirstOrDefaultAsync(ct);
            if (ip is not null)
            {
                var dayAgo = now.AddDays(-1);
                var sameIp = await db.AuditLogs.CountAsync(a => a.Action == "user.registered" && a.IpAddress == ip && a.At >= dayAgo, ct);
                if (sameIp >= 5)
                    await RaiseOnceAsync("User", userId, userId, "MULTIPLE_ACCOUNTS_SAME_IP", 40, RiskDecision.Review,
                        $"{sameIp} accounts registered from the same IP in 24h.", ct);
            }
        }
    }

    private async Task RaiseOnceAsync(string subjectType, Guid subjectId, Guid? userId, string rule, int score, RiskDecision decision, string details, CancellationToken ct)
    {
        var dayAgo = clock.GetUtcNow().AddDays(-1);
        if (await db.FraudChecks.AnyAsync(f => f.SubjectId == subjectId && f.RuleCode == rule && f.CreatedAt >= dayAgo, ct)) return;
        db.FraudChecks.Add(FraudCheck.Raise(subjectType, subjectId, userId, rule, score, decision, details, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
    }
}
