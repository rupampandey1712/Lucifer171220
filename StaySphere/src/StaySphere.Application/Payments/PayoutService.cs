using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StaySphere.Application.Abstractions;
using StaySphere.Application.Common;
using StaySphere.Contracts;
using StaySphere.Contracts.Payments;
using StaySphere.Domain.Booking;
using StaySphere.Domain.Common;
using StaySphere.Domain.Payments;

namespace StaySphere.Application.Payments;

public interface IPayoutService
{
    Task<PayoutSummaryDto> GetSummaryAsync(CancellationToken ct);
    Task<Result<PayoutAccountDto>> SetAccountAsync(PayoutAccountRequest request, CancellationToken ct);
    Task<Result<IReadOnlyList<PayoutDto>>> RequestPayoutAsync(CancellationToken ct);
    Task<int> RunScheduledPayoutsAsync(CancellationToken ct);
    Task<PagedResult<PayoutDto>> ListAllAsync(string? status, int page, int pageSize, CancellationToken ct);
}

/// <summary>
/// Host payouts. Earnings for a stay become <b>available</b> 24 hours after check-in (protects guests against no-shows
/// and misrepresented listings) and are paid in each listing currency.
/// Outstanding per reservation = Σ HostEarning ledger entries (incl. refund reversals) + Σ Payout entries (negative).
/// Paying writes negative Payout entries in the same transaction as the payout record, so the same money can never be
/// paid twice; a filtered unique index allows only one in-flight payout per host and currency.
/// </summary>
public sealed class PayoutService(
    IAppDbContext db,
    IPayoutProvider provider,
    ICurrentUser currentUser,
    IAuditLogger audit,
    TimeProvider clock,
    ILogger<PayoutService> logger) : IPayoutService
{
    public static readonly TimeSpan ReleaseDelayAfterCheckIn = TimeSpan.FromHours(24);
    public const string Schedule = "Earnings become available 24 hours after check-in and are paid out automatically every day.";

    public async Task<PayoutSummaryDto> GetSummaryAsync(CancellationToken ct)
    {
        var hostId = currentUser.RequireUserId();
        var account = await db.PayoutAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.HostId == hostId, ct);
        var lines = await OutstandingAsync(hostId, ct);
        var paidOut = await db.HostPayouts.AsNoTracking().Where(p => p.HostId == hostId && p.Status == PayoutStatus.Paid)
            .GroupBy(p => p.Currency).Select(g => new { g.Key, Sum = g.Sum(p => p.Amount) }).ToListAsync(ct);

        var currencies = lines.Select(l => l.Currency).Concat(paidOut.Select(p => p.Key)).Distinct().Order();
        var balances = currencies.Select(c => new CurrencyBalanceDto(c,
            decimal.Round(lines.Where(l => l.Currency == c && l.Available && l.Outstanding > 0).Sum(l => l.Outstanding), 2),
            decimal.Round(lines.Where(l => l.Currency == c && !l.Available && l.Outstanding > 0).Sum(l => l.Outstanding), 2),
            paidOut.FirstOrDefault(p => p.Key == c)?.Sum ?? 0)).ToList();

        var history = await Project(db.HostPayouts.AsNoTracking().Where(p => p.HostId == hostId).OrderByDescending(p => p.CreatedAt).Take(50))
            .ToListAsync(ct);
        return new PayoutSummaryDto(account is null ? null : new PayoutAccountDto(account.AccountHolder, account.MaskedAccount, account.Country),
            balances, history, Schedule);
    }

    public async Task<Result<PayoutAccountDto>> SetAccountAsync(PayoutAccountRequest request, CancellationToken ct)
    {
        var hostId = currentUser.RequireUserId();
        var account = await db.PayoutAccounts.FirstOrDefaultAsync(a => a.HostId == hostId, ct);
        if (account is null)
        {
            var created = PayoutAccount.Create(hostId, request.AccountHolder, request.Iban, request.Country, clock.GetUtcNow());
            if (created.IsFailure) return created.Error!;
            account = created.Value;
            db.PayoutAccounts.Add(account);
        }
        else
        {
            var updated = account.Update(request.AccountHolder, request.Iban, request.Country);
            if (updated.IsFailure) return updated.Error!;
        }

        // Never log or audit the account number itself.
        audit.Record("payout.account_updated", nameof(PayoutAccount), hostId.ToString(), new { account.MaskedAccount });
        await db.SaveChangesAsync(ct);
        return new PayoutAccountDto(account.AccountHolder, account.MaskedAccount, account.Country);
    }

    public async Task<Result<IReadOnlyList<PayoutDto>>> RequestPayoutAsync(CancellationToken ct)
    {
        var hostId = currentUser.RequireUserId();
        var account = await db.PayoutAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.HostId == hostId, ct);
        if (account is null) return Error.Validation("payout.no_account", "Add a payout account before requesting a payout.");

        var results = new List<PayoutDto>();
        var lines = await OutstandingAsync(hostId, ct);
        foreach (var currency in lines.Where(l => l.Available && l.Outstanding > 0).Select(l => l.Currency).Distinct())
        {
            var payout = await PayAsync(hostId, currency, account.MaskedAccount, automatic: false, ct);
            if (payout.IsFailure && payout.Error!.Type == ErrorType.Conflict) return payout.Error;
            if (payout.IsSuccess) results.Add(payout.Value);
        }

        return results.Count == 0
            ? Error.Validation("payout.nothing_available", "There are no available earnings to pay out yet.")
            : results;
    }

    public async Task<int> RunScheduledPayoutsAsync(CancellationToken ct)
    {
        await ReconcileStuckAsync(ct);
        var hosts = await db.PayoutAccounts.AsNoTracking().Select(a => new { a.HostId, a.MaskedAccount }).ToListAsync(ct);
        var paid = 0;
        foreach (var host in hosts)
        {
            var lines = await OutstandingAsync(host.HostId, ct);
            foreach (var currency in lines.Where(l => l.Available && l.Outstanding > 0).Select(l => l.Currency).Distinct())
            {
                var result = await PayAsync(host.HostId, currency, host.MaskedAccount, automatic: true, ct);
                if (result.IsSuccess && result.Value.Status == nameof(PayoutStatus.Paid)) paid++;
            }
        }

        return paid;
    }

    public async Task<PagedResult<PayoutDto>> ListAllAsync(string? status, int page, int pageSize, CancellationToken ct)
    {
        (page, pageSize) = Paging.Normalize(page, pageSize, 100);
        var q = db.HostPayouts.AsNoTracking();
        if (Enum.TryParse<PayoutStatus>(status, true, out var s)) q = q.Where(p => p.Status == s);
        var total = await q.CountAsync(ct);
        var items = await Project(q.OrderByDescending(p => p.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync(ct);
        return new PagedResult<PayoutDto>(items, page, pageSize, total);
    }

    private async Task<Result<PayoutDto>> PayAsync(Guid hostId, string currency, string destination, bool automatic, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var items = (await OutstandingAsync(hostId, ct))
            .Where(l => l.Currency == currency && l.Available && l.Outstanding > 0)
            .Select(l => (l.ReservationId, l.Outstanding)).ToList();
        var created = HostPayout.Create(hostId, currency, destination, items, automatic, now);
        if (created.IsFailure) return created.Error!;
        var payout = created.Value;

        db.HostPayouts.Add(payout);
        foreach (var (reservationId, amount) in items)
            db.LedgerEntries.Add(LedgerEntry.Create(reservationId, hostId, LedgerAccount.Payout, -amount, currency, now, $"Payout {payout.Id.ToString()[..8]}"));
        audit.Record("payout.created", nameof(HostPayout), payout.Id.ToString(), new { payout.Amount, currency, automatic }, automatic ? null : hostId);

        try
        {
            await db.SaveChangesAsync(ct); // reserve the money first (atomic with the ledger entries)
        }
        catch (DbUpdateException ex) when (db.IsUniqueViolation(ex))
        {
            db.ResetTracking();
            return Error.Conflict("payout.in_progress", "A payout is already being processed. Please try again shortly.");
        }

        var transfer = await provider.SendAsync(payout.Id, destination, payout.Amount, currency, ct);
        Apply(payout, transfer, now);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Payout {PayoutId} for host {HostId}: {Status} {Amount} {Currency}", payout.Id, hostId, payout.Status, payout.Amount, currency);
        return (await Project(db.HostPayouts.Where(p => p.Id == payout.Id)).FirstAsync(ct));
    }

    private void Apply(HostPayout payout, PayoutTransferResult transfer, DateTimeOffset now)
    {
        if (transfer.Succeeded)
        {
            payout.MarkPaid(transfer.ProviderPayoutId!, now);
            return;
        }

        payout.MarkFailed(transfer.FailureReason ?? "Transfer failed");
        // Compensating entries: the money becomes available again for the next attempt.
        foreach (var item in payout.Items)
            db.LedgerEntries.Add(LedgerEntry.Create(item.ReservationId, payout.HostId, LedgerAccount.Payout, item.Amount, payout.Currency, now,
                $"Payout {payout.Id.ToString()[..8]} failed — reversed"));
    }

    /// <summary>Payouts left Processing by a crash between "reserve" and "transfer" are resolved against the provider.</summary>
    private async Task ReconcileStuckAsync(CancellationToken ct)
    {
        var cutoff = clock.GetUtcNow().AddMinutes(-10);
        var stuck = await db.HostPayouts.Include(p => p.Items).Where(p => p.Status == PayoutStatus.Processing && p.CreatedAt < cutoff).ToListAsync(ct);
        foreach (var payout in stuck)
        {
            var status = await provider.GetStatusAsync(payout.Id, ct)
                ?? await provider.SendAsync(payout.Id, payout.Destination, payout.Amount, payout.Currency, ct); // provider is idempotent on payout id
            Apply(payout, status, clock.GetUtcNow());
        }

        if (stuck.Count > 0) await db.SaveChangesAsync(ct);
    }

    private sealed record OutstandingLine(Guid ReservationId, string Currency, decimal Outstanding, bool Available);

    private async Task<List<OutstandingLine>> OutstandingAsync(Guid hostId, CancellationToken ct)
    {
        var releaseBefore = DateOnly.FromDateTime(clock.GetUtcNow().Subtract(ReleaseDelayAfterCheckIn).UtcDateTime);
        var rows = await db.LedgerEntries.AsNoTracking()
            .Where(l => l.HostId == hostId && (l.Account == LedgerAccount.HostEarning || l.Account == LedgerAccount.Payout))
            .GroupBy(l => new { l.ReservationId, l.Currency })
            .Select(g => new { g.Key.ReservationId, g.Key.Currency, Outstanding = g.Sum(x => x.Amount) })
            .Join(db.Reservations, l => l.ReservationId, r => r.Id, (l, r) => new { l.ReservationId, l.Currency, l.Outstanding, r.CheckIn, r.Status })
            .ToListAsync(ct);
        return rows.Select(r => new OutstandingLine(r.ReservationId, r.Currency.Trim(), decimal.Round(r.Outstanding, 2),
            r.CheckIn <= releaseBefore && r.Status is ReservationStatus.Confirmed or ReservationStatus.Completed or ReservationStatus.Cancelled
                or ReservationStatus.Refunded or ReservationStatus.RefundPending)).ToList();
    }

    private IQueryable<PayoutDto> Project(IQueryable<HostPayout> query) =>
        query.Select(p => new PayoutDto(p.Id, p.HostId, db.Users.Where(u => u.Id == p.HostId).Select(u => u.DisplayName).FirstOrDefault(),
            p.Amount, p.Currency, p.Status.ToString(), p.Destination, p.Automatic, p.FailureReason, p.CreatedAt, p.PaidAt, p.Items.Count));
}
