namespace StaySphere.Contracts.Booking;

public sealed record QuoteRequest(DateOnly CheckIn, DateOnly CheckOut, int Guests, string? CouponCode);

public sealed record NightlyRateDto(DateOnly Night, decimal Amount, string Reason);

public sealed record QuoteDto(Guid PropertyId, DateOnly CheckIn, DateOnly CheckOut, int Guests, int Nights, string Currency,
    IReadOnlyList<NightlyRateDto> NightlyRates, decimal AverageNightly, decimal Accommodation, decimal Discount, decimal CleaningFee,
    decimal ServiceFee, decimal Taxes, decimal Total, string QuoteToken, DateTimeOffset QuoteExpiresAt, bool Available);

public sealed record AvailabilityDto(Guid PropertyId, DateOnly From, DateOnly To, IReadOnlyList<DateOnly> UnavailableDates, int MinNights);

public sealed record BlockDatesRequest(IReadOnlyList<DateOnly> Block, IReadOnlyList<DateOnly> Unblock);

public sealed record SeasonalPriceRequest(DateOnly Start, DateOnly End, decimal NightlyPrice);
public sealed record SeasonalPriceDto(Guid Id, DateOnly Start, DateOnly End, decimal NightlyPrice);

public sealed record CreateReservationRequest(Guid PropertyId, DateOnly CheckIn, DateOnly CheckOut, int Guests, string QuoteToken, string? CouponCode);

public sealed record ReservationDto(
    Guid Id, Guid PropertyId, string PropertyTitle, string? PropertyImageUrl, string City, string Country,
    Guid GuestId, string GuestName, Guid HostId, string HostName,
    DateOnly CheckIn, DateOnly CheckOut, int Guests, int Nights,
    decimal BaseAmount, decimal CleaningFee, decimal ServiceFee, decimal Taxes, decimal Discount, decimal TotalAmount, string Currency,
    string Status, string CancellationPolicy, DateTimeOffset? HoldExpiresAt, DateTimeOffset CreatedAt, decimal RefundAmount,
    bool CanCancel, bool CanReview, bool HasReview);

public sealed record CancelReservationRequest(string? Reason);

public sealed record CancellationPreviewDto(Guid ReservationId, decimal RefundAmount, decimal NonRefundable, string Currency, string Explanation);
