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
    /// <summary>The host's account at the payout provider (Stripe Connect <c>acct_…</c>) when bank details are collected there.</summary>
    public string? ProviderAccountId { get; private set; }
    /// <summary>False until the provider has verified the host and their bank account (always true for directly entered IBANs).</summary>
    public bool PayoutsEnabled { get; private set; } = true;

    /// <summary>Where transfers go: the provider account when there is one, otherwise the (simulated) bank account.</summary>
    public string Destination => ProviderAccountId ?? MaskedAccount;

    /// <summary>Starts a provider-hosted onboarding: the provider collects identity and bank details, so we never hold them.</summary>
    public static PayoutAccount CreateConnected(Guid hostId, string accountHolder, string providerAccountId, string country, DateTimeOffset now) => new()
    {
        HostId = hostId, AccountHolder = accountHolder.Trim(), MaskedAccount = "Pending setup", Country = country.ToUpperInvariant(),
        ProviderAccountId = providerAccountId, PayoutsEnabled = false, CreatedAt = now, UpdatedAt = now,
    };

    /// <summary>Mirrors the provider's view of the account after onboarding (or a later change on their side).</summary>
    public void SyncConnected(bool payoutsEnabled, string? accountHolder, string? bankLast4, string? country)
    {
        PayoutsEnabled = payoutsEnabled;
        if (!string.IsNullOrWhiteSpace(accountHolder)) AccountHolder = accountHolder.Trim()[..Math.Min(accountHolder.Trim().Length, 100)];
        MaskedAccount = bankLast4 is { Length: 4 } ? $"Bank •••• {bankLast4}" : payoutsEnabled ? "Linked with provider" : "Pending setup";
        if (country is { Length: 2 }) Country = country.ToUpperInvariant();
    }

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
        if (ProviderAccountId is not null) return Error.Validation("payout.provider_account", "This account's bank details are managed by the payout provider.");
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
