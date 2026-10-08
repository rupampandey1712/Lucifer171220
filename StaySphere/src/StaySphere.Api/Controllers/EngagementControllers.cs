using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.FeatureManagement;
using Microsoft.FeatureManagement.Mvc;
using StaySphere.Api.Common;
using StaySphere.Application.Admin;
using StaySphere.Application.Ai;
using StaySphere.Application.Booking;
using StaySphere.Application.Catalog;
using StaySphere.Application.Engagement;
using StaySphere.Application.Payments;
using StaySphere.Application.Reviews;
using StaySphere.Application.Support;
using StaySphere.Contracts;
using StaySphere.Contracts.Admin;
using StaySphere.Contracts.Ai;
using StaySphere.Contracts.Booking;
using StaySphere.Contracts.Engagement;
using StaySphere.Contracts.Payments;
using StaySphere.Contracts.Properties;
using StaySphere.Contracts.Reviews;
using StaySphere.Contracts.Support;

namespace StaySphere.Api.Controllers;

[Route("api/v{version:apiVersion}/reviews")]
public sealed class ReviewsController(IReviewService reviews, ISupportService support) : ApiControllerBase
{
    /// <summary>Guests may review only their own completed stays, after checkout, once per reservation.</summary>
    [HttpPost]
    [Authorize]
    [ProducesResponseType(typeof(ReviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReviewDto>> Create(CreateReviewRequest request, CancellationToken ct) => FromResult(await reviews.CreateAsync(request, ct));

    [HttpPost("{id:guid}/response")]
    [Authorize(Policy = Policies.CanManageProperty)]
    public async Task<IActionResult> Respond(Guid id, ReviewResponseRequest request, CancellationToken ct) =>
        FromResult(await reviews.RespondAsync(id, request.Response, ct));

    [HttpPost("{id:guid}/report")]
    [Authorize]
    public async Task<IActionResult> Report(Guid id, [FromBody] CreateReportRequest request, CancellationToken ct) =>
        FromResult(await support.FileReportAsync(request with { TargetType = "Review", TargetId = id }, ct));
}

[Route("api/v{version:apiVersion}/favorites")]
[Authorize]
public sealed class FavoritesController(IFavoriteService favorites) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PropertyCardDto>>> List(CancellationToken ct) => Ok(await favorites.ListAsync(ct));

    [HttpGet("ids")]
    public async Task<ActionResult<IReadOnlyList<Guid>>> Ids(CancellationToken ct) => Ok(await favorites.ListIdsAsync(ct));

    /// <summary>Idempotent: favoriting twice is a no-op.</summary>
    [HttpPut("{propertyId:guid}")]
    [HttpPost("{propertyId:guid}")]
    public async Task<IActionResult> Add(Guid propertyId, CancellationToken ct) => FromResult(await favorites.AddAsync(propertyId, ct));

    [HttpDelete("{propertyId:guid}")]
    public async Task<IActionResult> Remove(Guid propertyId, CancellationToken ct)
    {
        await favorites.RemoveAsync(propertyId, ct);
        return NoContent();
    }
}

[Route("api/v{version:apiVersion}/conversations")]
[Authorize]
public sealed class ConversationsController(IMessagingService messaging) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ConversationDto>>> List(CancellationToken ct) => Ok(await messaging.ListConversationsAsync(ct));

    [HttpGet("unread-count")]
    public async Task<ActionResult<int>> Unread(CancellationToken ct) => Ok(await messaging.UnreadCountAsync(ct));

    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Messaging)]
    public async Task<ActionResult<ConversationDto>> Start(StartConversationRequest request, CancellationToken ct) => FromResult(await messaging.StartAsync(request, ct));

    [HttpGet("{id:guid}/messages")]
    public async Task<ActionResult<PagedResult<MessageDto>>> Messages(Guid id, [FromQuery] DateTimeOffset? before, [FromQuery] int pageSize = 50,
        CancellationToken ct = default) => FromResult(await messaging.GetMessagesAsync(id, before, pageSize, ct));

    [HttpPost("{id:guid}/messages")]
    [EnableRateLimiting(RateLimitPolicies.Messaging)]
    public async Task<ActionResult<MessageDto>> Send(Guid id, SendMessageRequest request, CancellationToken ct) => FromResult(await messaging.SendAsync(id, request.Body, ct));

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> Read(Guid id, CancellationToken ct) => FromResult(await messaging.MarkReadAsync(id, ct));

    [HttpPost("{id:guid}/report")]
    public async Task<IActionResult> Report(Guid id, CreateReportRequest request, CancellationToken ct) => FromResult(await messaging.ReportAsync(id, request.Reason, ct));
}

[Route("api/v{version:apiVersion}/notifications")]
[Authorize]
public sealed class NotificationsController(INotificationService notifications) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<NotificationDto>>> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await notifications.ListAsync(page, pageSize, ct));

    [HttpGet("unread-count")]
    public async Task<ActionResult<int>> Unread(CancellationToken ct) => Ok(await notifications.UnreadCountAsync(ct));

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> Read(Guid id, CancellationToken ct)
    {
        await notifications.MarkReadAsync(id, ct);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> ReadAll(CancellationToken ct)
    {
        await notifications.MarkAllReadAsync(ct);
        return NoContent();
    }
}

[Route("api/v{version:apiVersion}/support")]
[Authorize]
public sealed class SupportController(ISupportService support) : ApiControllerBase
{
    [HttpPost("tickets")]
    public async Task<ActionResult<TicketDto>> Create(CreateTicketRequest request, CancellationToken ct) => FromResult(await support.CreateAsync(request, ct));

    /// <summary>Users see their own tickets; support agents see the queue.</summary>
    [HttpGet("tickets")]
    public async Task<ActionResult<PagedResult<TicketDto>>> List([FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default) => Ok(await support.ListAsync(status, page, pageSize, ct));

    [HttpGet("tickets/{id:guid}")]
    public async Task<ActionResult<TicketDto>> Get(Guid id, CancellationToken ct) => FromResult(await support.GetAsync(id, ct));

    [HttpPost("tickets/{id:guid}/messages")]
    public async Task<ActionResult<TicketDto>> Message(Guid id, TicketMessageRequest request, CancellationToken ct) =>
        FromResult(await support.AddMessageAsync(id, request.Body, ct));

    [HttpPatch("tickets/{id:guid}")]
    [Authorize(Policy = Policies.CanHandleSupport)]
    public async Task<ActionResult<TicketDto>> Update(Guid id, UpdateTicketRequest request, CancellationToken ct) => FromResult(await support.UpdateAsync(id, request, ct));

    [HttpPost("~/api/v{version:apiVersion}/reports")]
    public async Task<IActionResult> Report(CreateReportRequest request, CancellationToken ct) => FromResult(await support.FileReportAsync(request, ct));
}

[Route("api/v{version:apiVersion}/host")]
[Authorize(Policy = Policies.CanManageProperty)]
public sealed class HostController(IPropertyService properties, IReservationService reservations, IPaymentService payments, IReviewService reviews,
    IHostDashboardService dashboard) : ApiControllerBase
{
    [HttpGet("dashboard")]
    public async Task<ActionResult<HostDashboardDto>> Dashboard(CancellationToken ct) => Ok(await dashboard.GetAsync(ct));

    [HttpGet("properties")]
    public async Task<ActionResult<IReadOnlyList<HostPropertyListItemDto>>> Properties(CancellationToken ct) => Ok(await properties.ListForHostAsync(ct));

    [HttpGet("reservations")]
    public async Task<ActionResult<PagedResult<ReservationDto>>> Reservations([FromQuery] string? scope, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default) => Ok(await reservations.ListForHostAsync(scope, page, pageSize, ct));

    [HttpGet("earnings")]
    public async Task<ActionResult<EarningsSummaryDto>> Earnings([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        Ok(await payments.GetHostEarningsAsync(from, to, ct));

    [HttpGet("reviews")]
    public async Task<ActionResult<PagedResult<ReviewDto>>> Reviews([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await reviews.ListForHostAsync(page, pageSize, ct));
}

[Route("api/v{version:apiVersion}/admin")]
[Authorize(Policy = Policies.CanViewAdminDashboard)]
public sealed class AdminController(IAdminService admin, IReviewService reviews, IPaymentService payments) : ApiControllerBase
{
    [HttpGet("dashboard")]
    public async Task<ActionResult<AdminDashboardDto>> Dashboard(CancellationToken ct) => Ok(await admin.DashboardAsync(ct));

    [HttpGet("users")]
    [Authorize(Policy = Policies.CanManageUsers)]
    public async Task<ActionResult<PagedResult<AdminUserDto>>> Users([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        CancellationToken ct = default) => Ok(await admin.UsersAsync(search, page, pageSize, ct));

    [HttpPost("users/{id:guid}/suspend")]
    [Authorize(Policy = Policies.CanManageUsers)]
    public async Task<IActionResult> Suspend(Guid id, CancellationToken ct) => FromResult(await admin.SetUserSuspendedAsync(id, true, ct));

    [HttpPost("users/{id:guid}/reinstate")]
    [Authorize(Policy = Policies.CanManageUsers)]
    public async Task<IActionResult> Reinstate(Guid id, CancellationToken ct) => FromResult(await admin.SetUserSuspendedAsync(id, false, ct));

    [HttpPost("users/{id:guid}/roles/{role}")]
    [Authorize(Policy = Policies.CanManageUsers)]
    public async Task<IActionResult> GrantRole(Guid id, string role, CancellationToken ct) => FromResult(await admin.SetUserRoleAsync(id, role, ct));

    [HttpGet("properties")]
    public async Task<ActionResult<PagedResult<AdminPropertyDto>>> Properties([FromQuery] string? search, [FromQuery] string? status, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default) => Ok(await admin.PropertiesAsync(search, status, page, pageSize, ct));

    [HttpPost("properties/{id:guid}/suspend")]
    public async Task<IActionResult> SuspendProperty(Guid id, CancellationToken ct) => FromResult(await admin.SuspendPropertyAsync(id, ct));

    [HttpGet("reservations")]
    public async Task<ActionResult<PagedResult<ReservationDto>>> Reservations([FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        CancellationToken ct = default) => Ok(await admin.ReservationsAsync(status, page, pageSize, ct));

    [HttpGet("payments")]
    public async Task<ActionResult<PagedResult<PaymentDto>>> Payments([FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        CancellationToken ct = default) => Ok(await admin.PaymentsAsync(status, page, pageSize, ct));

    [HttpPost("payments/{id:guid}/refund")]
    [Authorize(Policy = Policies.CanProcessRefund)]
    [Idempotent("admin.refund")]
    public async Task<ActionResult<PaymentDto>> Refund(Guid id, RefundRequest request, CancellationToken ct) =>
        FromResult(await payments.RefundAsync(id, request.Amount, request.Reason, ct));

    [HttpGet("reviews")]
    [Authorize(Policy = Policies.CanModerateReview)]
    public async Task<ActionResult<PagedResult<ReviewDto>>> Reviews([FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        CancellationToken ct = default) => Ok(await reviews.ListAllAsync(status, page, pageSize, ct));

    [HttpPost("reviews/{id:guid}/moderate")]
    [Authorize(Policy = Policies.CanModerateReview)]
    public async Task<IActionResult> Moderate(Guid id, ModerateReviewRequest request, CancellationToken ct) => FromResult(await reviews.ModerateAsync(id, request, ct));

    [HttpGet("reports")]
    public async Task<ActionResult<PagedResult<ReportDto>>> Reports([FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        CancellationToken ct = default) => Ok(await admin.ReportsAsync(status, page, pageSize, ct));

    [HttpPost("reports/{id:guid}/resolve")]
    public async Task<IActionResult> ResolveReport(Guid id, [FromQuery] bool actioned, CancellationToken ct) => FromResult(await admin.ResolveReportAsync(id, actioned, ct));

    [HttpGet("fraud-alerts")]
    public async Task<ActionResult<PagedResult<FraudAlertDto>>> FraudAlerts([FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Ok(await admin.FraudAlertsAsync(page, pageSize, ct));

    [HttpPost("fraud-alerts/{id:guid}/acknowledge")]
    public async Task<IActionResult> Acknowledge(Guid id, CancellationToken ct) => FromResult(await admin.AcknowledgeFraudAlertAsync(id, ct));

    [HttpGet("audit")]
    [Authorize(Policy = Policies.CanViewAudit)]
    public async Task<ActionResult<PagedResult<AuditLogDto>>> Audit([FromQuery] string? action, [FromQuery] Guid? actorId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50, CancellationToken ct = default) => Ok(await admin.AuditAsync(action, actorId, page, pageSize, ct));

    [HttpGet("coupons")]
    public async Task<ActionResult<IReadOnlyList<CouponDto>>> Coupons(CancellationToken ct) => Ok(await admin.CouponsAsync(ct));

    [HttpPost("coupons")]
    public async Task<ActionResult<CouponDto>> CreateCoupon(CreateCouponRequest request, CancellationToken ct) => FromResult(await admin.CreateCouponAsync(request, ct));

    [HttpPost("coupons/{id:guid}/deactivate")]
    public async Task<IActionResult> DeactivateCoupon(Guid id, CancellationToken ct) => FromResult(await admin.DeactivateCouponAsync(id, ct));
}

[Route("api/v{version:apiVersion}/ai")]
[FeatureGate("AiAssistant")]
public sealed class AiController(IAssistantService assistant) : ApiControllerBase
{
    /// <summary>Chat with the travel assistant. Anonymous users can search; booking proposals require sign-in.</summary>
    [HttpPost("messages")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Ai)]
    public async Task<ActionResult<AssistantReplyDto>> Send(AssistantMessageRequest request, CancellationToken ct) => FromResult(await assistant.SendAsync(request, ct));

    /// <summary>Human-in-the-loop gate: executes (or rejects) an action the assistant proposed.</summary>
    [HttpPost("actions/{actionId:guid}/confirm")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Ai)]
    public async Task<ActionResult<ConfirmActionResultDto>> Confirm(Guid actionId, ConfirmActionRequest request, CancellationToken ct) =>
        FromResult(await assistant.ConfirmAsync(actionId, request.Approve, ct));
}

[Route("api/v{version:apiVersion}/features")]
[AllowAnonymous]
public sealed class FeaturesController(IVariantFeatureManager features) : ApiControllerBase
{
    private static readonly string[] Known = ["AiAssistant", "NewSearch", "DynamicPricing", "Experiences"];

    /// <summary>Feature flags for the SPA (UI hints only — the API enforces flags itself).</summary>
    [HttpGet]
    public async Task<ActionResult<Dictionary<string, bool>>> Get(CancellationToken ct)
    {
        var result = new Dictionary<string, bool>();
        foreach (var f in Known) result[f] = await features.IsEnabledAsync(f, ct);
        return Ok(result);
    }
}
