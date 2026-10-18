using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StaySphere.Application.Abstractions;
using StaySphere.Application.Common;
using StaySphere.Application.Engagement;
using StaySphere.Contracts.Auth;
using StaySphere.Contracts;
using StaySphere.Domain.Common;
using StaySphere.Domain.Identity;

namespace StaySphere.Application.Identity;

public interface IHostVerificationService
{
    Task<Result<HostVerificationDto>> GetMineAsync(CancellationToken ct);
    Task<Result<HostVerificationDto>> RequestAsync(RequestHostVerificationRequest request, CancellationToken ct);
    Task<PagedResult<HostVerificationDto>> ListAsync(string? status, int page, int pageSize, CancellationToken ct);
    Task<Result<HostVerificationDto>> ReviewAsync(Guid userId, ReviewHostVerificationRequest request, CancellationToken ct);
    /// <summary>Starts an ID-document check at the identity provider and returns its hosted capture URL.</summary>
    Task<Result<IdentityCheckDto>> StartDocumentCheckAsync(CancellationToken ct);
    /// <summary>Reads the provider's verdict (when the host returns from the capture flow).</summary>
    Task<Result<HostVerificationDto>> RefreshDocumentCheckAsync(CancellationToken ct);
    /// <summary>Background safety net: settles document checks for hosts who never came back to the site.</summary>
    Task<int> ReconcileDocumentChecksAsync(CancellationToken ct);
}

public sealed class HostVerificationService(IAppDbContext db, ICurrentUser currentUser, IAuditLogger audit, INotificationService notifications,
    IIdentityVerificationProvider identity, IOptions<AppOptions> appOptions, TimeProvider clock, ILogger<HostVerificationService> logger)
    : IHostVerificationService
{
    /// <summary>Sessions the host abandons are given this long before the reconcile job stops polling and fails them.</summary>
    public static readonly TimeSpan AbandonedAfter = TimeSpan.FromDays(2);

    private HostVerificationDto ToDto(User u) => new(u.Id, u.DisplayName, u.Email, u.HostVerification.ToString(), u.HostVerificationLegalName,
        u.HostVerificationNote, u.HostVerificationSubmittedAt, u.HostVerifiedAt,
        u.HostVerification == HostVerificationStatus.None ? null : u.IdentitySessionId is null ? "Manual" : "Document", identity.Enabled);

    public async Task<Result<IdentityCheckDto>> StartDocumentCheckAsync(CancellationToken ct)
    {
        if (!identity.Enabled) return Error.Validation("verification.document_unavailable", "ID document checks aren't available; request a manual review instead.");
        var user = await db.Users.Include(u => u.Roles).FirstOrDefaultAsync(u => u.Id == currentUser.RequireUserId(), ct);
        if (user is null) return Error.NotFound("user.not_found", "User not found.");
        if (user.HostVerification == HostVerificationStatus.Verified) return Error.Conflict("verification.already_verified", "You are already verified.");

        var (sessionId, url) = await identity.CreateSessionAsync(user.Id, $"{appOptions.Value.PublicWebUrl.TrimEnd('/')}/host/verification?identity=return", ct);
        var started = user.StartIdentityCheck(sessionId, clock.GetUtcNow());
        if (started.IsFailure) return started.Error!;
        audit.Record("host.identity_check_started", nameof(User), user.Id.ToString(), new { sessionId });
        await db.SaveChangesAsync(ct);
        return new IdentityCheckDto(url);
    }

    public async Task<Result<HostVerificationDto>> RefreshDocumentCheckAsync(CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == currentUser.RequireUserId(), ct);
        if (user is null) return Error.NotFound("user.not_found", "User not found.");
        if (user.HostVerification == HostVerificationStatus.Pending && user.IdentitySessionId is not null)
            await SettleAsync(user, abandonIfPending: false, ct);
        return ToDto(user);
    }

    public async Task<int> ReconcileDocumentChecksAsync(CancellationToken ct)
    {
        if (!identity.Enabled) return 0;
        var pending = await db.Users.Where(u => u.HostVerification == HostVerificationStatus.Pending && u.IdentitySessionId != null)
            .OrderBy(u => u.HostVerificationSubmittedAt).Take(50).ToListAsync(ct);
        var settled = 0;
        foreach (var user in pending)
        {
            try
            {
                var abandoned = user.HostVerificationSubmittedAt < clock.GetUtcNow() - AbandonedAfter;
                if (await SettleAsync(user, abandoned, ct)) settled++;
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "Identity session {SessionId} could not be checked; will retry", user.IdentitySessionId);
            }
        }

        return settled;
    }

    /// <summary>Applies the provider's verdict. Returns true when the check reached a final state.</summary>
    private async Task<bool> SettleAsync(User user, bool abandonIfPending, CancellationToken ct)
    {
        var session = await identity.GetSessionAsync(user.IdentitySessionId!, ct);
        if (session.Status == IdentitySessionStatus.Pending && !abandonIfPending) return false;

        var verified = session.Status == IdentitySessionStatus.Verified;
        var reason = session.Status == IdentitySessionStatus.Pending ? "The ID check wasn't completed. You can start a new one." : session.FailureReason;
        var completed = user.CompleteIdentityCheck(session.SessionId, verified, session.VerifiedName, reason, clock.GetUtcNow());
        if (completed.IsFailure) return false;

        audit.Record(verified ? "host.verification_approved" : "host.verification_rejected", nameof(User), user.Id.ToString(),
            new { method = "document", reason });
        await db.SaveChangesAsync(ct);
        await notifications.NotifyAsync(user.Id, verified ? "host.verified" : "host.verification_rejected",
            verified ? "You're a verified host" : "Your ID check didn't go through",
            verified ? "Your ID document was verified. Guests now see a Verified host badge on your listings." : $"{reason} You can try again or ask for a manual review.",
            "/host/verification", ct, email: true);
        return true;
    }

    public async Task<Result<HostVerificationDto>> GetMineAsync(CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUser.RequireUserId(), ct);
        return user is null ? Error.NotFound("user.not_found", "User not found.") : ToDto(user);
    }

    public async Task<Result<HostVerificationDto>> RequestAsync(RequestHostVerificationRequest request, CancellationToken ct)
    {
        var user = await db.Users.Include(u => u.Roles).FirstOrDefaultAsync(u => u.Id == currentUser.RequireUserId(), ct);
        if (user is null) return Error.NotFound("user.not_found", "User not found.");
        var result = user.RequestHostVerification(request.LegalName, request.Note, clock.GetUtcNow());
        if (result.IsFailure) return result.Error!;
        audit.Record("host.verification_requested", nameof(User), user.Id.ToString());
        await db.SaveChangesAsync(ct);
        return ToDto(user);
    }

    public async Task<PagedResult<HostVerificationDto>> ListAsync(string? status, int page, int pageSize, CancellationToken ct)
    {
        (page, pageSize) = Paging.Normalize(page, pageSize, 100);
        var wanted = Enum.TryParse<HostVerificationStatus>(status, true, out var s) ? s : HostVerificationStatus.Pending;
        var q = db.Users.AsNoTracking().Where(u => u.HostVerification == wanted);
        var total = await q.CountAsync(ct);
        var users = await q.OrderBy(u => u.HostVerificationSubmittedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<HostVerificationDto>(users.Select(ToDto).ToList(), page, pageSize, total);
    }

    public async Task<Result<HostVerificationDto>> ReviewAsync(Guid userId, ReviewHostVerificationRequest request, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return Error.NotFound("user.not_found", "User not found.");
        var result = user.ReviewHostVerification(request.Approve, request.Reason, clock.GetUtcNow());
        if (result.IsFailure) return result.Error!;
        audit.Record(request.Approve ? "host.verification_approved" : "host.verification_rejected", nameof(User), user.Id.ToString(),
            new { reason = request.Reason });
        await db.SaveChangesAsync(ct);
        await notifications.NotifyAsync(user.Id, request.Approve ? "host.verified" : "host.verification_rejected",
            request.Approve ? "You're a verified host" : "Verification needs another look",
            request.Approve ? "Guests now see a Verified host badge on your listings." : $"Your verification request was not approved: {request.Reason}",
            "/host/verification", ct, email: true);
        return ToDto(user);
    }
}
