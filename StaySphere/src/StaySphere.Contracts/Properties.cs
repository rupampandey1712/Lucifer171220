namespace StaySphere.Contracts.Properties;

public sealed record AddressDto(string Line1, string? Line2, string City, string? Region, string? PostalCode, string CountryCode, string Country);

public sealed record ImageDto(Guid Id, string Url, string ThumbnailUrl, int Width, int Height, int SortOrder);

public sealed record AmenityDto(string Code, string Name, string Category, string Icon);

public sealed record HostSummaryDto(Guid Id, string DisplayName, string? AvatarUrl, DateTimeOffset JoinedAt, int ListingCount, double Rating);

public sealed record PropertyDetailDto(
    Guid Id, Guid HostId, string Title, string Description, string PropertyType, string RoomType,
    int MaxGuests, int Bedrooms, int Beds, decimal Bathrooms,
    decimal BasePrice, decimal CleaningFee, string Currency, decimal WeekendAdjustmentPercent, decimal WeeklyDiscountPercent, decimal MonthlyDiscountPercent, int MinNights,
    AddressDto? Address, double? Latitude, double? Longitude, string Status, bool InstantBook,
    bool PetsAllowed, bool SmokingAllowed, bool EventsAllowed, string? HouseRules, string CheckInTime, string CheckOutTime,
    string CancellationPolicy, double RatingAverage, int ReviewCount,
    IReadOnlyList<ImageDto> Images, IReadOnlyList<AmenityDto> Amenities, HostSummaryDto Host, DateTimeOffset UpdatedAt);

public sealed record PropertyCardDto(
    Guid Id, string Title, string PropertyType, string RoomType, string City, string Country, double? Latitude, double? Longitude,
    decimal NightlyPrice, string Currency, decimal? TotalPrice, double RatingAverage, int ReviewCount, int MaxGuests, int Bedrooms, int Beds,
    bool InstantBook, IReadOnlyList<string> ImageUrls, double? DistanceKm);

public sealed record CreatePropertyRequest(string PropertyType, string RoomType, string? Title);

public sealed record UpdatePropertyRequest(
    string? Title, string? Description, string? PropertyType, string? RoomType,
    int? MaxGuests, int? Bedrooms, int? Beds, decimal? Bathrooms,
    AddressDto? Address, double? Latitude, double? Longitude,
    IReadOnlyList<string>? Amenities,
    PricingRequest? Pricing,
    RulesRequest? Rules);

public sealed record PricingRequest(decimal BasePrice, decimal CleaningFee, string Currency, decimal WeekendAdjustmentPercent,
    decimal WeeklyDiscountPercent, decimal MonthlyDiscountPercent, int MinNights);

public sealed record RulesRequest(bool PetsAllowed, bool SmokingAllowed, bool EventsAllowed, string? HouseRules,
    string CheckInTime, string CheckOutTime, string CancellationPolicy, bool InstantBook);

public sealed record HostPropertyListItemDto(Guid Id, string Title, string Status, string City, decimal BasePrice, string Currency,
    string? CoverImageUrl, double RatingAverage, int ReviewCount, int UpcomingReservations, DateTimeOffset UpdatedAt, int CompletionPercent);

public sealed record ReorderImagesRequest(IReadOnlyList<Guid> ImageIds);
