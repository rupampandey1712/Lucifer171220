using Microsoft.EntityFrameworkCore;
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
}

public sealed class HostVerificationService(IAppDbContext db, ICurrentUser currentUser, IAuditLogger audit, INotificationService notifications,
    TimeProvider clock) : IHostVerificationService
{
    private static HostVerificationDto ToDto(User u) => new(u.Id, u.DisplayName, u.Email, u.HostVerification.ToString(), u.HostVerificationLegalName,
        u.HostVerificationNote, u.HostVerificationSubmittedAt, u.HostVerifiedAt);

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
