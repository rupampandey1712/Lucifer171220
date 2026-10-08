using StaySphere.Domain.Pricing;

namespace StaySphere.UnitTests.Domain;

public sealed class PricingTests
{
    // 2030-06-03 is a Monday.
    private static readonly DateOnly Monday = new(2030, 6, 3);

    [Fact]
    public void Calculates_base_cleaning_service_fee_and_taxes()
    {
        var p = TestData.PublishedProperty(price: 100m, cleaning: 50m);

        var b = PriceCalculator.Calculate(p, TestData.Stay(Monday, 3), null, PricingSettings.Default, 10m, TestData.Now).Value;

        b.Nights.ShouldBe(3);
        b.Accommodation.ShouldBe(300m);
        b.CleaningFee.ShouldBe(50m);
        b.ServiceFee.ShouldBe(42m);      // 12% of 350
        b.Taxes.ShouldBe(35m);           // 10% of 350
        b.Total.ShouldBe(427m);
        b.Currency.ShouldBe("EUR");
    }

    [Fact]
    public void Applies_weekend_adjustment_to_friday_and_saturday_only()
    {
        var p = TestData.PublishedProperty(price: 100m, cleaning: 0m, weekendPercent: 20m);
        var thursday = Monday.AddDays(3);

        var b = PriceCalculator.Calculate(p, TestData.Stay(thursday, 4), null, PricingSettings.Default, 0m, TestData.Now).Value;

        b.NightlyRates.Select(r => r.Amount).ShouldBe([100m, 120m, 120m, 100m]);
        b.Accommodation.ShouldBe(440m);
    }

    [Fact]
    public void Seasonal_price_overrides_base_price_for_nights_in_season()
    {
        var p = TestData.PublishedProperty(price: 100m, cleaning: 0m);
        p.AddSeasonalPrice(Monday.AddDays(1), Monday.AddDays(3), 200m).IsSuccess.ShouldBeTrue();

        var b = PriceCalculator.Calculate(p, TestData.Stay(Monday, 4), null, PricingSettings.Default, 0m, TestData.Now).Value;

        b.NightlyRates.Select(r => r.Amount).ShouldBe([100m, 200m, 200m, 100m]);
    }

    [Fact]
    public void Weekly_discount_applies_from_seven_nights()
    {
        var p = TestData.PublishedProperty(price: 100m, cleaning: 0m, weeklyDiscount: 10m);

        var six = PriceCalculator.Calculate(p, TestData.Stay(Monday, 6), null, PricingSettings.Default, 0m, TestData.Now).Value;
        var seven = PriceCalculator.Calculate(p, TestData.Stay(Monday, 7), null, PricingSettings.Default, 0m, TestData.Now).Value;

        six.LengthOfStayDiscount.ShouldBe(0m);
        seven.LengthOfStayDiscount.ShouldBe(70m);
    }

    [Fact]
    public void Coupon_discount_is_applied_and_validated()
    {
        var p = TestData.PublishedProperty(price: 100m, cleaning: 0m);
        var coupon = Coupon.Create("SAVE10", 10, null, null, 0, TestData.Now.AddDays(-1), TestData.Now.AddDays(10), 5);
        var expired = Coupon.Create("OLD", 10, null, null, 0, TestData.Now.AddDays(-10), TestData.Now.AddDays(-1), 5);

        var b = PriceCalculator.Calculate(p, TestData.Stay(Monday, 2), coupon, PricingSettings.Default, 0m, TestData.Now).Value;
        b.CouponDiscount.ShouldBe(20m);

        PriceCalculator.Calculate(p, TestData.Stay(Monday, 2), expired, PricingSettings.Default, 0m, TestData.Now).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Minimum_nights_is_enforced()
    {
        var p = TestData.PublishedProperty();
        p.UpdatePricing(100m, 0m, "EUR", 0, 0, 0, 3);

        var result = PriceCalculator.Calculate(p, TestData.Stay(Monday, 2), null, PricingSettings.Default, 0m, TestData.Now);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("pricing.min_nights");
    }

    [Fact]
    public void Seasonal_prices_cannot_overlap()
    {
        var p = TestData.PublishedProperty();
        p.AddSeasonalPrice(Monday, Monday.AddDays(10), 150m).IsSuccess.ShouldBeTrue();

        p.AddSeasonalPrice(Monday.AddDays(5), Monday.AddDays(12), 160m).IsFailure.ShouldBeTrue();
    }
}
