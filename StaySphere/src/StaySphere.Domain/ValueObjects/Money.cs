using System.Globalization;

namespace StaySphere.Domain.ValueObjects;

/// <summary>A monetary amount in a specific ISO-4217 currency. Never uses floating point.</summary>
public readonly record struct Money
{
    public Money(decimal amount, string currency)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
            throw new ArgumentException("Currency must be a 3-letter ISO code.", nameof(currency));
        Amount = amount;
        Currency = currency.ToUpperInvariant();
    }

    public decimal Amount { get; }
    public string Currency { get; }

    public static Money Zero(string currency) => new(0m, currency);

    public Money Round() => new(decimal.Round(Amount, 2, MidpointRounding.ToEven), Currency);

    public Money Multiply(decimal factor) => new(Amount * factor, Currency);

    public static Money operator +(Money a, Money b) => new(a.Amount + EnsureSame(a, b).Amount, a.Currency);
    public static Money operator -(Money a, Money b) => new(a.Amount - EnsureSame(a, b).Amount, a.Currency);

    public static Money Add(Money a, Money b) => a + b;
    public static Money Subtract(Money a, Money b) => a - b;

    private static Money EnsureSame(Money a, Money b) =>
        a.Currency == b.Currency
            ? b
            : throw new InvalidOperationException($"Currency mismatch: {a.Currency} vs {b.Currency}.");

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Amount:0.00} {Currency}");
}
