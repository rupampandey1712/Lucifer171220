using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StaySphere.Application.Abstractions;
using StaySphere.Application.Common;
using StaySphere.Application.Payments;
using StaySphere.Contracts.Booking;
using StaySphere.Domain.Booking;
using StaySphere.Domain.Common;
using StaySphere.Domain.Identity;
using StaySphere.Domain.Payments;

namespace StaySphere.Application.Booking;

/// <summary>
/// Request-to-book: the guest's card is authorized (not charged) and the host has 24 hours to respond.
/// Accept → capture + confirm + ledger. Decline / timeout / guest cancellation → void (nothing is ever charged).
/// </summary>
public interface IBookingRequestService
{
    Task<Result<ReservationDto>> ApproveAsync(Guid reservationId, CancellationToken ct);
    Task<Result<ReservationDto>> DeclineAsync(Guid reservationId, string? reason, CancellationToken ct);
    Task<int> ExpireOverdueAsync(CancellationToken ct);
    Task VoidAuthorizationAsync(Guid reservationId, CancellationToken ct);
}

public sealed class BookingRequestService(
    IAppDbContext db,
    IPaymentProvider provider,
    IReservationService reservations,
    ICurrentUser currentUser,
    IAuditLogger audit,
    TimeProvider clock,
    ILogger<BookingRequestService> logger) : IBookingRequestService
{
    private static readonly Error NotFound = Error.NotFound("reservation.not_found", "Reservation not found.");

    public async Task<Result<ReservationDto>> ApproveAsync(Guid reservationId, CancellationToken ct)
    {
        var (reservation, payment, error) = await LoadForHostAsync(reservationId, ct);
        if (error is not null) return error;
        var now = clock.GetUtcNow();
        if (reservation!.Status == ReservationStatus.Confirmed) return (await reservations.GetAsync(reservationId, ct)).Value; // idempotent
        if (reservation.Status != ReservationStatus.AwaitingApproval)
            return Error.Conflict("reservation.state", $"This request can no longer be accepted (it is {reservation.Status}).");
        if (reservation.ApprovalDeadline < now) return Error.Conflict("reservation.request_expired", "This request has expired.");
        if (payment is null) return Error.Conflict("payment.missing", "No authorized payment was found for this request.");

        var capture = await provider.CaptureAsync(payment.ProviderPaymentId!, payment.Amount, $"capture-{payment.Id}", ct);
        if (capture.Status != ChargeStatus.Succeeded)
        {
            // Authorization no longer valid (e.g. expired at the bank): treat as a failed booking and release the dates.
            payment.MarkFailed(capture.FailureReason ?? "Capture failed", capture.ProviderPaymentId, now);
            reservation.Decline(null, "The guest's payment could not be captured.", now);
            await db.SaveChangesAsync(ct);
            return new Error("payment.capture_failed", "The guest's payment could not be captured, so the request was closed.", ErrorType.PaymentRequired);
        }

        payment.MarkSucceeded(capture.ProviderPaymentId ?? payment.ProviderPaymentId!, now);
        var confirmed = reservation.Confirm(now);
        if (confirmed.IsFailure) return confirmed.Error!;
        db.LedgerEntries.AddRange(Ledger.ForConfirmation(reservation, now));
        audit.Record("reservation.request_accepted", nameof(Reservation), reservationId.ToString());

        if (!await TrySaveAsync(ct)) return Error.Conflict("reservation.concurrency", "This request was just updated. Please refresh.");
        logger.LogInformation("Booking request accepted. ReservationId={ReservationId}", reservationId);
        return (await reservations.GetAsync(reservationId, ct)).Value;
    }

    public async Task<Result<ReservationDto>> DeclineAsync(Guid reservationId, string? reason, CancellationToken ct)
    {
        var (reservation, payment, error) = await LoadForHostAsync(reservationId, ct);
        if (error is not null) return error;
        var now = clock.GetUtcNow();
        // Admins act on the host's behalf; authorization was already checked in LoadForHostAsync.
        var declined = reservation!.Decline(reservation.HostId, reason?.Trim(), now);
        if (declined.IsFailure) return declined.Error!;
        await VoidAsync(payment, now, ct);
        audit.Record("reservation.request_declined", nameof(Reservation), reservationId.ToString(), new { reason });
        if (!await TrySaveAsync(ct)) return Error.Conflict("reservation.concurrency", "This request was just updated. Please refresh.");
        return (await reservations.GetAsync(reservationId, ct)).Value;
    }

    public async Task<int> ExpireOverdueAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var ids = await db.Reservations.Where(r => r.Status == ReservationStatus.AwaitingApproval && r.ApprovalDeadline < now)
            .Select(r => r.Id).Take(100).ToListAsync(ct);
        var expired = 0;
        foreach (var id in ids)
        {
            var reservation = await db.Reservations.Include(r => r.NightRows).FirstAsync(r => r.Id == id, ct);
            if (reservation.Decline(null, null, now).IsFailure) continue;
            await VoidAsync(await AuthorizedPaymentAsync(id, ct), now, ct);
            if (await TrySaveAsync(ct)) expired++;
        }

        if (expired > 0) logger.LogInformation("Expired {Count} unanswered booking requests", expired);
        return expired;
    }

    public async Task VoidAuthorizationAsync(Guid reservationId, CancellationToken ct)
    {
        var payment = await AuthorizedPaymentAsync(reservationId, ct);
        if (payment is null) return;
        await VoidAsync(payment, clock.GetUtcNow(), ct);
        await TrySaveAsync(ct);
    }

    private async Task VoidAsync(Payment? payment, DateTimeOffset now, CancellationToken ct)
    {
        if (payment is null) return;
        if (await provider.VoidAsync(payment.ProviderPaymentId!, $"void-{payment.Id}", ct))
            payment.MarkVoided(now);
        else
            logger.LogError("Voiding authorization {PaymentId} failed; reconciliation required", payment.Id);
    }

    private Task<Payment?> AuthorizedPaymentAsync(Guid reservationId, CancellationToken ct) =>
        db.Payments.Include(p => p.Refunds).FirstOrDefaultAsync(p => p.ReservationId == reservationId && p.Status == PaymentStatus.Authorized, ct);

    private async Task<(Reservation?, Payment?, Error?)> LoadForHostAsync(Guid reservationId, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var reservation = await db.Reservations.Include(r => r.NightRows).FirstOrDefaultAsync(r => r.Id == reservationId, ct);
        // Guests and strangers get 404 (no existence leak); only the listing's host or an admin may respond.
        if (reservation is null || (reservation.HostId != userId && !currentUser.IsInRole(Roles.Admin))) return (null, null, NotFound);
        return (reservation, await AuthorizedPaymentAsync(reservationId, ct), null);
    }

    private async Task<bool> TrySaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ResetTracking();
            return false;
        }
    }
}
