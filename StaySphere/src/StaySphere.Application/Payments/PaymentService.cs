using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StaySphere.Application.Abstractions;
using StaySphere.Application.Common;
using StaySphere.Contracts.Payments;
using StaySphere.Domain.Booking;
using StaySphere.Domain.Common;
using StaySphere.Domain.Payments;
using StaySphere.Domain.Pricing;

namespace StaySphere.Application.Payments;

public interface IPaymentService
{
    Task<Result<PaymentDto>> PayAsync(Guid reservationId, PayReservationRequest request, string idempotencyKey, CancellationToken ct);
    Task<Result<PaymentDto>> GetForReservationAsync(Guid reservationId, CancellationToken ct);
    Task<Result> HandleWebhookAsync(string provider, string payload, string signature, long timestamp, CancellationToken ct);
    Task<Result<PaymentDto>> RefundAsync(Guid paymentId, decimal amount, string reason, CancellationToken ct);
    Task<Result> RefundForCancelledReservationAsync(Guid reservationId, CancellationToken ct);
    Task<int> ReconcilePendingAsync(CancellationToken ct);
    Task<EarningsSummaryDto> GetHostEarningsAsync(DateOnly? from, DateOnly? to, CancellationToken ct);
}

public sealed class PaymentService(
    IAppDbContext db,
    IPaymentProvider provider,
    ICurrentUser currentUser,
    IAuditLogger audit,
    TimeProvider clock,
    ILogger<PaymentService> logger) : IPaymentService
{
    private static readonly Error NotFound = Error.NotFound("reservation.not_found", "Reservation not found.");

    public async Task<Result<PaymentDto>> PayAsync(Guid reservationId, PayReservationRequest request, string idempotencyKey, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var reservation = await db.Reservations.Include(r => r.NightRows).FirstOrDefaultAsync(r => r.Id == reservationId, ct);
        if (reservation is null || reservation.GuestId != userId) return NotFound;

        // Business-level idempotency: a reservation is charged at most once, regardless of HTTP retries.
        var existing = await db.Payments.Include(p => p.Refunds).FirstOrDefaultAsync(
            p => p.ReservationId == reservationId && (p.Status == PaymentStatus.Succeeded || p.Status == PaymentStatus.Pending), ct);
        if (existing is not null) return ToDto(existing, reservation);

        var now = clock.GetUtcNow();
        var pending = reservation.MarkPaymentPending(now);
        if (pending.IsFailure) return pending.Error!;

        var payment = Payment.Start(reservation.Id, userId, reservation.TotalAmount, reservation.Currency, provider.Name, idempotencyKey,
            null, now);
        db.Payments.Add(payment);
        audit.Record("payment.started", nameof(Payment), payment.Id.ToString(), new { reservationId, payment.Amount });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict("payment.in_progress", "A payment for this reservation is already in progress.");
        }
        catch (DbUpdateException ex) when (db.IsUniqueViolation(ex))
        {
            return Error.Conflict("payment.in_progress", "A payment for this reservation is already in progress.");
        }

        ChargeResult charge;
        try
        {
            // Never retried automatically: the provider idempotency key (payment id) makes a manual retry safe.
            charge = await provider.ChargeAsync(new ChargeRequest(payment.Id, payment.Amount, payment.Currency, request.PaymentMethodToken,
                payment.Id.ToString()), ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Payment provider call failed for {PaymentId}; leaving Pending for reconciliation", payment.Id);
            charge = new ChargeResult(ChargeStatus.Pending, null, null, null);
        }

        await ApplyChargeResultAsync(payment, reservation, charge, ct);
        await db.SaveChangesAsync(ct);

        return charge.Status == ChargeStatus.Declined
            ? new Error("payment.declined", charge.FailureReason ?? "Your card was declined.", ErrorType.PaymentRequired)
            : ToDto(payment, reservation);
    }

    public async Task<Result<PaymentDto>> GetForReservationAsync(Guid reservationId, CancellationToken ct)
    {
        var reservation = await db.Reservations.AsNoTracking().FirstOrDefaultAsync(r => r.Id == reservationId, ct);
        if (reservation is null || (reservation.GuestId != currentUser.UserId && !currentUser.IsStaff())) return NotFound;
        var payment = await db.Payments.AsNoTracking().Include(p => p.Refunds).Where(p => p.ReservationId == reservationId)
            .OrderByDescending(p => p.CreatedAt).FirstOrDefaultAsync(ct);
        return payment is null ? Error.NotFound("payment.not_found", "No payment yet.") : ToDto(payment, reservation);
    }

    public async Task<Result> HandleWebhookAsync(string providerName, string payload, string signature, long timestamp, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (!string.Equals(providerName, provider.Name, StringComparison.OrdinalIgnoreCase))
            return Error.NotFound("webhook.provider", "Unknown payment provider.");
        if (!provider.VerifyWebhookSignature(payload, signature, timestamp, now))
            return Error.Unauthorized("webhook.signature", "Invalid webhook signature.");

        FakeWebhookPayload? evt;
        try
        {
            evt = JsonSerializer.Deserialize<FakeWebhookPayload>(payload, Json.Options);
        }
        catch (JsonException)
        {
            return Error.Validation("webhook.payload", "Malformed webhook payload.");
        }

        if (evt is null || string.IsNullOrWhiteSpace(evt.EventId)) return Error.Validation("webhook.payload", "Malformed webhook payload.");

        // Replay protection: the unique (Provider, EventId) index rejects duplicates even under concurrency.
        if (await db.WebhookEvents.AnyAsync(w => w.Provider == provider.Name && w.EventId == evt.EventId, ct))
            return Result.Success();
        var stored = WebhookEvent.Receive(provider.Name, evt.EventId, evt.Type, payload, now);
        db.WebhookEvents.Add(stored);

        var payment = await db.Payments.Include(p => p.Refunds).FirstOrDefaultAsync(p => p.Id == evt.PaymentId, ct);
        if (payment is not null)
        {
            var reservation = await db.Reservations.Include(r => r.NightRows).FirstAsync(r => r.Id == payment.ReservationId, ct);
            var status = evt.Type switch
            {
                "payment.succeeded" => ChargeStatus.Succeeded,
                "payment.failed" => ChargeStatus.Declined,
                _ => ChargeStatus.Pending,
            };
            await ApplyChargeResultAsync(payment, reservation, new ChargeResult(status, evt.PaymentIntentId, evt.FailureReason, null), ct);
        }

        stored.MarkProcessed(now);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (db.IsUniqueViolation(ex))
        {
            db.ResetTracking(); // Concurrent duplicate delivery — the other one won.
        }

        return Result.Success();
    }

    public async Task<Result<PaymentDto>> RefundAsync(Guid paymentId, decimal amount, string reason, CancellationToken ct)
    {
        var payment = await db.Payments.Include(p => p.Refunds).FirstOrDefaultAsync(p => p.Id == paymentId, ct);
        if (payment is null) return Error.NotFound("payment.not_found", "Payment not found.");
        var reservation = await db.Reservations.FirstAsync(r => r.Id == payment.ReservationId, ct);
        var result = await ExecuteRefundAsync(payment, reservation, amount, reason, ct);
        if (result.IsFailure) return result.Error!;
        audit.Record("payment.refunded", nameof(Payment), paymentId.ToString(), new { amount, reason });
        await db.SaveChangesAsync(ct);
        return ToDto(payment, reservation);
    }

    public async Task<Result> RefundForCancelledReservationAsync(Guid reservationId, CancellationToken ct)
    {
        var reservation = await db.Reservations.FirstOrDefaultAsync(r => r.Id == reservationId, ct);
        if (reservation is null || reservation.Status != ReservationStatus.RefundPending) return Result.Success();
        var payment = await db.Payments.Include(p => p.Refunds)
            .FirstOrDefaultAsync(p => p.ReservationId == reservationId && p.Status == PaymentStatus.Succeeded, ct);
        if (payment is null)
        {
            logger.LogWarning("Reservation {ReservationId} is RefundPending but has no successful payment", reservationId);
            return Result.Success();
        }

        var result = await ExecuteRefundAsync(payment, reservation, reservation.RefundAmount, "Reservation cancelled", ct);
        if (result.IsFailure) return result;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<int> ReconcilePendingAsync(CancellationToken ct)
    {
        var cutoff = clock.GetUtcNow().AddMinutes(-1);
        var pending = await db.Payments.Include(p => p.Refunds).Where(p => p.Status == PaymentStatus.Pending && p.CreatedAt < cutoff)
            .OrderBy(p => p.CreatedAt).Take(50).ToListAsync(ct);
        var changed = 0;
        foreach (var payment in pending)
        {
            var status = await provider.GetStatusAsync(payment.Id, ct);
            if (status is null || status.Status == ChargeStatus.Pending)
            {
                // Provider never saw it (e.g. crashed before the call) and the hold is long gone → fail it.
                if (status is null && payment.CreatedAt < clock.GetUtcNow().AddMinutes(-30))
                    status = new ChargeResult(ChargeStatus.Declined, null, "Payment could not be confirmed.", null);
                else continue;
            }

            var reservation = await db.Reservations.Include(r => r.NightRows).FirstAsync(r => r.Id == payment.ReservationId, ct);
            await ApplyChargeResultAsync(payment, reservation, status, ct);
            try
            {
                await db.SaveChangesAsync(ct);
                changed++;
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ResetTracking(); // A webhook or another worker handled it concurrently.
            }
        }

        return changed;
    }

    public async Task<EarningsSummaryDto> GetHostEarningsAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var hostId = currentUser.RequireUserId();
        var start = from is null ? DateTimeOffset.MinValue : new DateTimeOffset(from.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = to is null ? DateTimeOffset.MaxValue : new DateTimeOffset(to.Value.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        var lines = await db.LedgerEntries.AsNoTracking()
            .Where(l => l.HostId == hostId && l.OccurredAt >= start && l.OccurredAt <= end &&
                        (l.Account == LedgerAccount.HostEarning || l.Account == LedgerAccount.Refund || l.Account == LedgerAccount.GuestPayment || l.Account == LedgerAccount.PlatformFee))
            .OrderByDescending(l => l.OccurredAt)
            .Select(l => new LedgerLineDto(l.ReservationId, l.Account.ToString(), l.Amount, l.Currency, l.OccurredAt, l.Description))
            .ToListAsync(ct);

        var currency = lines.FirstOrDefault()?.Currency ?? "USD";
        var gross = lines.Where(l => l.Account == nameof(LedgerAccount.GuestPayment)).Sum(l => l.Amount);
        var fees = lines.Where(l => l.Account == nameof(LedgerAccount.PlatformFee)).Sum(l => l.Amount);
        var refunds = -lines.Where(l => l.Account == nameof(LedgerAccount.Refund)).Sum(l => l.Amount);
        var net = lines.Where(l => l.Account == nameof(LedgerAccount.HostEarning)).Sum(l => l.Amount);
        return new EarningsSummaryDto(currency, gross, fees, refunds, net,
            lines.Where(l => l.Account == nameof(LedgerAccount.HostEarning) || l.Account == nameof(LedgerAccount.Refund)).Take(200).ToList());
    }

    private async Task ApplyChargeResultAsync(Payment payment, Reservation reservation, ChargeResult charge, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        switch (charge.Status)
        {
            case ChargeStatus.Succeeded:
                if (!payment.MarkSucceeded(charge.ProviderPaymentId ?? payment.Id.ToString("N"), now)) return;
                if (reservation.Confirm(now).IsSuccess)
                {
                    WriteConfirmationLedger(reservation, now);
                    audit.Record("reservation.confirmed", nameof(Reservation), reservation.Id.ToString(), actorOverride: reservation.GuestId);
                    logger.LogInformation("Reservation confirmed. ReservationId={ReservationId}, PaymentId={PaymentId}", reservation.Id, payment.Id);
                }
                else
                {
                    // Late success after the hold expired: never keep money for a booking we cannot honour.
                    logger.LogWarning("Payment {PaymentId} succeeded for {Status} reservation {ReservationId}; refunding",
                        payment.Id, reservation.Status, reservation.Id);
                    await ExecuteRefundAsync(payment, reservation, payment.Amount, "Hold expired before payment completed", ct);
                }

                break;
            case ChargeStatus.Declined:
                if (payment.MarkFailed(charge.FailureReason ?? "Declined", charge.ProviderPaymentId, now))
                {
                    reservation.Fail(charge.FailureReason ?? "Payment declined", now);
                    audit.Record("payment.failed", nameof(Payment), payment.Id.ToString(), new { charge.FailureReason }, reservation.GuestId);
                }

                break;
            case ChargeStatus.Pending:
            default:
                break;
        }
    }

    private async Task<Result> ExecuteRefundAsync(Payment payment, Reservation reservation, decimal amount, string reason, CancellationToken ct)
    {
        if (amount <= 0) return Result.Success();
        var refund = payment.RequestRefund(amount, reason, clock.GetUtcNow());
        if (refund.IsFailure) return refund.Error!;

        var result = await provider.RefundAsync(payment.ProviderPaymentId!, amount, refund.Value.Id.ToString(), ct);
        if (!result.Succeeded)
        {
            logger.LogError("Refund {RefundId} failed at provider: {Reason}", refund.Value.Id, result.FailureReason);
            return new Error("refund.failed", "The refund could not be processed. It will be retried.", ErrorType.Unavailable);
        }

        var now = clock.GetUtcNow();
        payment.CompleteRefund(refund.Value.Id, result.ProviderRefundId!, now);
        reservation.MarkRefunded(now);
        WriteRefundLedger(reservation, amount, now);
        return Result.Success();
    }

    /// <summary>Immutable ledger entries for a confirmed booking: guest paid = platform fees + taxes + host earning.</summary>
    private void WriteConfirmationLedger(Reservation r, DateTimeOffset now)
    {
        var hostFee = decimal.Round((r.BaseAmount - r.Discount + r.CleaningFee) * PricingSettings.Default.HostFeePercent / 100m, 2);
        var hostEarning = r.TotalAmount - r.ServiceFee - r.Taxes - hostFee;
        db.LedgerEntries.AddRange(
            LedgerEntry.Create(r.Id, r.HostId, LedgerAccount.GuestPayment, r.TotalAmount, r.Currency, now, "Guest payment captured"),
            LedgerEntry.Create(r.Id, r.HostId, LedgerAccount.PlatformFee, r.ServiceFee + hostFee, r.Currency, now, "Guest service fee + host fee"),
            LedgerEntry.Create(r.Id, r.HostId, LedgerAccount.TaxesPayable, r.Taxes, r.Currency, now, "Occupancy taxes collected"),
            LedgerEntry.Create(r.Id, r.HostId, LedgerAccount.HostEarning, hostEarning, r.Currency, now, "Host earning"));
    }

    /// <summary>Compensating entries — the original rows are never modified.</summary>
    private void WriteRefundLedger(Reservation r, decimal amount, DateTimeOffset now)
    {
        var share = r.TotalAmount == 0 ? 0 : amount / r.TotalAmount;
        var hostFee = decimal.Round((r.BaseAmount - r.Discount + r.CleaningFee) * PricingSettings.Default.HostFeePercent / 100m, 2);
        var hostEarning = r.TotalAmount - r.ServiceFee - r.Taxes - hostFee;
        db.LedgerEntries.AddRange(
            LedgerEntry.Create(r.Id, r.HostId, LedgerAccount.Refund, -amount, r.Currency, now, "Refund to guest"),
            LedgerEntry.Create(r.Id, r.HostId, LedgerAccount.HostEarning, -hostEarning * share, r.Currency, now, "Host earning reversal (refund)"),
            LedgerEntry.Create(r.Id, r.HostId, LedgerAccount.PlatformFee, -(r.ServiceFee + hostFee) * share, r.Currency, now, "Fee reversal (refund)"),
            LedgerEntry.Create(r.Id, r.HostId, LedgerAccount.TaxesPayable, -r.Taxes * share, r.Currency, now, "Tax reversal (refund)"));
    }

    private static PaymentDto ToDto(Payment p, Reservation r) =>
        new(p.Id, p.ReservationId, p.Amount, p.Currency, p.Status.ToString(), p.Provider, p.CardLast4, p.FailureReason,
            p.RefundedAmount, p.CreatedAt, r.Status.ToString());
}
