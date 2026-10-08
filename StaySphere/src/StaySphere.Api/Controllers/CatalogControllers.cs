using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StaySphere.Api.Common;
using StaySphere.Application.Abstractions;
using StaySphere.Application.Booking;
using StaySphere.Application.Catalog;
using StaySphere.Application.Identity;
using StaySphere.Application.Reviews;
using StaySphere.Application.Search;
using StaySphere.Contracts;
using StaySphere.Contracts.Auth;
using StaySphere.Contracts.Booking;
using StaySphere.Contracts.Properties;
using StaySphere.Contracts.Reviews;
using StaySphere.Contracts.Search;

namespace StaySphere.Api.Controllers;

[Route("api/v{version:apiVersion}/me")]
[Authorize]
public sealed class MeController(IProfileService profiles, IAuthService auth) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<UserDto>> Get(CancellationToken ct) =>
        await profiles.GetMeAsync(ct) is { } me ? Ok(me) : NotFound();

    [HttpPut("profile")]
    public async Task<ActionResult<UserDto>> Update(UpdateProfileRequest request, CancellationToken ct) => FromResult(await profiles.UpdateAsync(request, ct));

    [HttpPost("avatar")]
    [EnableRateLimiting(RateLimitPolicies.Upload)]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public async Task<ActionResult<UserDto>> Avatar(IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        return FromResult(await profiles.SetAvatarAsync(stream, ct));
    }

    /// <summary>Adds the Host role and returns a fresh access token containing it.</summary>
    [HttpPost("become-host")]
    public async Task<ActionResult<AuthResponse>> BecomeHost(CancellationToken ct)
    {
        var result = await profiles.BecomeHostAsync(ct);
        if (result.IsFailure) return Problem(result.Error!);
        var tokens = await auth.IssueForCurrentUserAsync(ct);
        return tokens.IsSuccess ? Ok(tokens.Value.Response) : Problem(tokens.Error!);
    }

    [HttpGet("export")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> Export(CancellationToken ct) => Ok(await profiles.ExportAsync(ct));

    [HttpDelete]
    public async Task<IActionResult> Delete(CancellationToken ct)
    {
        var result = await profiles.DeleteAccountAsync(ct);
        if (result.IsSuccess) Response.Cookies.Delete(AuthController.RefreshCookie);
        return FromResult(result);
    }
}

[Route("api/v{version:apiVersion}/properties")]
public sealed class PropertiesController(
    IPropertyService properties,
    IAvailabilityService availability,
    IPricingService pricing,
    IReviewService reviews,
    ISearchService search,
    IWeatherService weather) : ApiControllerBase
{
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PropertyDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PropertyDetailDto>> Get(Guid id, CancellationToken ct) =>
        await properties.GetAsync(id, ct) is { } p ? Ok(p) : Problem(Domain.Common.Error.NotFound("property.not_found", "Listing not found."));

    [HttpPost]
    [Authorize(Policy = Policies.CanCreateProperty)]
    [ProducesResponseType(typeof(PropertyDetailDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<PropertyDetailDto>> Create(CreatePropertyRequest request, CancellationToken ct) =>
        Created(await properties.CreateAsync(request, ct), p => $"/api/v1/properties/{p.Id}");

    /// <summary>Partial update used by every step of the listing wizard (autosave).</summary>
    [HttpPatch("{id:guid}")]
    [Authorize(Policy = Policies.CanManageProperty)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PropertyDetailDto>> Update(Guid id, UpdatePropertyRequest request, CancellationToken ct) =>
        FromResult(await properties.UpdateAsync(id, request, ct));

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.CanManageProperty)]
    public Task<ActionResult<PropertyDetailDto>> Replace(Guid id, UpdatePropertyRequest request, CancellationToken ct) => Update(id, request, ct);

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.CanManageProperty)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) => FromResult(await properties.DeleteAsync(id, ct));

    [HttpPost("{id:guid}/publish")]
    [Authorize(Policy = Policies.CanManageProperty)]
    public async Task<IActionResult> Publish(Guid id, CancellationToken ct) => FromResult(await properties.PublishAsync(id, ct));

    [HttpPost("{id:guid}/unpublish")]
    [Authorize(Policy = Policies.CanManageProperty)]
    public async Task<IActionResult> Unpublish(Guid id, CancellationToken ct) => FromResult(await properties.UnpublishAsync(id, ct));

    [HttpPost("{id:guid}/images")]
    [Authorize(Policy = Policies.CanManageProperty)]
    [EnableRateLimiting(RateLimitPolicies.Upload)]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public async Task<ActionResult<ImageDto>> UploadImage(Guid id, IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        return FromResult(await properties.UploadImageAsync(id, stream, ct));
    }

    [HttpDelete("{id:guid}/images/{imageId:guid}")]
    [Authorize(Policy = Policies.CanManageProperty)]
    public async Task<IActionResult> DeleteImage(Guid id, Guid imageId, CancellationToken ct) => FromResult(await properties.DeleteImageAsync(id, imageId, ct));

    [HttpPut("{id:guid}/images/order")]
    [Authorize(Policy = Policies.CanManageProperty)]
    public async Task<IActionResult> ReorderImages(Guid id, ReorderImagesRequest request, CancellationToken ct) =>
        FromResult(await properties.ReorderImagesAsync(id, request.ImageIds, ct));

    [HttpGet("{id:guid}/availability")]
    [AllowAnonymous]
    public async Task<ActionResult<AvailabilityDto>> Availability(Guid id, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        var start = from ?? DateOnly.FromDateTime(DateTime.UtcNow);
        return await availability.GetAsync(id, start, to ?? start.AddDays(365), ct) is { } a ? Ok(a) : NotFound();
    }

    [HttpPut("{id:guid}/blocked-dates")]
    [Authorize(Policy = Policies.CanManageProperty)]
    public async Task<IActionResult> BlockDates(Guid id, BlockDatesRequest request, CancellationToken ct) =>
        FromResult(await availability.UpdateBlockedDatesAsync(id, request, ct));

    [HttpGet("{id:guid}/seasonal-prices")]
    [Authorize(Policy = Policies.CanManageProperty)]
    public async Task<ActionResult<IReadOnlyList<SeasonalPriceDto>>> SeasonalPrices(Guid id, CancellationToken ct) =>
        FromResult(await availability.GetSeasonalPricesAsync(id, ct));

    [HttpPost("{id:guid}/seasonal-prices")]
    [Authorize(Policy = Policies.CanManageProperty)]
    public async Task<ActionResult<SeasonalPriceDto>> AddSeasonalPrice(Guid id, SeasonalPriceRequest request, CancellationToken ct) =>
        FromResult(await availability.AddSeasonalPriceAsync(id, request, ct));

    [HttpDelete("{id:guid}/seasonal-prices/{seasonId:guid}")]
    [Authorize(Policy = Policies.CanManageProperty)]
    public async Task<IActionResult> RemoveSeasonalPrice(Guid id, Guid seasonId, CancellationToken ct) =>
        FromResult(await availability.RemoveSeasonalPriceAsync(id, seasonId, ct));

    /// <summary>Server-authoritative price quote; returns a signed quote token required to reserve.</summary>
    [HttpPost("{id:guid}/quote")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Anonymous)]
    public async Task<ActionResult<QuoteDto>> Quote(Guid id, QuoteRequest request, CancellationToken ct) => FromResult(await pricing.QuoteAsync(id, request, ct));

    [HttpGet("{id:guid}/reviews")]
    [AllowAnonymous]
    public async Task<ActionResult<PagedResult<ReviewDto>>> Reviews(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken ct = default) =>
        Ok(await reviews.ListForPropertyAsync(id, page, pageSize, ct));

    [HttpGet("{id:guid}/reviews/summary")]
    [AllowAnonymous]
    public async Task<ActionResult<RatingSummaryDto>> ReviewSummary(Guid id, CancellationToken ct) => Ok(await reviews.SummaryAsync(id, ct));

    [HttpGet("{id:guid}/similar")]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<PropertyCardDto>>> Similar(Guid id, CancellationToken ct) => Ok(await search.SimilarAsync(id, ct));

    /// <summary>Cached weather near the property. Returns 204 when the provider is unavailable (page still works).</summary>
    [HttpGet("{id:guid}/weather")]
    [AllowAnonymous]
    public async Task<IActionResult> Weather(Guid id, CancellationToken ct)
    {
        var p = await properties.GetAsync(id, ct);
        if (p?.Latitude is null) return NotFound();
        var forecast = await weather.GetForecastAsync(p.Latitude.Value, p.Longitude!.Value, ct);
        return forecast is null ? NoContent() : Ok(forecast);
    }

    [HttpGet("~/api/v{version:apiVersion}/amenities")]
    [AllowAnonymous]
    [ResponseCache(Duration = 3600)]
    public async Task<ActionResult<IReadOnlyList<AmenityDto>>> Amenities(CancellationToken ct) => Ok(await properties.GetAmenitiesAsync(ct));
}

[Route("api/v{version:apiVersion}/search")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Anonymous)]
public sealed class SearchController(ISearchService search) : ApiControllerBase
{
    /// <summary>Search published stays. Supports text location, radius, map bounds, dates, filters, sorting and paging.</summary>
    [HttpGet("properties")]
    public async Task<ActionResult<PagedResult<PropertyCardDto>>> Search([FromQuery] SearchPropertiesQuery query, CancellationToken ct) =>
        Ok(await search.SearchAsync(query, ct));

    [HttpGet("suggest")]
    public async Task<ActionResult<IReadOnlyList<string>>> Suggest([FromQuery] string q, CancellationToken ct) => Ok(await search.SuggestAsync(q, ct));

    [HttpGet("~/api/v{version:apiVersion}/destinations/featured")]
    public async Task<ActionResult<IReadOnlyList<DestinationDto>>> Featured(CancellationToken ct) => Ok(await search.GetFeaturedDestinationsAsync(ct));
}

[Route("api/v{version:apiVersion}")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Anonymous)]
public sealed class GeoController(IGeocodingService geocoding, ICurrencyService currency) : ApiControllerBase
{
    /// <summary>Throttled, cached geocoding proxy (OpenStreetMap Nominatim in Live mode).</summary>
    [HttpGet("geo/geocode")]
    public async Task<ActionResult<IReadOnlyList<GeocodeResultDto>>> Geocode([FromQuery] string q, CancellationToken ct) =>
        Ok(await geocoding.SearchAsync(q ?? string.Empty, ct));

    [HttpGet("currencies/rates")]
    public async Task<ActionResult<ExchangeRates>> Rates([FromQuery] string @base = "USD", CancellationToken ct = default) =>
        @base.Length == 3 ? Ok(await currency.GetRatesAsync(@base, ct)) : BadRequest();
}
