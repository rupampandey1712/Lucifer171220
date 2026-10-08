using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StaySphere.Api.Common;
using StaySphere.Application.Booking;
using StaySphere.Application.Payments;
using StaySphere.Contracts;
using StaySphere.Contracts.Booking;
using StaySphere.Contracts.Payments;

namespace StaySphere.Api.Controllers;

[Route("api/v{version:apiVersion}/reservations")]
[Authorize]
public sealed class ReservationsController(IReservationService reservations, IPaymentService payments) : ApiControllerBase
{
    /// <summary>
    /// Creates a 10-minute hold. Requires an Idempotency-Key and the signed quote token from POST /properties/{id}/quote.
    /// Returns 409 if the dates were taken concurrently (database-enforced) or the price changed.
    /// </summary>
    [HttpPost]
    [Idempotent("reservations.create")]
    [ProducesResponseType(typeof(ReservationDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReservationDto>> Create(CreateReservationRequest request, CancellationToken ct) =>
        Created(await reservations.CreateHoldAsync(request, ct), r => $"/api/v1/reservations/{r.Id}");

    [HttpGet]
    public async Task<ActionResult<PagedResult<ReservationDto>>> Mine([FromQuery] string? scope, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default) => Ok(await reservations.ListMineAsync(scope, page, pageSize, ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReservationDto>> Get(Guid id, CancellationToken ct) => FromResult(await reservations.GetAsync(id, ct));

    [HttpPost("{id:guid}/cancel/preview")]
    public async Task<ActionResult<CancellationPreviewDto>> PreviewCancel(Guid id, CancellationToken ct) =>
        FromResult(await reservations.PreviewCancellationAsync(id, ct));

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = Policies.CanManageReservation)]
    [Idempotent("reservations.cancel")]
    public async Task<ActionResult<ReservationDto>> Cancel(Guid id, CancelReservationRequest request, CancellationToken ct) =>
        FromResult(await reservations.CancelAsync(id, request.Reason, ct));

    /// <summary>
    /// Pays for a held reservation with a provider token (e.g. tok_4242). Idempotency-Key is mandatory: the same key never
    /// charges twice and replays the original response. 402 = declined; 200 with status Pending = awaiting provider.
    /// </summary>
    [HttpPost("{id:guid}/payment")]
    [Idempotent("reservations.payment")]
    [EnableRateLimiting(RateLimitPolicies.Payment)]
    [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status402PaymentRequired)]
    public async Task<ActionResult<PaymentDto>> Pay(Guid id, PayReservationRequest request, CancellationToken ct) =>
        FromResult(await payments.PayAsync(id, request, Request.Headers[IdempotentAttribute.HeaderName].ToString(), ct));

    [HttpGet("{id:guid}/payment")]
    public async Task<ActionResult<PaymentDto>> GetPayment(Guid id, CancellationToken ct) => FromResult(await payments.GetForReservationAsync(id, ct));
}

[Route("api/v{version:apiVersion}/payments")]
public sealed class PaymentsController(IPaymentService payments) : ApiControllerBase
{
    /// <summary>Provider webhook. Authenticated by HMAC signature + timestamp (no JWT); duplicate events are ignored.</summary>
    [HttpPost("webhook/{provider}")]
    [AllowAnonymous]
    [Consumes("application/json")]
    public async Task<IActionResult> Webhook(string provider, CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync(ct);
        if (payload.Length > 64 * 1024) return StatusCode(StatusCodes.Status413PayloadTooLarge);
        var signature = Request.Headers["X-Fake-Signature"].ToString();
        _ = long.TryParse(Request.Headers["X-Fake-Timestamp"].ToString(), out var timestamp);
        var result = await payments.HandleWebhookAsync(provider, payload, signature, timestamp, ct);
        return result.IsSuccess ? Ok() : Problem(result.Error!);
    }
}
