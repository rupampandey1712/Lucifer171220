using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.FeatureManagement.Mvc;
using StaySphere.Api.Common;
using StaySphere.Application.Experiences;
using StaySphere.Contracts.Experiences;

namespace StaySphere.Api.Controllers;

/// <summary>Hosted experiences (tours, classes). Behind the <c>Experiences</c> feature flag: 404 everywhere while it is off.</summary>
[Route("api/v{version:apiVersion}/experiences")]
[FeatureGate("Experiences")]
public sealed class ExperiencesController(IExperienceService experiences) : ApiControllerBase
{
    /// <summary>Published experiences with an upcoming session that has room for <paramref name="guests"/>, soonest first.</summary>
    [HttpGet]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Anonymous)]
    public async Task<ActionResult<IReadOnlyList<ExperienceCardDto>>> Search([FromQuery] string? city, [FromQuery] string? category,
        [FromQuery] DateOnly? date, [FromQuery] int? guests, CancellationToken ct) => Ok(await experiences.SearchAsync(city, category, date, guests, ct));

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Anonymous)]
    public async Task<ActionResult<ExperienceDetailDto>> Get(Guid id, CancellationToken ct) =>
        await experiences.GetAsync(id, ct) is { } e ? Ok(e) : NotFound();

    /// <summary>Books seats in a session. 409 when the session doesn't have enough seats left (checked atomically).</summary>
    [HttpPost("{id:guid}/bookings")]
    [Authorize]
    [Idempotent("experiences.book")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ExperienceBookingDto>> Book(Guid id, BookExperienceRequest request, CancellationToken ct) =>
        Created(await experiences.BookAsync(id, request, ct), b => $"/api/v1/experience-bookings/{b.Id}");
}

[Route("api/v{version:apiVersion}/experience-bookings")]
[FeatureGate("Experiences")]
[Authorize]
public sealed class ExperienceBookingsController(IExperienceService experiences) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ExperienceBookingDto>>> Mine(CancellationToken ct) => Ok(await experiences.MyBookingsAsync(ct));

    /// <summary>Free cancellation up to 24 hours before the start; the seats go back on sale.</summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<ExperienceBookingDto>> Cancel(Guid id, CancellationToken ct) => FromResult(await experiences.CancelBookingAsync(id, ct));
}

[Route("api/v{version:apiVersion}/host/experiences")]
[FeatureGate("Experiences")]
[Authorize(Policy = Policies.CanManageProperty)]
public sealed class HostExperiencesController(IExperienceService experiences) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ExperienceDetailDto>>> Mine(CancellationToken ct) => Ok(await experiences.ListMineAsync(ct));

    /// <summary>Creates a draft. Add at least one session, then publish.</summary>
    [HttpPost]
    public async Task<ActionResult<ExperienceDetailDto>> Create(CreateExperienceRequest request, CancellationToken ct) =>
        Created(await experiences.CreateAsync(request, ct), e => $"/api/v1/experiences/{e.Id}");

    [HttpPost("{id:guid}/slots")]
    public async Task<ActionResult<ExperienceSlotDto>> AddSlot(Guid id, AddExperienceSlotRequest request, CancellationToken ct) =>
        FromResult(await experiences.AddSlotAsync(id, request, ct));

    [HttpPost("{id:guid}/publish")]
    public async Task<IActionResult> Publish(Guid id, CancellationToken ct) => FromResult(await experiences.PublishAsync(id, ct));
}
