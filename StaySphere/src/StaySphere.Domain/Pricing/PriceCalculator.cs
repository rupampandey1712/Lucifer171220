using StaySphere.Domain.Catalog;
using StaySphere.Domain.Common;
using StaySphere.Domain.ValueObjects;

namespace StaySphere.Domain.Pricing;

public sealed record NightlyRate(DateOnly Night, decimal Amount, string Reason);

public sealed record PriceBreakdown(
    string Currency,
    int Nights,
    IReadOnlyList<NightlyRate> NightlyRates,
    decimal Accommodation,
    decimal LengthOfStayDiscount,
    decimal CouponDiscount,
    decimal CleaningFee,
    decimal ServiceFee,
    decimal Taxes,
    decimal Total)
{
    public decimal Discount => LengthOfStayDiscount + CouponDiscount;
    public decimal AverageNightly => Nights == 0 ? 0 : decimal.Round(Accommodation / Nights, 2);
}

public sealed record PricingSettings(decimal ServiceFeePercent, decimal HostFeePercent, decimal DefaultTaxPercent)
{
    public static readonly PricingSettings Default = new(12m, 3m, 10m);
}

/// <summary>
/// Server-authoritative price calculation. Pure function of property pricing data + stay + coupon so it is
/// trivially unit-testable and cannot be influenced by the client.
/// Order: nightly (seasonal or base, weekend adjusted) → length-of-stay discount → coupon → cleaning →
/// service fee on (accommodation − discounts + cleaning) → taxes on the same base → total.
/// </summary>
public static class PriceCalculator
{
    public static Result<PriceBreakdown> Calculate(Property property, DateRange stay, Coupon? coupon,
        PricingSettings settings, decimal taxPercent, DateTimeOffset now)
    {
        if (stay.Nights < property.MinNights)
            return Error.Validation("pricing.min_nights", $"This place has a minimum stay of {property.MinNights} nights.");

        var rates = new List<NightlyRate>(stay.Nights);
        foreach (var night in stay.EachNight())
        {
            var season = property.SeasonalPrices.FirstOrDefault(s => night >= s.Start && night < s.End);
            var amount = season?.NightlyPrice ?? property.BasePrice;
            var reason = season is null ? "base" : "seasonal";
            if (night.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday && property.WeekendAdjustmentPercent != 0)
            {
                amount += amount * property.WeekendAdjustmentPercent / 100m;
                reason += "+weekend";
            }

            rates.Add(new NightlyRate(night, Round(amount), reason));
        }

        var accommodation = rates.Sum(r => r.Amount);

        var losPercent = stay.Nights >= 28 && property.MonthlyDiscountPercent > 0 ? property.MonthlyDiscountPercent
            : stay.Nights >= 7 ? property.WeeklyDiscountPercent
            : 0m;
        var losDiscount = Round(accommodation * losPercent / 100m);

        var couponDiscount = 0m;
        if (coupon is not null)
        {
            var valid = coupon.Validate(now, accommodation - losDiscount, property.Currency);
            if (valid.IsFailure) return valid.Error!;
            couponDiscount = coupon.DiscountFor(accommodation - losDiscount);
        }

        var cleaning = Round(property.CleaningFee);
        var feeBase = accommodation - losDiscount - couponDiscount + cleaning;
        var serviceFee = Round(feeBase * settings.ServiceFeePercent / 100m);
        var taxes = Round(feeBase * taxPercent / 100m);
        var total = feeBase + serviceFee + taxes;

        return new PriceBreakdown(property.Currency, stay.Nights, rates, accommodation, losDiscount, couponDiscount,
            cleaning, serviceFee, taxes, Round(total));
    }

    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.ToEven);
}

public sealed class Coupon : AggregateRoot
{
    private Coupon() { }

    public string Code { get; private set; } = default!;
    public decimal PercentOff { get; private set; }
    public decimal? AmountOff { get; private set; }
    public string? Currency { get; private set; }
    public decimal MinimumSpend { get; private set; }
    public DateTimeOffset ValidFrom { get; private set; }
    public DateTimeOffset ValidTo { get; private set; }
    public int MaxRedemptions { get; private set; }
    public int Redemptions { get; private set; }
    public bool IsActive { get; private set; } = true;

    public static Coupon Create(string code, decimal percentOff, decimal? amountOff, string? currency, decimal minimumSpend,
        DateTimeOffset validFrom, DateTimeOffset validTo, int maxRedemptions)
    {
        if (percentOff is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(percentOff));
        return new Coupon
        {
            Code = code.Trim().ToUpperInvariant(),
            PercentOff = percentOff,
            AmountOff = amountOff,
            Currency = currency?.ToUpperInvariant(),
            MinimumSpend = minimumSpend,
            ValidFrom = validFrom,
            ValidTo = validTo,
            MaxRedemptions = maxRedemptions,
            CreatedAt = validFrom,
            UpdatedAt = validFrom,
        };
    }

    public Result Validate(DateTimeOffset now, decimal spend, string currency)
    {
        if (!IsActive || now < ValidFrom || now > ValidTo)
            return Error.Validation("coupon.expired", "This coupon is not valid right now.");
        if (Redemptions >= MaxRedemptions)
            return Error.Validation("coupon.exhausted", "This coupon has been fully redeemed.");
        if (AmountOff is not null && Currency != currency)
            return Error.Validation("coupon.currency", "This coupon cannot be used with this currency.");
        if (spend < MinimumSpend)
            return Error.Validation("coupon.minimum", $"This coupon requires a minimum spend of {MinimumSpend:0.00}.");
        return Result.Success();
    }

    public decimal DiscountFor(decimal amount) =>
        decimal.Round(Math.Min(amount, AmountOff ?? amount * PercentOff / 100m), 2);

    public void Redeem() => Redemptions++;

    public void Deactivate() => IsActive = false;
}
