using StaySphere.Domain.Booking;
using StaySphere.Domain.Catalog;
using StaySphere.Domain.Reviews;

namespace StaySphere.UnitTests.Domain;

public sealed class ReservationTests
{
    private static readonly DateOnly CheckIn = DateOnly.FromDateTime(TestData.Now.UtcDateTime).AddDays(30);

    private static Reservation NewHold(Property? property = null, Guid? guest = null, int guests = 2)
    {
        var p = property ?? TestData.PublishedProperty();
        var stay = TestData.Stay(CheckIn, 3);
        return Reservation.Hold(p, guest ?? Guid.NewGuid(), stay, guests, TestData.Price(p, stay), null, TestData.Now).Value;
    }

    [Fact]
    public void Hold_creates_one_active_night_row_per_night_and_expires_in_ten_minutes()
    {
        var r = NewHold();

        r.Status.ShouldBe(ReservationStatus.Held);
        r.NightRows.Count.ShouldBe(3);
        r.NightRows.ShouldAllBe(n => n.IsActive);
        r.HoldExpiresAt.ShouldBe(TestData.Now.AddMinutes(10));
        r.DomainEvents.ShouldContain(e => e is ReservationHeldDomainEvent);
    }

    [Fact]
    public void Cannot_book_unpublished_property()
    {
        var p = TestData.PublishedProperty();
        p.Unpublish();
        var stay = TestData.Stay(CheckIn, 2);

        var result = Reservation.Hold(p, Guid.NewGuid(), stay, 2, TestData.Price(p, stay), null, TestData.Now);

        result.Error!.Code.ShouldBe("reservation.property_unavailable");
    }

    [Fact]
    public void Cannot_exceed_max_guests_or_book_own_listing()
    {
        var host = Guid.NewGuid();
        var p = TestData.PublishedProperty(hostId: host, maxGuests: 2);
        var stay = TestData.Stay(CheckIn, 2);
        var price = TestData.Price(p, stay);

        Reservation.Hold(p, Guid.NewGuid(), stay, 3, price, null, TestData.Now).Error!.Code.ShouldBe("reservation.guests");
        Reservation.Hold(p, host, stay, 1, price, null, TestData.Now).Error!.Code.ShouldBe("reservation.own_property");
    }

    [Fact]
    public void Cannot_book_blocked_dates_or_past_dates()
    {
        var p = TestData.PublishedProperty();
        p.BlockDates([CheckIn.AddDays(1)]);
        var stay = TestData.Stay(CheckIn, 3);
        var past = TestData.Stay(DateOnly.FromDateTime(TestData.Now.UtcDateTime).AddDays(-2), 1);

        Reservation.Hold(p, Guid.NewGuid(), stay, 1, TestData.Price(p, stay), null, TestData.Now).Error!.Code.ShouldBe("reservation.dates_blocked");
        Reservation.Hold(p, Guid.NewGuid(), past, 1, TestData.Price(p, past), null, TestData.Now).Error!.Code.ShouldBe("reservation.past");
    }

    [Fact]
    public void Happy_path_hold_pay_confirm_complete()
    {
        var r = NewHold();

        r.MarkPaymentPending(TestData.Now.AddMinutes(1)).IsSuccess.ShouldBeTrue();
        r.Confirm(TestData.Now.AddMinutes(2)).IsSuccess.ShouldBeTrue();
        r.Status.ShouldBe(ReservationStatus.Confirmed);
        r.HoldExpiresAt.ShouldBeNull();

        var afterCheckout = new DateTimeOffset(r.CheckOut.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        r.Complete(afterCheckout).IsSuccess.ShouldBeTrue();
        r.History.Select(h => h.To).ShouldBe([ReservationStatus.Held, ReservationStatus.PaymentPending, ReservationStatus.Confirmed, ReservationStatus.Completed]);
    }

    [Fact]
    public void Expired_hold_releases_nights_and_cannot_be_paid()
    {
        var r = NewHold();

        r.MarkPaymentPending(TestData.Now.AddMinutes(11)).Error!.Code.ShouldBe("reservation.hold_expired");
        r.Expire(TestData.Now.AddMinutes(11)).IsSuccess.ShouldBeTrue();

        r.Status.ShouldBe(ReservationStatus.Expired);
        r.NightRows.ShouldAllBe(n => !n.IsActive);
        r.Confirm(TestData.Now.AddMinutes(12)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Failed_payment_releases_nights()
    {
        var r = NewHold();
        r.MarkPaymentPending(TestData.Now);

        r.Fail("declined", TestData.Now).IsSuccess.ShouldBeTrue();

        r.NightRows.ShouldAllBe(n => !n.IsActive);
        r.DomainEvents.ShouldContain(e => e is ReservationFailedDomainEvent);
    }

    [Fact]
    public void Cancel_confirmed_with_refund_goes_to_refund_pending_then_refunded()
    {
        var r = NewHold();
        r.Confirm(TestData.Now);

        r.Cancel(r.GuestId, r.TotalAmount, "plans changed", TestData.Now.AddDays(1)).IsSuccess.ShouldBeTrue();
        r.Status.ShouldBe(ReservationStatus.RefundPending);
        r.NightRows.ShouldAllBe(n => !n.IsActive);

        r.MarkRefunded(TestData.Now.AddDays(1)).IsSuccess.ShouldBeTrue();
        r.Status.ShouldBe(ReservationStatus.Refunded);
    }

    [Fact]
    public void Cannot_complete_before_checkout()
    {
        var r = NewHold();
        r.Confirm(TestData.Now);

        r.Complete(TestData.Now).Error!.Code.ShouldBe("reservation.not_finished");
    }

    [Fact]
    public void Review_requires_completed_stay_by_the_same_guest_after_checkout()
    {
        var guest = Guid.NewGuid();
        var r = NewHold(guest: guest);
        r.Confirm(TestData.Now);
        var ratings = new ReviewRatings(5, 5, 5, 5, 5, 5, 5);

        Review.Submit(r, guest, ratings, "Wonderful stay, would come back!", TestData.Now).Error!.Code.ShouldBe("review.not_completed");

        var afterCheckout = new DateTimeOffset(r.CheckOut.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        r.Complete(afterCheckout);
        Review.Submit(r, Guid.NewGuid(), ratings, "Wonderful stay, would come back!", afterCheckout).Error!.Code.ShouldBe("review.not_your_stay");
        Review.Submit(r, guest, ratings, "Wonderful stay, would come back!", afterCheckout.AddDays(31)).Error!.Code.ShouldBe("review.window_closed");
        Review.Submit(r, guest, ratings, "Wonderful stay, would come back!", afterCheckout.AddDays(1)).IsSuccess.ShouldBeTrue();
    }
}
