using StaySphere.Domain.Booking;
using StaySphere.Domain.Payments;
using StaySphere.Domain.Pricing;

namespace StaySphere.Application.Payments;

/// <summary>The single source of the platform's accounting rules (append-only, compensating entries only).</summary>
public static class Ledger
{
    public static decimal HostFee(Reservation r) =>
        decimal.Round((r.BaseAmount - r.Discount + r.CleaningFee) * PricingSettings.Default.HostFeePercent / 100m, 2);

    public static decimal HostEarning(Reservation r) => r.TotalAmount - r.ServiceFee - r.Taxes - HostFee(r);

    /// <summary>Guest payment = platform fees + taxes + host earning.</summary>
    public static IEnumerable<LedgerEntry> ForConfirmation(Reservation r, DateTimeOffset at)
    {
        var hostFee = HostFee(r);
        yield return LedgerEntry.Create(r.Id, r.HostId, LedgerAccount.GuestPayment, r.TotalAmount, r.Currency, at, "Guest payment captured");
        yield return LedgerEntry.Create(r.Id, r.HostId, LedgerAccount.PlatformFee, r.ServiceFee + hostFee, r.Currency, at, "Guest service fee + host fee");
        yield return LedgerEntry.Create(r.Id, r.HostId, LedgerAccount.TaxesPayable, r.Taxes, r.Currency, at, "Occupancy taxes collected");
        yield return LedgerEntry.Create(r.Id, r.HostId, LedgerAccount.HostEarning, HostEarning(r), r.Currency, at, "Host earning");
    }

    /// <summary>Compensating entries for a refund, split proportionally across the original lines.</summary>
    public static IEnumerable<LedgerEntry> ForRefund(Reservation r, decimal amount, DateTimeOffset at)
    {
        var share = r.TotalAmount == 0 ? 0 : amount / r.TotalAmount;
        yield return LedgerEntry.Create(r.Id, r.HostId, LedgerAccount.Refund, -amount, r.Currency, at, "Refund to guest");
        yield return LedgerEntry.Create(r.Id, r.HostId, LedgerAccount.HostEarning, -HostEarning(r) * share, r.Currency, at, "Host earning reversal (refund)");
        yield return LedgerEntry.Create(r.Id, r.HostId, LedgerAccount.PlatformFee, -(r.ServiceFee + HostFee(r)) * share, r.Currency, at, "Fee reversal (refund)");
        yield return LedgerEntry.Create(r.Id, r.HostId, LedgerAccount.TaxesPayable, -r.Taxes * share, r.Currency, at, "Tax reversal (refund)");
    }
}
