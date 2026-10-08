using StaySphere.Domain.Common;
using StaySphere.Domain.ValueObjects;

namespace StaySphere.Domain.Catalog;

public enum PropertyType
{
    Apartment,
    House,
    Villa,
    Hotel,
    GuestHouse,
    Cabin,
    Cottage,
    FarmStay,
    Hostel,
    Resort,
    BedAndBreakfast,
    UniqueStay,
}

public enum RoomType
{
    EntirePlace,
    PrivateRoom,
    SharedRoom,
    HotelRoom,
}

public enum PropertyStatus
{
    Draft,
    Published,
    Unlisted,
    Suspended,
}

public enum CancellationPolicy
{
    Flexible,
    Moderate,
    Strict,
}

public sealed class Property : AggregateRoot
{
    private readonly List<PropertyImage> _images = [];
    private readonly List<PropertyAmenity> _amenities = [];
    private readonly List<SeasonalPrice> _seasonalPrices = [];
    private readonly List<BlockedDate> _blockedDates = [];

    private Property() { }

    public Guid HostId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public PropertyType PropertyType { get; private set; }
    public RoomType RoomType { get; private set; }
    public int MaxGuests { get; private set; } = 1;
    public int Bedrooms { get; private set; }
    public int Beds { get; private set; } = 1;
    public decimal Bathrooms { get; private set; } = 1;
    public decimal BasePrice { get; private set; }
    public decimal CleaningFee { get; private set; }
    public string Currency { get; private set; } = "USD";
    /// <summary>Percentage applied to Friday and Saturday nights (e.g. 15 = +15%).</summary>
    public decimal WeekendAdjustmentPercent { get; private set; }
    public decimal WeeklyDiscountPercent { get; private set; }
    public decimal MonthlyDiscountPercent { get; private set; }
    public Address? Address { get; private set; }
    public double? Latitude { get; private set; }
    public double? Longitude { get; private set; }
    public PropertyStatus Status { get; private set; } = PropertyStatus.Draft;
    public bool InstantBook { get; private set; } = true;
    public bool PetsAllowed { get; private set; }
    public bool SmokingAllowed { get; private set; }
    public bool EventsAllowed { get; private set; }
    public string? HouseRules { get; private set; }
    public TimeOnly CheckInTime { get; private set; } = new(15, 0);
    public TimeOnly CheckOutTime { get; private set; } = new(11, 0);
    public CancellationPolicy CancellationPolicy { get; private set; } = CancellationPolicy.Moderate;
    public int MinNights { get; private set; } = 1;
    public double RatingAverage { get; private set; }
    public int ReviewCount { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public IReadOnlyCollection<PropertyImage> Images => _images;
    public IReadOnlyCollection<PropertyAmenity> Amenities => _amenities;
    public IReadOnlyCollection<SeasonalPrice> SeasonalPrices => _seasonalPrices;
    public IReadOnlyCollection<BlockedDate> BlockedDates => _blockedDates;

    public bool IsBookable => Status == PropertyStatus.Published && DeletedAt is null;

    public static Property CreateDraft(Guid hostId, PropertyType type, RoomType roomType, DateTimeOffset now) =>
        new()
        {
            HostId = hostId,
            PropertyType = type,
            RoomType = roomType,
            CreatedAt = now,
            UpdatedAt = now,
        };

    public void UpdateBasics(string title, string description, PropertyType type, RoomType roomType)
    {
        Title = title.Trim();
        Description = description.Trim();
        PropertyType = type;
        RoomType = roomType;
    }

    public Result UpdateRooms(int maxGuests, int bedrooms, int beds, decimal bathrooms)
    {
        if (maxGuests is < 1 or > 50) return Error.Validation("property.guests", "Max guests must be between 1 and 50.");
        if (bedrooms < 0 || beds < 1 || bathrooms < 0)
            return Error.Validation("property.rooms", "Room counts are invalid.");
        MaxGuests = maxGuests;
        Bedrooms = bedrooms;
        Beds = beds;
        Bathrooms = bathrooms;
        return Result.Success();
    }

    public Result SetLocation(Address address, Coordinates coordinates)
    {
        Address = address;
        Latitude = coordinates.Latitude;
        Longitude = coordinates.Longitude;
        return Result.Success();
    }

    public Result UpdatePricing(decimal basePrice, decimal cleaningFee, string currency, decimal weekendAdjustmentPercent,
        decimal weeklyDiscountPercent, decimal monthlyDiscountPercent, int minNights)
    {
        if (basePrice <= 0) return Error.Validation("property.price", "Nightly price must be greater than zero.");
        if (cleaningFee < 0) return Error.Validation("property.cleaning_fee", "Cleaning fee cannot be negative.");
        if (weekendAdjustmentPercent is < -50 or > 200) return Error.Validation("property.weekend", "Weekend adjustment must be between -50% and 200%.");
        if (weeklyDiscountPercent is < 0 or > 80 || monthlyDiscountPercent is < 0 or > 80)
            return Error.Validation("property.discount", "Discounts must be between 0% and 80%.");
        if (minNights is < 1 or > 30) return Error.Validation("property.min_nights", "Minimum nights must be between 1 and 30.");
        BasePrice = basePrice;
        CleaningFee = cleaningFee;
        Currency = currency.ToUpperInvariant();
        WeekendAdjustmentPercent = weekendAdjustmentPercent;
        WeeklyDiscountPercent = weeklyDiscountPercent;
        MonthlyDiscountPercent = monthlyDiscountPercent;
        MinNights = minNights;
        if (Status == PropertyStatus.Published) Raise(new PropertyChangedDomainEvent(Id, "pricing"));
        return Result.Success();
    }

    public void UpdateRules(bool petsAllowed, bool smokingAllowed, bool eventsAllowed, string? houseRules,
        TimeOnly checkIn, TimeOnly checkOut, CancellationPolicy policy, bool instantBook)
    {
        PetsAllowed = petsAllowed;
        SmokingAllowed = smokingAllowed;
        EventsAllowed = eventsAllowed;
        HouseRules = houseRules?.Trim();
        CheckInTime = checkIn;
        CheckOutTime = checkOut;
        CancellationPolicy = policy;
        InstantBook = instantBook;
    }

    public void SetAmenities(IEnumerable<string> amenityCodes)
    {
        _amenities.Clear();
        foreach (var code in amenityCodes.Select(c => c.Trim().ToLowerInvariant()).Distinct())
            _amenities.Add(new PropertyAmenity(Id, code));
    }

    public bool HasAmenity(string code) => _amenities.Any(a => a.AmenityCode == code);

    public PropertyImage AddImage(string url, string thumbnailUrl, string? blobKey, int width, int height)
    {
        var image = new PropertyImage(Id, url, thumbnailUrl, blobKey, width, height,
            _images.Count == 0 ? 0 : _images.Max(i => i.SortOrder) + 1);
        _images.Add(image);
        return image;
    }

    public Result RemoveImage(Guid imageId)
    {
        var image = _images.FirstOrDefault(i => i.Id == imageId);
        if (image is null) return Error.NotFound("image.not_found", "Image not found.");
        if (Status == PropertyStatus.Published && _images.Count == 1)
            return Error.Conflict("image.last", "A published listing needs at least one photo.");
        _images.Remove(image);
        return Result.Success();
    }

    public void ReorderImages(IReadOnlyList<Guid> orderedIds)
    {
        for (var i = 0; i < orderedIds.Count; i++)
            _images.FirstOrDefault(x => x.Id == orderedIds[i])?.SetSortOrder(i);
    }

    public Result Publish(DateTimeOffset now)
    {
        if (DeletedAt is not null) return Error.Conflict("property.deleted", "Deleted listings cannot be published.");
        if (Status == PropertyStatus.Suspended) return Error.Forbidden("property.suspended", "This listing is suspended.");

        var missing = new List<string>();
        if (Title.Length < 5) missing.Add("title");
        if (Description.Length < 20) missing.Add("description");
        if (Address is null || Latitude is null) missing.Add("location");
        if (BasePrice <= 0) missing.Add("price");
        if (_images.Count == 0) missing.Add("photos");
        if (missing.Count > 0)
            return Error.Validation("property.incomplete", $"Listing is incomplete: {string.Join(", ", missing)}.");

        Status = PropertyStatus.Published;
        PublishedAt ??= now;
        Raise(new PropertyPublishedDomainEvent(Id, HostId));
        return Result.Success();
    }

    public void Unpublish()
    {
        if (Status != PropertyStatus.Published) return;
        Status = PropertyStatus.Unlisted;
        Raise(new PropertyChangedDomainEvent(Id, "unpublished"));
    }

    public void Suspend()
    {
        Status = PropertyStatus.Suspended;
        Raise(new PropertyChangedDomainEvent(Id, "suspended"));
    }

    public void SoftDelete(DateTimeOffset now)
    {
        DeletedAt = now;
        Status = PropertyStatus.Unlisted;
        Raise(new PropertyChangedDomainEvent(Id, "deleted"));
    }

    public void BlockDates(IEnumerable<DateOnly> dates)
    {
        foreach (var d in dates.Distinct().Where(d => _blockedDates.All(b => b.Date != d)))
            _blockedDates.Add(new BlockedDate(Id, d));
    }

    public void UnblockDates(IEnumerable<DateOnly> dates)
    {
        var set = dates.ToHashSet();
        _blockedDates.RemoveAll(b => set.Contains(b.Date));
    }

    public Result AddSeasonalPrice(DateOnly start, DateOnly end, decimal nightlyPrice)
    {
        if (end <= start) return Error.Validation("season.dates", "Season end must be after start.");
        if (nightlyPrice <= 0) return Error.Validation("season.price", "Seasonal price must be positive.");
        if (_seasonalPrices.Any(s => s.Start < end && start < s.End))
            return Error.Conflict("season.overlap", "Seasonal prices cannot overlap.");
        _seasonalPrices.Add(new SeasonalPrice(Id, start, end, nightlyPrice));
        return Result.Success();
    }

    public void RemoveSeasonalPrice(Guid id) => _seasonalPrices.RemoveAll(s => s.Id == id);

    public void ApplyReviewStats(double average, int count)
    {
        RatingAverage = Math.Round(average, 2);
        ReviewCount = count;
    }
}

public sealed class PropertyImage : Entity
{
    private PropertyImage() { }

    internal PropertyImage(Guid propertyId, string url, string thumbnailUrl, string? blobKey, int width, int height, int sortOrder)
    {
        PropertyId = propertyId;
        Url = url;
        ThumbnailUrl = thumbnailUrl;
        BlobKey = blobKey;
        Width = width;
        Height = height;
        SortOrder = sortOrder;
    }

    public Guid PropertyId { get; private set; }
    public string Url { get; private set; } = default!;
    public string ThumbnailUrl { get; private set; } = default!;
    public string? BlobKey { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int SortOrder { get; private set; }

    internal void SetSortOrder(int order) => SortOrder = order;
}

public sealed class PropertyAmenity
{
    private PropertyAmenity() { }

    internal PropertyAmenity(Guid propertyId, string amenityCode)
    {
        PropertyId = propertyId;
        AmenityCode = amenityCode;
    }

    public Guid PropertyId { get; private set; }
    public string AmenityCode { get; private set; } = default!;
}

public sealed class Amenity
{
    private Amenity() { }

    public Amenity(string code, string name, string category, string icon)
    {
        Code = code;
        Name = name;
        Category = category;
        Icon = icon;
    }

    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string Category { get; private set; } = default!;
    public string Icon { get; private set; } = default!;
}

public sealed class SeasonalPrice : Entity
{
    private SeasonalPrice() { }

    internal SeasonalPrice(Guid propertyId, DateOnly start, DateOnly end, decimal nightlyPrice)
    {
        PropertyId = propertyId;
        Start = start;
        End = end;
        NightlyPrice = nightlyPrice;
    }

    public Guid PropertyId { get; private set; }
    public DateOnly Start { get; private set; }
    /// <summary>Exclusive.</summary>
    public DateOnly End { get; private set; }
    public decimal NightlyPrice { get; private set; }
}

public sealed class BlockedDate
{
    private BlockedDate() { }

    internal BlockedDate(Guid propertyId, DateOnly date)
    {
        PropertyId = propertyId;
        Date = date;
    }

    public Guid PropertyId { get; private set; }
    public DateOnly Date { get; private set; }
}

public sealed record PropertyPublishedDomainEvent(Guid PropertyId, Guid HostId) : IDomainEvent;
public sealed record PropertyChangedDomainEvent(Guid PropertyId, string Change) : IDomainEvent;
