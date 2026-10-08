using StaySphere.Domain.Common;

namespace StaySphere.Domain.Payments;

/// <summary>Where a host receives money. Only a masked account reference is stored — never full bank details.</summary>
public sealed class PayoutAccount : AggregateRoot
{
    private PayoutAccount() { }

    public Guid HostId { get; private set; }
    public string AccountHolder { get; private set; } = default!;
    /// <summary>e.g. "PT50 •••• 1234".</summary>
    public string MaskedAccount { get; private set; } = default!;
    public string Country { get; private set; } = default!;

    public static Result<PayoutAccount> Create(Guid hostId, string accountHolder, string iban, string country, DateTimeOffset now)
    {
        var result = Validate(accountHolder, iban, country);
        if (result.IsFailure) return result.Error!;
        return new PayoutAccount
        {
            HostId = hostId, AccountHolder = accountHolder.Trim(), MaskedAccount = Mask(iban), Country = country.ToUpperInvariant(),
            CreatedAt = now, UpdatedAt = now,
        };
    }

    public Result Update(string accountHolder, string iban, string country)
    {
        var result = Validate(accountHolder, iban, country);
        if (result.IsFailure) return result;
        AccountHolder = accountHolder.Trim();
        MaskedAccount = Mask(iban);
        Country = country.ToUpperInvariant();
        return Result.Success();
    }

    private static Result Validate(string holder, string iban, string country)
    {
        var compact = new string(iban.Where(char.IsLetterOrDigit).ToArray());
        if (string.IsNullOrWhiteSpace(holder) || holder.Length > 100) return Error.Validation("payout.holder", "Account holder name is required.");
        if (compact.Length is < 12 or > 34 || !char.IsLetter(compact[0]) || !char.IsLetter(compact[1]))
            return Error.Validation("payout.iban", "Enter a valid IBAN (test values are fine, e.g. PT50000201231234567890154).");
        if (country.Length != 2) return Error.Validation("payout.country", "Country must be a 2-letter code.");
        return Result.Success();
    }

    private static string Mask(string iban)
    {
        var compact = new string(iban.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return $"{compact[..4]} •••• {compact[^4..]}";
    }
}

public enum PayoutStatus
{
    Processing,
    Paid,
    Failed,
}

/// <summary>
/// A transfer of available earnings to a host, in one currency. Its items say exactly which reservations' earnings it
/// covers; matching negative <see cref="LedgerAccount.Payout"/> ledger entries make the money "spent" so it can never be
/// paid twice. A filtered unique index allows only one Processing payout per host + currency at a time.
/// </summary>
public sealed class HostPayout : AggregateRoot
{
    private readonly List<HostPayoutItem> _items = [];

    private HostPayout() { }

    public Guid HostId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = default!;
    public PayoutStatus Status { get; private set; }
    public string Destination { get; private set; } = default!;
    public string? ProviderPayoutId { get; private set; }
    public string? FailureReason { get; private set; }
    public bool Automatic { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }
    public IReadOnlyCollection<HostPayoutItem> Items => _items;

    public const decimal MinimumAmount = 1m;

    public static Result<HostPayout> Create(Guid hostId, string currency, string destination, IReadOnlyList<(Guid ReservationId, decimal Amount)> items,
        bool automatic, DateTimeOffset now)
    {
        var total = items.Sum(i => i.Amount);
        if (items.Count == 0 || total < MinimumAmount)
            return Error.Validation("payout.nothing_available", "There are no available earnings to pay out yet.");
        if (items.Any(i => i.Amount <= 0)) throw new ArgumentException("Payout items must be positive.", nameof(items));
        var payout = new HostPayout
        {
            HostId = hostId, Currency = currency, Amount = decimal.Round(total, 2), Destination = destination, Status = PayoutStatus.Processing,
            Automatic = automatic, CreatedAt = now, UpdatedAt = now,
        };
        foreach (var (reservationId, amount) in items) payout._items.Add(new HostPayoutItem(payout.Id, reservationId, decimal.Round(amount, 2)));
        return payout;
    }

    public void MarkPaid(string providerPayoutId, DateTimeOffset now)
    {
        if (Status != PayoutStatus.Processing) return;
        Status = PayoutStatus.Paid;
        ProviderPayoutId = providerPayoutId;
        PaidAt = now;
        Raise(new PayoutPaidDomainEvent(Id, HostId, Amount, Currency));
    }

    public void MarkFailed(string reason)
    {
        if (Status != PayoutStatus.Processing) return;
        Status = PayoutStatus.Failed;
        FailureReason = reason;
    }
}

public sealed class HostPayoutItem : Entity
{
    private HostPayoutItem() { }

    internal HostPayoutItem(Guid payoutId, Guid reservationId, decimal amount)
    {
        PayoutId = payoutId;
        ReservationId = reservationId;
        Amount = amount;
    }

    public Guid PayoutId { get; private set; }
    public Guid ReservationId { get; private set; }
    public decimal Amount { get; private set; }
}

public sealed record PayoutPaidDomainEvent(Guid PayoutId, Guid HostId, decimal Amount, string Currency) : IDomainEvent;
