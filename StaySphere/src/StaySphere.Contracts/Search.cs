namespace StaySphere.Contracts.Search;

public enum SearchSort
{
    Recommended,
    PriceLowHigh,
    PriceHighLow,
    Rating,
    MostReviewed,
    Distance,
    Newest,
}

public sealed record SearchPropertiesQuery
{
    public string? Location { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public double? RadiusKm { get; init; }
    /// <summary>Map bounds: south,west,north,east.</summary>
    public double? South { get; init; }
    public double? West { get; init; }
    public double? North { get; init; }
    public double? East { get; init; }
    public DateOnly? CheckIn { get; init; }
    public DateOnly? CheckOut { get; init; }
    public int? Guests { get; init; }
    public IReadOnlyList<string>? PropertyTypes { get; init; }
    public string? RoomType { get; init; }
    public decimal? MinPrice { get; init; }
    public decimal? MaxPrice { get; init; }
    public int? Bedrooms { get; init; }
    public int? Beds { get; init; }
    public decimal? Bathrooms { get; init; }
    public IReadOnlyList<string>? Amenities { get; init; }
    public double? MinRating { get; init; }
    public bool? InstantBook { get; init; }
    public bool? PetsAllowed { get; init; }
    public SearchSort Sort { get; init; } = SearchSort.Recommended;
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public string? Cursor { get; init; }
}

public sealed record DestinationDto(string City, string Country, int ListingCount, double Latitude, double Longitude, string? ImageUrl);

public sealed record GeocodeResultDto(string DisplayName, double Latitude, double Longitude, string? City, string? Country, string? CountryCode);
