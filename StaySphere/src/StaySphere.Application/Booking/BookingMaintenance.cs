using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StaySphere.Application.Abstractions;
using StaySphere.Application.Engagement;
using StaySphere.Domain.Booking;

namespace StaySphere.Application.Booking;

/// <summary>
/// Scheduled booking jobs. Safe to run on many worker replicas at once: each reservation update is protected by its
/// rowversion, so a replica that loses a race gets a concurrency exception and simply skips that row.
/// </summary>
public interface IBookingMaintenance
{
    Task<int> ExpireHoldsAsync(CancellationToken ct);
    Task<int> CompleteFinishedStaysAsync(CancellationToken ct);
    Task<int> SendCheckInRemindersAsync(CancellationToken ct);
    Task<int> CleanupAsync(CancellationToken ct);
}

public sealed class BookingMaintenance(
    IAppDbContext db,
    INotificationService notifications,
    TimeProvider clock,
    ILogger<BookingMaintenance> logger) : IBookingMaintenance
{
    /// <summary>Grace period so an in-flight payment is not expired from under the guest.</summary>
    public static readonly TimeSpan PaymentGrace = TimeSpan.FromMinutes(5);

    public async Task<int> ExpireHoldsAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var graceCutoff = now - PaymentGrace;
        var ids = await db.Reservations
            .Where(r => (r.Status == ReservationStatus.Held && r.HoldExpiresAt < now) ||
                        (r.Status == ReservationStatus.PaymentPending && r.HoldExpiresAt < graceCutoff))
            .OrderBy(r => r.HoldExpiresAt).Select(r => r.Id).Take(100).ToListAsync(ct);

        var expired = 0;
        foreach (var id in ids)
        {
            // A PaymentPending reservation may still succeed — only expire if no payment is pending/succeeded.
            if (await db.Payments.AnyAsync(p => p.ReservationId == id &&
                    (p.Status == Domain.Payments.PaymentStatus.Succeeded || p.Status == Domain.Payments.PaymentStatus.Pending), ct))
                continue;

            var reservation = await db.Reservations.Include(r => r.NightRows).FirstAsync(r => r.Id == id, ct);
            if (reservation.Expire(now).IsFailure) continue;
            try
            {
                await db.SaveChangesAsync(ct);
                expired++;
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ResetTracking(); // Another replica (or a payment) got there first.
            }
        }

        if (expired > 0) logger.LogInformation("Expired {Count} reservation holds", expired);
        return expired;
    }

    public async Task<int> CompleteFinishedStaysAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var ids = await db.Reservations.Where(r => r.Status == ReservationStatus.Confirmed && r.CheckOut <= today)
            .Select(r => r.Id).Take(200).ToListAsync(ct);
        var completed = 0;
        foreach (var id in ids)
        {
            var reservation = await db.Reservations.FirstAsync(r => r.Id == id, ct);
            if (reservation.Complete(now).IsFailure) continue;
            try
            {
                await db.SaveChangesAsync(ct);
                completed++;
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ResetTracking();
            }
        }

        return completed;
    }

    public async Task<int> SendCheckInRemindersAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var target = today.AddDays(2);
        var due = await db.Reservations.AsNoTracking()
            .Where(r => r.Status == ReservationStatus.Confirmed && r.CheckIn == target)
            .Select(r => new { r.Id, r.GuestId, Title = db.Properties.Where(p => p.Id == r.PropertyId).Select(p => p.Title).First() })
            .ToListAsync(ct);
        var sent = 0;
        foreach (var r in due)
        {
            var link = $"/trips/{r.Id}";
            if (await db.Notifications.AnyAsync(n => n.UserId == r.GuestId && n.Type == "checkin.reminder" && n.Link == link, ct)) continue;
            await notifications.NotifyAsync(r.GuestId, "checkin.reminder", "Your trip is coming up",
                $"Check-in at {r.Title} is in 2 days. Message your host if you have questions.", link, ct, email: true);
            sent++;
        }

        return sent;
    }

    public async Task<int> CleanupAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var removed = await db.IdempotencyRecords.Where(r => r.ExpiresAt < now).ExecuteDeleteAsync(ct);
        var weekAgo = now.AddDays(-7);
        removed += await db.OutboxMessages.Where(m => m.ProcessedAt != null && m.ProcessedAt < weekAgo).ExecuteDeleteAsync(ct);
        removed += await db.InboxMessages.Where(m => m.ProcessedAt < weekAgo).ExecuteDeleteAsync(ct);
        return removed;
    }
}
