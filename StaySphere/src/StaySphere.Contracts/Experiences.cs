namespace StaySphere.Contracts.Experiences;

public sealed record ExperienceSlotDto(Guid Id, DateTimeOffset StartsAt, int Capacity, int Remaining);

public sealed record ExperienceCardDto(Guid Id, string Title, string Category, string City, string CountryCode, int DurationMinutes,
    decimal PricePerGuest, string Currency, string? ImageUrl, string HostName, DateTimeOffset? NextSlotAt);

/// <param name="Status">Draft, Published or Unlisted.</param>
public sealed record ExperienceDetailDto(Guid Id, Guid HostId, string HostName, string Title, string Description, string Category, string City,
    string CountryCode, string MeetingPoint, int DurationMinutes, int MaxGuests, decimal PricePerGuest, string Currency, string? ImageUrl,
    string Status, IReadOnlyList<ExperienceSlotDto> Slots);

/// <param name="Category">Food, Culture, Outdoors, Wellness, Art or Nightlife.</param>
public sealed record CreateExperienceRequest(string Title, string Description, string Category, string City, string CountryCode, string MeetingPoint,
    int DurationMinutes, int MaxGuests, decimal PricePerGuest, string Currency, string? ImageUrl);

/// <param name="Capacity">Seats in this session; defaults to the experience's group size.</param>
public sealed record AddExperienceSlotRequest(DateTimeOffset StartsAt, int? Capacity);

public sealed record BookExperienceRequest(Guid SlotId, int Guests);

/// <param name="Total">Price per guest × guests, computed by the server. Paid to the host at the experience.</param>
public sealed record ExperienceBookingDto(Guid Id, Guid ExperienceId, string ExperienceTitle, string City, string MeetingPoint, DateTimeOffset StartsAt,
    int DurationMinutes, int Guests, decimal Total, string Currency, string Status, bool CanCancel);
