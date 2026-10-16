using StaySphere.Domain.Pricing;

namespace StaySphere.UnitTests.Domain;

public sealed class PromotionTests
{
    // TestData.Now is 2030-06-01; 2030-06-03 is a Monday, two days later.
    private static readonly DateOnly Soon = new(2030, 6, 3);
    private static readonly DateOnly Later = new(2030, 9, 2);

    private static Promotion Make(PromotionKind kind, decimal percent, Guid? hostId = null, Guid? propertyId = null, int? minNights = null,
        int? days = null, DateOnly? stayStart = null, DateOnly? stayEnd = null, DateTimeOffset? validTo = null) =>
        Promotion.Create("Deal", kind, percent, hostId, propertyId, minNights, days, stayStart, stayEnd,
            TestData.Now.AddDays(-1), validTo ?? TestData.Now.AddDays(60), Guid.NewGuid(), TestData.Now).Value;

    [Fact]
    public void Early_bird_applies_only_when_booking_far_enough_ahead()
    {
        var p = TestData.PublishedProperty(price: 100m, cleaning: 0m);
        var promo = Make(PromotionKind.EarlyBird, 10m, days: 30);

        var early = PriceCalculator.Calculate(p, TestData.Stay(Later, 3), null, PricingSettings.Default, 0m, TestData.Now, [promo]).Value;
        var late = PriceCalculator.Calculate(p, TestData.Stay(Soon, 3), null, PricingSettings.Default, 0m, TestData.Now, [promo]).Value;

        early.PromotionDiscount.ShouldBe(30m);
        early.PromotionName.ShouldBe("Deal");
        late.PromotionDiscount.ShouldBe(0m);
        late.PromotionName.ShouldBeNull();
    }

    [Fact]
    public void Last_minute_applies_only_close_to_check_in()
    {
        var p = TestData.PublishedProperty(price: 100m, cleaning: 0m);
        var promo = Make(PromotionKind.LastMinute, 20m, days: 7);

        PriceCalculator.Calculate(p, TestData.Stay(Soon, 2), null, PricingSettings.Default, 0m, TestData.Now, [promo]).Value.PromotionDiscount.ShouldBe(40m);
        PriceCalculator.Calculate(p, TestData.Stay(Later, 2), null, PricingSettings.Default, 0m, TestData.Now, [promo]).Value.PromotionDiscount.ShouldBe(0m);
    }

    [Fact]
    public void Only_the_best_promotion_applies_and_fees_use_the_discounted_price()
    {
        var p = TestData.PublishedProperty(price: 100m, cleaning: 0m);
        var small = Make(PromotionKind.LongStay, 5m, minNights: 2);
        var big = Make(PromotionKind.LongStay, 15m, minNights: 3);

        var b = PriceCalculator.Calculate(p, TestData.Stay(Soon, 4), null, PricingSettings.Default, 10m, TestData.Now, [small, big]).Value;

        b.PromotionDiscount.ShouldBe(60m);                  // 15% of 400, not 20% (no stacking)
        b.Discount.ShouldBe(60m);
        b.ServiceFee.ShouldBe(40.8m);                       // 12% of 340
        b.Taxes.ShouldBe(34m);                              // 10% of 340
        b.Total.ShouldBe(414.8m);
    }

    [Fact]
    public void Promotions_respect_host_listing_stay_window_and_validity()
    {
        var p = TestData.PublishedProperty(price: 100m, cleaning: 0m);
        var otherHost = Make(PromotionKind.LongStay, 10m, hostId: Guid.NewGuid(), minNights: 2);
        var otherListing = Make(PromotionKind.LongStay, 10m, hostId: p.HostId, propertyId: Guid.NewGuid(), minNights: 2);
        var flash = Make(PromotionKind.FlashSale, 10m, stayStart: Later, stayEnd: Later.AddDays(7));
        var ended = Make(PromotionKind.LongStay, 10m, minNights: 2, validTo: TestData.Now.AddHours(1));

        foreach (var promo in new[] { otherHost, otherListing, flash })
            promo.AppliesTo(p, TestData.Stay(Soon, 3), TestData.Now).ShouldBeFalse();
        flash.AppliesTo(p, TestData.Stay(Later.AddDays(2), 3), TestData.Now).ShouldBeTrue();
        ended.AppliesTo(p, TestData.Stay(Soon, 3), TestData.Now.AddDays(1)).ShouldBeFalse();
    }

    [Fact]
    public void Coupons_apply_after_the_promotion()
    {
        var p = TestData.PublishedProperty(price: 100m, cleaning: 0m);
        var promo = Make(PromotionKind.LongStay, 10m, minNights: 2);
        var coupon = Coupon.Create("TEN", 10m, null, null, 0m, TestData.Now.AddDays(-1), TestData.Now.AddDays(1), 10);

        var b = PriceCalculator.Calculate(p, TestData.Stay(Soon, 2), coupon, PricingSettings.Default, 0m, TestData.Now, [promo]).Value;

        b.PromotionDiscount.ShouldBe(20m);
        b.CouponDiscount.ShouldBe(18m);                     // 10% of 180
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public void Discount_must_be_between_1_and_50_percent(decimal percent)
    {
        Promotion.Create("Deal", PromotionKind.LongStay, percent, null, null, 3, null, null, null, TestData.Now, TestData.Now.AddDays(1), Guid.NewGuid(), TestData.Now)
            .IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Each_kind_requires_its_rule()
    {
        Promotion.Create("Deal", PromotionKind.EarlyBird, 10m, null, null, null, null, null, null, TestData.Now, TestData.Now.AddDays(1), Guid.NewGuid(), TestData.Now)
            .Error!.Code.ShouldBe("promotion.rule");
        Promotion.Create("Deal", PromotionKind.FlashSale, 10m, null, null, null, null, Soon, null, TestData.Now, TestData.Now.AddDays(1), Guid.NewGuid(), TestData.Now)
            .Error!.Code.ShouldBe("promotion.rule");
    }
}
