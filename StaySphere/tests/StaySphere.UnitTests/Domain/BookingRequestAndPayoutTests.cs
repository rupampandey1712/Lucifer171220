using StaySphere.Application.Payments;
using StaySphere.Domain.Booking;
using StaySphere.Domain.Catalog;
using StaySphere.Domain.Payments;

namespace StaySphere.UnitTests.Domain;

public sealed class BookingRequestAndPayoutTests
{
    private static readonly DateOnly CheckIn = DateOnly.FromDateTime(TestData.Now.UtcDateTime).AddDays(30);

    private static Reservation RequestToBook(out Property property)
    {
        property = TestData.PublishedProperty();
        property.UpdateRules(false, false, false, null, new TimeOnly(15, 0), new TimeOnly(11, 0), CancellationPolicy.Moderate, instantBook: false);
        var stay = TestData.Stay(CheckIn, 3);
        var r = Reservation.Hold(property, Guid.NewGuid(), stay, 2, TestData.Price(property, stay), null, TestData.Now).Value;
        r.MarkPaymentPending(TestData.Now);
        return r;
    }

    [Fact]
    public void Request_to_book_cannot_be_confirmed_without_host_approval()
    {
        var r = RequestToBook(out _);

        r.RequiresApproval.ShouldBeTrue();
        r.Confirm(TestData.Now).Error!.Code.ShouldBe("reservation.needs_approval");
        r.AwaitApproval(TestData.Now).IsSuccess.ShouldBeTrue();
        r.Status.ShouldBe(ReservationStatus.AwaitingApproval);
        r.ApprovalDeadline.ShouldBe(TestData.Now.Add(Reservation.ApprovalWindow));
        r.NightRows.ShouldAllBe(n => n.IsActive, "dates stay blocked while the host decides");
        r.Confirm(TestData.Now.AddHours(1)).IsSuccess.ShouldBeTrue();
        r.ApprovalDeadline.ShouldBeNull();
    }

    [Fact]
    public void Instant_book_reservations_cannot_enter_approval()
    {
        var p = TestData.PublishedProperty();
        var stay = TestData.Stay(CheckIn, 2);
        var r = Reservation.Hold(p, Guid.NewGuid(), stay, 2, TestData.Price(p, stay), null, TestData.Now).Value;

        r.AwaitApproval(TestData.Now).Error!.Code.ShouldBe("reservation.instant");
    }

    [Fact]
    public void Only_the_host_can_decline_and_declining_releases_the_dates()
    {
        var r = RequestToBook(out _);
        r.AwaitApproval(TestData.Now);

        r.Decline(Guid.NewGuid(), "nope", TestData.Now).Error!.Code.ShouldBe("reservation.not_host");
        r.Decline(r.HostId, "Dates no longer available", TestData.Now).IsSuccess.ShouldBeTrue();

        r.Status.ShouldBe(ReservationStatus.Declined);
        r.NightRows.ShouldAllBe(n => !n.IsActive);
        r.DomainEvents.OfType<ReservationDeclinedDomainEvent>().Single().Expired.ShouldBeFalse();
    }

    [Fact]
    public void Timeout_declines_as_expired()
    {
        var r = RequestToBook(out _);
        r.AwaitApproval(TestData.Now);

        r.Decline(null, null, TestData.Now.AddHours(25)).IsSuccess.ShouldBeTrue();

        r.DeclineReason.ShouldBe("The host did not respond in time.");
        r.DomainEvents.OfType<ReservationDeclinedDomainEvent>().Single().Expired.ShouldBeTrue();
    }

    [Fact]
    public void Guest_can_withdraw_a_pending_request()
    {
        var r = RequestToBook(out _);
        r.AwaitApproval(TestData.Now);

        r.Cancel(r.GuestId, 0, "changed plans", TestData.Now).IsSuccess.ShouldBeTrue();
        r.Status.ShouldBe(ReservationStatus.Cancelled);
        r.NightRows.ShouldAllBe(n => !n.IsActive);
    }

    [Fact]
    public void Authorization_can_be_captured_or_voided_once()
    {
        var p = Payment.Start(Guid.NewGuid(), Guid.NewGuid(), 300m, "EUR", "fake", "k", null, TestData.Now);
        p.MarkAuthorized("pi_1", TestData.Now).ShouldBeTrue();
        p.MarkAuthorized("pi_1", TestData.Now).ShouldBeFalse();

        var voided = Payment.Start(Guid.NewGuid(), Guid.NewGuid(), 300m, "EUR", "fake", "k2", null, TestData.Now);
        voided.MarkAuthorized("pi_2", TestData.Now);
        voided.MarkVoided(TestData.Now).ShouldBeTrue();
        voided.MarkSucceeded("pi_2", TestData.Now).ShouldBeFalse("a voided authorization can never be captured");

        p.MarkSucceeded("pi_1", TestData.Now).ShouldBeTrue();
        p.MarkVoided(TestData.Now).ShouldBeFalse("a captured payment is refunded, not voided");
        p.Transactions.Select(t => t.Kind).ShouldBe([PaymentTransactionKind.Authorization, PaymentTransactionKind.Charge]);
    }

    [Fact]
    public void Confirmation_ledger_balances_and_refund_reverses_proportionally()
    {
        var p = TestData.PublishedProperty(price: 100m, cleaning: 40m);
        var stay = TestData.Stay(CheckIn, 3);
        var r = Reservation.Hold(p, Guid.NewGuid(), stay, 2, TestData.Price(p, stay), null, TestData.Now).Value;
        r.Confirm(TestData.Now);

        var entries = Ledger.ForConfirmation(r, TestData.Now).ToList();
        entries.Single(e => e.Account == LedgerAccount.GuestPayment).Amount
            .ShouldBe(entries.Where(e => e.Account != LedgerAccount.GuestPayment).Sum(e => e.Amount));

        var refund = Ledger.ForRefund(r, r.TotalAmount / 2, TestData.Now).ToList();
        refund.Single(e => e.Account == LedgerAccount.HostEarning).Amount
            .ShouldBe(-decimal.Round(Ledger.HostEarning(r) / 2, 2), tolerance: 0.01m);
    }

    [Fact]
    public void Payout_requires_positive_items_and_a_minimum_amount()
    {
        HostPayout.Create(Guid.NewGuid(), "EUR", "PT50 •••• 0154", [], false, TestData.Now).Error!.Code.ShouldBe("payout.nothing_available");
        HostPayout.Create(Guid.NewGuid(), "EUR", "PT50 •••• 0154", [(Guid.NewGuid(), 0.5m)], false, TestData.Now).IsFailure.ShouldBeTrue();

        var payout = HostPayout.Create(Guid.NewGuid(), "EUR", "PT50 •••• 0154", [(Guid.NewGuid(), 120m), (Guid.NewGuid(), 80.55m)], true, TestData.Now).Value;
        payout.Amount.ShouldBe(200.55m);
        payout.Status.ShouldBe(PayoutStatus.Processing);
        payout.MarkPaid("po_1", TestData.Now);
        payout.MarkFailed("too late");
        payout.Status.ShouldBe(PayoutStatus.Paid, "terminal states don't change");
    }

    [Theory]
    [InlineData("PT50000201231234567890154", true, "PT50 •••• 0154")]
    [InlineData("GB82 WEST 1234 5698 7654 32", true, "GB82 •••• 5432")]
    [InlineData("12345", false, null)]
    public void Payout_account_validates_and_stores_only_a_masked_reference(string iban, bool valid, string? masked)
    {
        var result = PayoutAccount.Create(Guid.NewGuid(), "Hana Host", iban, "PT", TestData.Now);
        result.IsSuccess.ShouldBe(valid);
        if (valid) result.Value.MaskedAccount.ShouldBe(masked);
    }
}
