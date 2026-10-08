using StaySphere.Domain.ValueObjects;

namespace StaySphere.UnitTests.Domain;

public sealed class ValueObjectTests
{
    [Fact]
    public void Money_adds_same_currency_and_rejects_mixed_currencies()
    {
        var a = new Money(10.10m, "usd");
        var b = new Money(0.20m, "USD");

        (a + b).ShouldBe(new Money(10.30m, "USD"));
        Should.Throw<InvalidOperationException>(() => a + new Money(1, "EUR"));
        Should.Throw<ArgumentException>(() => new Money(1, "US"));
    }

    [Fact]
    public void Money_rounds_half_to_even()
    {
        new Money(2.345m, "EUR").Round().Amount.ShouldBe(2.34m);
        new Money(2.355m, "EUR").Round().Amount.ShouldBe(2.36m);
    }

    [Fact]
    public void DateRange_counts_nights_and_enumerates_each_night()
    {
        var range = DateRange.Create(new DateOnly(2030, 1, 1), new DateOnly(2030, 1, 4)).Value;

        range.Nights.ShouldBe(3);
        range.EachNight().ShouldBe([new DateOnly(2030, 1, 1), new DateOnly(2030, 1, 2), new DateOnly(2030, 1, 3)]);
        range.Contains(new DateOnly(2030, 1, 4)).ShouldBeFalse("check-out day is not a night");
    }

    [Theory]
    [InlineData("2030-01-05", "2030-01-05")]
    [InlineData("2030-01-05", "2030-01-04")]
    public void DateRange_rejects_checkout_on_or_before_checkin(string checkIn, string checkOut)
    {
        var result = DateRange.Create(DateOnly.Parse(checkIn), DateOnly.Parse(checkOut));

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("dates.invalid");
    }

    [Fact]
    public void DateRange_rejects_stays_longer_than_limit()
    {
        var start = new DateOnly(2030, 1, 1);
        DateRange.Create(start, start.AddDays(DateRange.MaxNights + 1)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void DateRange_overlap_is_half_open()
    {
        var a = DateRange.Create(new DateOnly(2030, 1, 1), new DateOnly(2030, 1, 5)).Value;
        var adjacent = DateRange.Create(new DateOnly(2030, 1, 5), new DateOnly(2030, 1, 7)).Value;
        var overlapping = DateRange.Create(new DateOnly(2030, 1, 4), new DateOnly(2030, 1, 7)).Value;

        a.Overlaps(adjacent).ShouldBeFalse("back-to-back stays share only the changeover day");
        a.Overlaps(overlapping).ShouldBeTrue();
    }

    [Theory]
    [InlineData(91, 0)]
    [InlineData(0, 181)]
    public void Coordinates_validate_ranges(double lat, double lng) => Coordinates.Create(lat, lng).IsFailure.ShouldBeTrue();

    [Fact]
    public void Coordinates_haversine_distance_is_reasonable()
    {
        var london = Coordinates.Create(51.5074, -0.1278).Value;
        var paris = Coordinates.Create(48.8566, 2.3522).Value;

        london.DistanceKmTo(paris).ShouldBeInRange(340, 350);
    }

    [Theory]
    [InlineData("Guest@Example.Local", true)]
    [InlineData("not-an-email", false)]
    [InlineData("", false)]
    public void EmailAddress_validates_and_normalizes(string input, bool valid)
    {
        var result = EmailAddress.Create(input);
        result.IsSuccess.ShouldBe(valid);
        if (valid) result.Value.Value.ShouldBe("guest@example.local");
    }
}
