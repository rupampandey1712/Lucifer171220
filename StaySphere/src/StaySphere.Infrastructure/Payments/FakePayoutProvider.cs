using System.Collections.Concurrent;
using StaySphere.Application.Abstractions;

namespace StaySphere.Infrastructure.Payments;

/// <summary>
/// Development bank-transfer simulator. Idempotent per payout id. Accounts whose IBAN ends in 0000 are rejected, so the
/// failure + ledger-reversal path can be exercised (e.g. GB00 0000 0000 0000 0000 00).
/// </summary>
public sealed class FakePayoutProvider : IPayoutProvider
{
    private static readonly ConcurrentDictionary<Guid, PayoutTransferResult> Transfers = new();

    public Task<PayoutTransferResult> SendAsync(Guid payoutId, string destination, decimal amount, string currency, CancellationToken cancellationToken) =>
        Task.FromResult(Transfers.GetOrAdd(payoutId, _ => destination.EndsWith("0000", StringComparison.Ordinal)
            ? new PayoutTransferResult(false, null, "The receiving bank rejected the transfer (test account).")
            : new PayoutTransferResult(true, "po_" + Guid.NewGuid().ToString("N")[..20], null)));

    public Task<PayoutTransferResult?> GetStatusAsync(Guid payoutId, CancellationToken cancellationToken) =>
        Task.FromResult(Transfers.TryGetValue(payoutId, out var r) ? r : null);
}
