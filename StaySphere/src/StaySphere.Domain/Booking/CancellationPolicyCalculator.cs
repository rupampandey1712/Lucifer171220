using StaySphere.Domain.Catalog;

namespace StaySphere.Domain.Booking;

public sealed record RefundQuote(decimal RefundAmount, decimal NonRefundable, string Explanation);

/// <summary>
/// Server-side cancellation rules. Check-in is treated as 15:00 property-local time approximated as UTC.
/// Flexible: full refund ≥ 24h before check-in, otherwise accommodation minus first night, no service fee.
/// Moderate: full refund ≥ 5 days before, otherwise 50% of accommodation.
/// Strict:   full refund ≥ 14 days before, 50% ≥ 7 days before, otherwise nothing.
/// Within 48h of booking (and ≥ 24h before check-in), every policy gives a full refund (grace period).
/// </summary>
public static class CancellationPolicyCalculator
{
    public static RefundQuote Calculate(Reservation reservation, DateTimeOffset now)
    {
        var total = reservation.TotalAmount;
        if (reservation.Status is not ReservationStatus.Confirmed)
            return new RefundQuote(0, 0, "Nothing has been charged for this reservation.");

        var checkIn = new DateTimeOffset(reservation.CheckIn.ToDateTime(new TimeOnly(15, 0)), TimeSpan.Zero);
        var untilCheckIn = checkIn - now;
        var accommodation = reservation.BaseAmount - reservation.Discount;

        if (untilCheckIn <= TimeSpan.Zero)
            return new RefundQuote(0, total, "The stay has already started, so it is not refundable.");

        if (reservation.ConfirmedAt is { } confirmed && now - confirmed <= TimeSpan.FromHours(48) && untilCheckIn >= TimeSpan.FromHours(24))
            return Full(total, "Cancelled within 48 hours of booking.");

        decimal refund;
        string why;
        switch (reservation.CancellationPolicy)
        {
            case CancellationPolicy.Flexible when untilCheckIn >= TimeSpan.FromHours(24):
                return Full(total, "Flexible policy: cancelled at least 24 hours before check-in.");
            case CancellationPolicy.Flexible:
                var firstNight = reservation.Nights == 0 ? 0 : accommodation / reservation.Nights;
                refund = accommodation - firstNight + reservation.Taxes * ((accommodation - firstNight) / Math.Max(accommodation, 1));
                why = "Flexible policy: the first night and service fee are non-refundable.";
                break;
            case CancellationPolicy.Moderate when untilCheckIn >= TimeSpan.FromDays(5):
                return Full(total, "Moderate policy: cancelled at least 5 days before check-in.");
            case CancellationPolicy.Moderate:
                refund = accommodation * 0.5m;
                why = "Moderate policy: 50% of the accommodation is refunded.";
                break;
            case CancellationPolicy.Strict when untilCheckIn >= TimeSpan.FromDays(14):
                return Full(total, "Strict policy: cancelled at least 14 days before check-in.");
            case CancellationPolicy.Strict when untilCheckIn >= TimeSpan.FromDays(7):
                refund = accommodation * 0.5m;
                why = "Strict policy: 50% of the accommodation is refunded.";
                break;
            default:
                refund = 0;
                why = "Strict policy: cancellations within 7 days of check-in are not refundable.";
                break;
        }

        refund = decimal.Round(Math.Clamp(refund, 0, total), 2);
        return new RefundQuote(refund, total - refund, why);
    }

    private static RefundQuote Full(decimal total, string why) => new(total, 0, why);
}
