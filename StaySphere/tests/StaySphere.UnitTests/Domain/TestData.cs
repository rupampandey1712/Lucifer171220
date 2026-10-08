using StaySphere.Domain.Catalog;
using StaySphere.Domain.Pricing;
using StaySphere.Domain.ValueObjects;

namespace StaySphere.UnitTests.Domain;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2030, 6, 1, 12, 0, 0, TimeSpan.Zero);

    public static Property PublishedProperty(Guid? hostId = null, decimal price = 100m, decimal cleaning = 50m, int maxGuests = 4,
        CancellationPolicy policy = CancellationPolicy.Moderate, decimal weekendPercent = 0, decimal weeklyDiscount = 0)
    {
        var p = Property.CreateDraft(hostId ?? Guid.NewGuid(), PropertyType.Apartment, RoomType.EntirePlace, Now.AddDays(-30));
        p.UpdateBasics("Bright apartment in the old town", "A lovely bright apartment close to everything you need.", PropertyType.Apartment, RoomType.EntirePlace);
        p.UpdateRooms(maxGuests, 2, 2, 1).IsSuccess.ShouldBeTrue();
        p.SetLocation(new Address("1 Main St", null, "Lisbon", null, "1000", "PT", "Portugal"), Coordinates.Create(38.7, -9.1).Value);
        p.UpdatePricing(price, cleaning, "EUR", weekendPercent, weeklyDiscount, 0, 1).IsSuccess.ShouldBeTrue();
        p.UpdateRules(false, false, false, null, new TimeOnly(15, 0), new TimeOnly(11, 0), policy, true);
        p.AddImage("https://img/1.jpg", "https://img/1_t.jpg", null, 1200, 800);
        p.Publish(Now.AddDays(-29)).IsSuccess.ShouldBeTrue();
        p.ClearDomainEvents();
        return p;
    }

    public static DateRange Stay(DateOnly checkIn, int nights) => DateRange.Create(checkIn, checkIn.AddDays(nights)).Value;

    public static PriceBreakdown Price(Property p, DateRange stay) =>
        PriceCalculator.Calculate(p, stay, null, PricingSettings.Default, 10m, Now).Value;
}
