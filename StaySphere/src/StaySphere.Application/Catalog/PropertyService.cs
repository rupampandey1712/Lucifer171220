using Microsoft.EntityFrameworkCore;
using StaySphere.Application.Abstractions;
using StaySphere.Application.Common;
using StaySphere.Contracts.Properties;
using StaySphere.Domain.Booking;
using StaySphere.Domain.Catalog;
using StaySphere.Domain.Common;
using StaySphere.Domain.Identity;
using StaySphere.Domain.ValueObjects;

namespace StaySphere.Application.Catalog;

public interface IPropertyService
{
    Task<PropertyDetailDto?> GetAsync(Guid id, CancellationToken ct);
    Task<Result<PropertyDetailDto>> CreateAsync(CreatePropertyRequest request, CancellationToken ct);
    Task<Result<PropertyDetailDto>> UpdateAsync(Guid id, UpdatePropertyRequest request, CancellationToken ct);
    Task<Result> PublishAsync(Guid id, CancellationToken ct);
    Task<Result> UnpublishAsync(Guid id, CancellationToken ct);
    Task<Result> DeleteAsync(Guid id, CancellationToken ct);
    Task<Result<ImageDto>> UploadImageAsync(Guid id, Stream content, CancellationToken ct);
    Task<Result> DeleteImageAsync(Guid id, Guid imageId, CancellationToken ct);
    Task<Result> ReorderImagesAsync(Guid id, IReadOnlyList<Guid> imageIds, CancellationToken ct);
    Task<IReadOnlyList<HostPropertyListItemDto>> ListForHostAsync(CancellationToken ct);
    Task<IReadOnlyList<AmenityDto>> GetAmenitiesAsync(CancellationToken ct);
}

/// <summary>Resource-based authorization rules shared by services (backend is the only enforcement point).</summary>
public static class AccessRules
{
    public static bool CanManageProperty(ICurrentUser user, Property property) =>
        user.UserId == property.HostId || user.IsInRole(Roles.Admin);

    public static bool CanViewReservation(ICurrentUser user, Reservation reservation) =>
        user.UserId == reservation.GuestId || user.UserId == reservation.HostId || user.IsStaff();
}

public sealed class PropertyService(
    IAppDbContext db,
    ICurrentUser currentUser,
    ICacheService cache,
    IImageProcessor imageProcessor,
    IMalwareScanner malwareScanner,
    IFileStorageService storage,
    IAuditLogger audit,
    TimeProvider clock) : IPropertyService
{
    public const string ImageContainer = "property-images";
    private static readonly TimeSpan DetailTtl = TimeSpan.FromMinutes(10);

    private static readonly Error NotFound = Error.NotFound("property.not_found", "Listing not found.");
    private static readonly Error Forbidden = Error.Forbidden("property.forbidden", "You can only manage your own listings.");

    public async Task<PropertyDetailDto?> GetAsync(Guid id, CancellationToken ct)
    {
        var dto = await cache.GetOrCreateAsync(CacheKeys.Property(id), DetailTtl, c => LoadDetailAsync(id, c), ct);
        if (dto is null) return null;
        if (dto.Status == nameof(PropertyStatus.Published)) return dto;
        // Drafts / unlisted listings are only visible to their host and admins.
        return currentUser.UserId == dto.HostId || currentUser.IsInRole(Roles.Admin) ? dto : null;
    }

    public async Task<Result<PropertyDetailDto>> CreateAsync(CreatePropertyRequest request, CancellationToken ct)
    {
        var hostId = currentUser.RequireUserId();
        if (!Enum.TryParse<PropertyType>(request.PropertyType, true, out var type) ||
            !Enum.TryParse<RoomType>(request.RoomType, true, out var roomType))
            return Error.Validation("property.type", "Unknown property or room type.");

        var property = Property.CreateDraft(hostId, type, roomType, clock.GetUtcNow());
        if (!string.IsNullOrWhiteSpace(request.Title)) property.UpdateBasics(request.Title, string.Empty, type, roomType);
        db.Properties.Add(property);
        audit.Record("property.created", nameof(Property), property.Id.ToString());
        await db.SaveChangesAsync(ct);
        return (await LoadDetailAsync(property.Id, ct))!;
    }

    public async Task<Result<PropertyDetailDto>> UpdateAsync(Guid id, UpdatePropertyRequest r, CancellationToken ct)
    {
        var property = await LoadForWriteAsync(id, ct);
        if (property.IsFailure) return property.Error!;
        var p = property.Value;

        if (r.Title is not null || r.Description is not null || r.PropertyType is not null || r.RoomType is not null)
        {
            var type = r.PropertyType is null ? p.PropertyType : Enum.Parse<PropertyType>(r.PropertyType, true);
            var room = r.RoomType is null ? p.RoomType : Enum.Parse<RoomType>(r.RoomType, true);
            p.UpdateBasics(r.Title ?? p.Title, r.Description ?? p.Description, type, room);
        }

        if (r.MaxGuests is not null || r.Bedrooms is not null || r.Beds is not null || r.Bathrooms is not null)
        {
            var rooms = p.UpdateRooms(r.MaxGuests ?? p.MaxGuests, r.Bedrooms ?? p.Bedrooms, r.Beds ?? p.Beds, r.Bathrooms ?? p.Bathrooms);
            if (rooms.IsFailure) return rooms.Error!;
        }

        if (r.Address is not null && r.Latitude is not null && r.Longitude is not null)
        {
            var coords = Coordinates.Create(r.Latitude.Value, r.Longitude.Value);
            if (coords.IsFailure) return coords.Error!;
            var a = r.Address;
            p.SetLocation(new Address(a.Line1, a.Line2, a.City, a.Region, a.PostalCode, a.CountryCode.ToUpperInvariant(), a.Country), coords.Value);
        }

        if (r.Amenities is not null)
        {
            var known = await db.Amenities.Select(a => a.Code).ToListAsync(ct);
            var unknown = r.Amenities.Except(known, StringComparer.OrdinalIgnoreCase).ToList();
            if (unknown.Count > 0) return Error.Validation("property.amenities", $"Unknown amenities: {string.Join(", ", unknown)}.");
            p.SetAmenities(r.Amenities);
        }

        if (r.Pricing is { } pr)
        {
            var pricing = p.UpdatePricing(pr.BasePrice, pr.CleaningFee, pr.Currency, pr.WeekendAdjustmentPercent,
                pr.WeeklyDiscountPercent, pr.MonthlyDiscountPercent, pr.MinNights);
            if (pricing.IsFailure) return pricing.Error!;
        }

        if (r.Rules is { } ru)
        {
            if (!Enum.TryParse<CancellationPolicy>(ru.CancellationPolicy, true, out var policy))
                return Error.Validation("property.policy", "Unknown cancellation policy.");
            p.UpdateRules(ru.PetsAllowed, ru.SmokingAllowed, ru.EventsAllowed, ru.HouseRules,
                TimeOnly.Parse(ru.CheckInTime, System.Globalization.CultureInfo.InvariantCulture),
                TimeOnly.Parse(ru.CheckOutTime, System.Globalization.CultureInfo.InvariantCulture), policy, ru.InstantBook);
        }

        audit.Record("property.updated", nameof(Property), id.ToString());
        var saved = await SaveAsync(ct);
        if (saved.IsFailure) return saved.Error!;
        await cache.RemoveAsync(CacheKeys.Property(id), ct);
        return (await LoadDetailAsync(id, ct))!;
    }

    public async Task<Result> PublishAsync(Guid id, CancellationToken ct)
    {
        var property = await LoadForWriteAsync(id, ct);
        if (property.IsFailure) return property.Error!;
        var result = property.Value.Publish(clock.GetUtcNow());
        if (result.IsFailure) return result;
        audit.Record("property.published", nameof(Property), id.ToString());
        var saved = await SaveAsync(ct);
        await cache.RemoveAsync(CacheKeys.Property(id), ct);
        return saved;
    }

    public async Task<Result> UnpublishAsync(Guid id, CancellationToken ct)
    {
        var property = await LoadForWriteAsync(id, ct);
        if (property.IsFailure) return property.Error!;
        property.Value.Unpublish();
        audit.Record("property.unpublished", nameof(Property), id.ToString());
        var saved = await SaveAsync(ct);
        await cache.RemoveAsync(CacheKeys.Property(id), ct);
        return saved;
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct)
    {
        var property = await LoadForWriteAsync(id, ct);
        if (property.IsFailure) return property.Error!;
        var hasUpcoming = await db.Reservations.AnyAsync(r => r.PropertyId == id && r.Status == ReservationStatus.Confirmed, ct);
        if (hasUpcoming) return Error.Conflict("property.has_reservations", "This listing has upcoming reservations.");
        property.Value.SoftDelete(clock.GetUtcNow());
        audit.Record("property.deleted", nameof(Property), id.ToString());
        var saved = await SaveAsync(ct);
        await cache.RemoveAsync(CacheKeys.Property(id), ct);
        return saved;
    }

    public async Task<Result<ImageDto>> UploadImageAsync(Guid id, Stream content, CancellationToken ct)
    {
        var property = await LoadForWriteAsync(id, ct);
        if (property.IsFailure) return property.Error!;
        if (property.Value.Images.Count >= 30) return Error.Validation("image.limit", "A listing can have at most 30 photos.");

        var processed = await imageProcessor.ProcessAsync(content, ct);
        if (processed.IsFailure) return processed.Error!;
        if (!await malwareScanner.IsCleanAsync(processed.Value.Full, ct))
            return Error.Validation("image.rejected", "This file was rejected by the security scan.");

        var baseKey = $"{id:N}/{Guid.NewGuid():N}";
        await using var full = new MemoryStream(processed.Value.Full);
        await using var thumb = new MemoryStream(processed.Value.Thumbnail);
        var storedFull = await storage.UploadAsync(ImageContainer, baseKey + processed.Value.Extension, full, processed.Value.ContentType, ct);
        var storedThumb = await storage.UploadAsync(ImageContainer, baseKey + "_thumb" + processed.Value.Extension, thumb, processed.Value.ContentType, ct);

        var image = property.Value.AddImage(storedFull.Url, storedThumb.Url, storedFull.Key, processed.Value.Width, processed.Value.Height);
        audit.Record("property.image_added", nameof(Property), id.ToString(), new { imageId = image.Id });
        var saved = await SaveAsync(ct);
        if (saved.IsFailure) return saved.Error!;
        await cache.RemoveAsync(CacheKeys.Property(id), ct);
        return new ImageDto(image.Id, image.Url, image.ThumbnailUrl, image.Width, image.Height, image.SortOrder);
    }

    public async Task<Result> DeleteImageAsync(Guid id, Guid imageId, CancellationToken ct)
    {
        var property = await LoadForWriteAsync(id, ct);
        if (property.IsFailure) return property.Error!;
        var blobKey = property.Value.Images.FirstOrDefault(i => i.Id == imageId)?.BlobKey;
        var result = property.Value.RemoveImage(imageId);
        if (result.IsFailure) return result;
        var saved = await SaveAsync(ct);
        if (saved.IsFailure) return saved;
        if (blobKey is not null) await storage.DeleteAsync(ImageContainer, blobKey, ct);
        await cache.RemoveAsync(CacheKeys.Property(id), ct);
        return Result.Success();
    }

    public async Task<Result> ReorderImagesAsync(Guid id, IReadOnlyList<Guid> imageIds, CancellationToken ct)
    {
        var property = await LoadForWriteAsync(id, ct);
        if (property.IsFailure) return property.Error!;
        property.Value.ReorderImages(imageIds);
        var saved = await SaveAsync(ct);
        await cache.RemoveAsync(CacheKeys.Property(id), ct);
        return saved;
    }

    public async Task<IReadOnlyList<HostPropertyListItemDto>> ListForHostAsync(CancellationToken ct)
    {
        var hostId = currentUser.RequireUserId();
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var rows = await db.Properties.AsNoTracking()
            .Where(p => p.HostId == hostId && p.DeletedAt == null)
            .OrderByDescending(p => p.UpdatedAt)
            .Select(p => new
            {
                p.Id, p.Title, p.Status, City = p.Address != null ? p.Address.City : "", p.BasePrice, p.Currency,
                Cover = p.Images.OrderBy(i => i.SortOrder).Select(i => i.ThumbnailUrl).FirstOrDefault(),
                p.RatingAverage, p.ReviewCount, p.UpdatedAt, p.Description, HasLocation = p.Latitude != null,
                ImageCount = p.Images.Count, AmenityCount = p.Amenities.Count,
                Upcoming = db.Reservations.Count(r => r.PropertyId == p.Id && r.Status == ReservationStatus.Confirmed && r.CheckIn >= today),
            })
            .ToListAsync(ct);

        return rows.Select(p =>
        {
            var steps = new[] { p.Title.Length >= 5, p.Description.Length >= 20, p.HasLocation, p.BasePrice > 0, p.ImageCount > 0, p.AmenityCount > 0 };
            return new HostPropertyListItemDto(p.Id, p.Title.Length == 0 ? "Untitled listing" : p.Title, p.Status.ToString(), p.City,
                p.BasePrice, p.Currency, p.Cover, p.RatingAverage, p.ReviewCount, p.Upcoming, p.UpdatedAt,
                steps.Count(s => s) * 100 / steps.Length);
        }).ToList();
    }

    public Task<IReadOnlyList<AmenityDto>> GetAmenitiesAsync(CancellationToken ct) =>
        cache.GetOrCreateAsync<IReadOnlyList<AmenityDto>>(CacheKeys.Amenities, TimeSpan.FromHours(6), async c =>
            await db.Amenities.AsNoTracking().OrderBy(a => a.Category).ThenBy(a => a.Name)
                .Select(a => new AmenityDto(a.Code, a.Name, a.Category, a.Icon)).ToListAsync(c), ct);

    private async Task<Result<Property>> LoadForWriteAsync(Guid id, CancellationToken ct)
    {
        var property = await db.Properties
            .Include(p => p.Images).Include(p => p.Amenities)
            .FirstOrDefaultAsync(p => p.Id == id && p.DeletedAt == null, ct);
        if (property is null) return NotFound;
        if (!AccessRules.CanManageProperty(currentUser, property)) return Forbidden;
        return property;
    }

    private async Task<Result> SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict("property.concurrency", "This listing was changed by someone else. Reload and try again.");
        }
    }

    private async Task<PropertyDetailDto?> LoadDetailAsync(Guid id, CancellationToken ct)
    {
        var p = await db.Properties.AsNoTracking()
            .Include(x => x.Images).Include(x => x.Amenities)
            .FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, ct);
        if (p is null) return null;

        var host = await db.Users.AsNoTracking().Where(u => u.Id == p.HostId)
            .Select(u => new { u.Id, u.DisplayName, u.AvatarUrl, u.CreatedAt }).FirstAsync(ct);
        var hostStats = await db.Properties.AsNoTracking()
            .Where(x => x.HostId == p.HostId && x.Status == PropertyStatus.Published && x.DeletedAt == null)
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Rating = g.Where(x => x.ReviewCount > 0).Average(x => (double?)x.RatingAverage) ?? 0 })
            .FirstOrDefaultAsync(ct);

        var codes = p.Amenities.Select(a => a.AmenityCode).ToList();
        var amenities = await db.Amenities.AsNoTracking().Where(a => codes.Contains(a.Code))
            .Select(a => new AmenityDto(a.Code, a.Name, a.Category, a.Icon)).ToListAsync(ct);

        return new PropertyDetailDto(p.Id, p.HostId, p.Title, p.Description, p.PropertyType.ToString(), p.RoomType.ToString(),
            p.MaxGuests, p.Bedrooms, p.Beds, p.Bathrooms, p.BasePrice, p.CleaningFee, p.Currency, p.WeekendAdjustmentPercent,
            p.WeeklyDiscountPercent, p.MonthlyDiscountPercent, p.MinNights,
            p.Address is null ? null : new AddressDto(p.Address.Line1, p.Address.Line2, p.Address.City, p.Address.Region,
                p.Address.PostalCode, p.Address.CountryCode, p.Address.Country),
            p.Latitude, p.Longitude, p.Status.ToString(), p.InstantBook, p.PetsAllowed, p.SmokingAllowed, p.EventsAllowed,
            p.HouseRules, p.CheckInTime.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
            p.CheckOutTime.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture), p.CancellationPolicy.ToString(),
            p.RatingAverage, p.ReviewCount,
            p.Images.OrderBy(i => i.SortOrder).Select(i => new ImageDto(i.Id, i.Url, i.ThumbnailUrl, i.Width, i.Height, i.SortOrder)).ToList(),
            amenities,
            new HostSummaryDto(host.Id, host.DisplayName, host.AvatarUrl, host.CreatedAt, hostStats?.Count ?? 0, Math.Round(hostStats?.Rating ?? 0, 2)),
            p.UpdatedAt);
    }
}
