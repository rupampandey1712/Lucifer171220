using Microsoft.EntityFrameworkCore;
using StaySphere.Application.Abstractions;
using StaySphere.Application.Common;
using StaySphere.Contracts.Auth;
using StaySphere.Domain.Common;
using StaySphere.Domain.Identity;

namespace StaySphere.Application.Identity;

public interface IConsentService
{
    /// <summary>The current state of every purpose for the signed-in user.</summary>
    Task<IReadOnlyList<ConsentDto>> GetMineAsync(CancellationToken ct);
    Task<Result<ConsentDto>> SetAsync(string purpose, bool granted, CancellationToken ct);
}

public sealed class ConsentService(IAppDbContext db, ICurrentUser currentUser, IAuditLogger audit, TimeProvider clock) : IConsentService
{
    public async Task<IReadOnlyList<ConsentDto>> GetMineAsync(CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var latest = (await db.ConsentRecords.AsNoTracking().Where(c => c.UserId == userId).ToListAsync(ct))
            .GroupBy(c => c.Purpose)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.RecordedAt).ThenByDescending(c => c.Id).First());
        return Enum.GetValues<ConsentPurpose>().Select(p => latest.TryGetValue(p, out var r)
            ? new ConsentDto(p.ToString(), r.Granted, ConsentRecord.IsRequired(p), r.PolicyVersion, r.RecordedAt)
            : new ConsentDto(p.ToString(), false, ConsentRecord.IsRequired(p), ConsentRecord.CurrentPolicyVersion, null)).ToList();
    }

    public async Task<Result<ConsentDto>> SetAsync(string purpose, bool granted, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        if (!Enum.TryParse<ConsentPurpose>(purpose, ignoreCase: true, out var p) || !Enum.IsDefined(p))
            return Error.NotFound("consent.unknown", "Unknown consent.");
        if (ConsentRecord.IsRequired(p) && !granted)
            return Error.Validation("consent.required", "The Terms and Privacy Policy are required to use StaySphere. To withdraw, delete your account.");

        var record = ConsentRecord.Record(userId, p, granted, currentUser.IpAddress, clock.GetUtcNow());
        db.ConsentRecords.Add(record);
        audit.Record(granted ? "consent.granted" : "consent.withdrawn", "Consent", userId.ToString(), new { purpose = p.ToString() });
        await db.SaveChangesAsync(ct);
        return new ConsentDto(p.ToString(), granted, ConsentRecord.IsRequired(p), record.PolicyVersion, record.RecordedAt);
    }
}
