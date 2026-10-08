using StaySphere.Domain.Booking;
using StaySphere.Domain.Catalog;
using StaySphere.Domain.Payments;

namespace StaySphere.UnitTests.Domain;

public sealed class CancellationAndPaymentTests
{
    private static Reservation Confirmed(CancellationPolicy policy, int daysAhead, DateTimeOffset confirmedAt)
    {
        var p = TestData.PublishedProperty(policy: policy, price: 100m, cleaning: 0m);
        var checkIn = DateOnly.FromDateTime(TestData.Now.UtcDateTime).AddDays(daysAhead);
        var stay = TestData.Stay(checkIn, 4);
        var r = Reservation.Hold(p, Guid.NewGuid(), stay, 2, TestData.Price(p, stay), null, confirmedAt).Value;
        r.Confirm(confirmedAt);
        return r;
    }

    private static readonly DateTimeOffset LongAgo = TestData.Now.AddDays(-20);

    [Theory]
    [InlineData(CancellationPolicy.Flexible, 2, true)]
    [InlineData(CancellationPolicy.Moderate, 6, true)]
    [InlineData(CancellationPolicy.Moderate, 3, false)]
    [InlineData(CancellationPolicy.Strict, 15, true)]
    [InlineData(CancellationPolicy.Strict, 8, false)]
    [InlineData(CancellationPolicy.Strict, 3, false)]
    public void Full_refund_windows_follow_policy(CancellationPolicy policy, int daysAhead, bool full)
    {
        var r = Confirmed(policy, daysAhead, LongAgo);

        var quote = CancellationPolicyCalculator.Calculate(r, TestData.Now);

        (quote.RefundAmount == r.TotalAmount).ShouldBe(full);
    }

    [Fact]
    public void Moderate_late_cancellation_refunds_half_the_accommodation()
    {
        var r = Confirmed(CancellationPolicy.Moderate, 3, LongAgo);

        CancellationPolicyCalculator.Calculate(r, TestData.Now).RefundAmount.ShouldBe(200m);
    }

    [Fact]
    public void Strict_inside_seven_days_refunds_nothing()
    {
        var r = Confirmed(CancellationPolicy.Strict, 3, LongAgo);

        CancellationPolicyCalculator.Calculate(r, TestData.Now).RefundAmount.ShouldBe(0m);
    }

    [Fact]
    public void Grace_period_gives_full_refund_within_48h_of_booking()
    {
        var r = Confirmed(CancellationPolicy.Strict, 3, TestData.Now.AddHours(-2));

        CancellationPolicyCalculator.Calculate(r, TestData.Now).RefundAmount.ShouldBe(r.TotalAmount);
    }

    [Fact]
    public void Payment_success_is_idempotent()
    {
        var payment = Payment.Start(Guid.NewGuid(), Guid.NewGuid(), 100m, "EUR", "fake", "key", null, TestData.Now);

        payment.MarkSucceeded("pi_1", TestData.Now).ShouldBeTrue();
        payment.MarkSucceeded("pi_1", TestData.Now).ShouldBeFalse();

        payment.Transactions.Count.ShouldBe(1);
        payment.DomainEvents.Count.ShouldBe(1);
    }

    [Fact]
    public void Refunds_cannot_exceed_captured_amount()
    {
        var payment = Payment.Start(Guid.NewGuid(), Guid.NewGuid(), 100m, "EUR", "fake", "key", null, TestData.Now);
        payment.MarkSucceeded("pi_1", TestData.Now);

        var first = payment.RequestRefund(60m, "partial", TestData.Now);
        payment.CompleteRefund(first.Value.Id, "re_1", TestData.Now);

        payment.RequestRefund(50m, "too much", TestData.Now).IsFailure.ShouldBeTrue();
        payment.Status.ShouldBe(PaymentStatus.PartiallyRefunded);
        payment.RefundedAmount.ShouldBe(60m);
    }
}
