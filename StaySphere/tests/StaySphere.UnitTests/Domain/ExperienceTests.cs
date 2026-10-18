using StaySphere.Domain.Experiences;

namespace StaySphere.UnitTests.Domain;

public sealed class ExperienceTests
{
    private static readonly DateTimeOffset Now = new(2030, 5, 1, 9, 0, 0, TimeSpan.Zero);

    private static Experience Tour(int duration = 120, int maxGuests = 8) =>
        Experience.Create(Guid.NewGuid(), "Alfama food walk", "Six tastings in old Lisbon with a local cook.", ExperienceCategory.Food, "Lisbon", "pt",
            "Largo do Chafariz de Dentro", duration, maxGuests, 45.5m, "eur", null, Now).Value;

    [Fact]
    public void Totals_are_price_per_guest_times_guests()
    {
        var tour = Tour();
        tour.TotalFor(3).ShouldBe(136.5m);
        tour.Currency.ShouldBe("EUR");
        tour.CountryCode.ShouldBe("PT");
    }

    [Theory]
    [InlineData(10, 8, 40)]
    [InlineData(120, 0, 40)]
    [InlineData(120, 31, 40)]
    [InlineData(120, 8, 0)]
    public void Invalid_experiences_are_rejected(int duration, int maxGuests, int price) =>
        Experience.Create(Guid.NewGuid(), "Title", "Description long enough to pass.", ExperienceCategory.Art, "Porto", "PT", "Here", duration,
            maxGuests, price, "EUR", null, Now).IsFailure.ShouldBeTrue();

    [Fact]
    public void Publishing_needs_an_upcoming_session()
    {
        var tour = Tour();
        tour.Publish(Now).IsFailure.ShouldBeTrue();
        tour.AddSlot(Now.AddDays(2), null, Now).IsSuccess.ShouldBeTrue();
        tour.Publish(Now).IsSuccess.ShouldBeTrue();
        tour.Status.ShouldBe(ExperienceStatus.Published);
    }

    [Fact]
    public void Sessions_cannot_overlap_start_too_soon_or_exceed_the_group_size()
    {
        var tour = Tour(duration: 120, maxGuests: 8);
        var first = tour.AddSlot(Now.AddDays(1), null, Now);
        first.Value.Capacity.ShouldBe(8);

        tour.AddSlot(Now.AddDays(1).AddMinutes(90), null, Now).Error!.Code.ShouldBe("experience.slot_overlap");
        tour.AddSlot(Now.AddDays(1).AddMinutes(120), 4, Now).IsSuccess.ShouldBeTrue("back-to-back is fine");
        tour.AddSlot(Now.AddHours(1), null, Now).Error!.Code.ShouldBe("experience.slot_past");
        tour.AddSlot(Now.AddDays(3), 9, Now).Error!.Code.ShouldBe("experience.slot_capacity");
    }

    [Fact]
    public void Bookings_can_be_cancelled_free_until_24_hours_before()
    {
        var tour = Tour();
        var slot = tour.AddSlot(Now.AddDays(2), null, Now).Value;
        var booking = ExperienceBooking.Create(tour, slot, Guid.NewGuid(), 2, Now);
        booking.Total.ShouldBe(91m);

        booking.Cancel(slot.StartsAt.AddHours(-23)).Error!.Code.ShouldBe("experience_booking.too_late");
        booking.Cancel(slot.StartsAt.AddHours(-25)).IsSuccess.ShouldBeTrue();
        booking.Cancel(slot.StartsAt.AddHours(-25)).Error!.Code.ShouldBe("experience_booking.cancelled");
    }
}
