using Microsoft.EntityFrameworkCore;
using StaySphere.Application.Abstractions;
using StaySphere.Application.Common;
using StaySphere.Contracts.Auth;
using StaySphere.Domain.Common;
using StaySphere.Domain.Identity;

namespace StaySphere.Application.Identity;

public interface IProfileService
{
    Task<UserDto?> GetMeAsync(CancellationToken ct);
    Task<Result<UserDto>> UpdateAsync(UpdateProfileRequest request, CancellationToken ct);
    Task<Result<UserDto>> SetAvatarAsync(Stream content, CancellationToken ct);
    Task<Result> BecomeHostAsync(CancellationToken ct);
    Task<object> ExportAsync(CancellationToken ct);
    Task<Result> DeleteAccountAsync(CancellationToken ct);
}

public sealed class ProfileService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IImageProcessor images,
    IFileStorageService storage,
    IPasswordHasher hasher,
    IAuditLogger audit,
    TimeProvider clock) : IProfileService
{
    public async Task<UserDto?> GetMeAsync(CancellationToken ct)
    {
        var id = currentUser.RequireUserId();
        var user = await db.Users.AsNoTracking().Include(u => u.Roles).FirstOrDefaultAsync(u => u.Id == id, ct);
        return user is null ? null : UserMapper.ToDto(user);
    }

    public async Task<Result<UserDto>> UpdateAsync(UpdateProfileRequest request, CancellationToken ct)
    {
        var id = currentUser.RequireUserId();
        var user = await db.Users.Include(u => u.Roles).FirstAsync(u => u.Id == id, ct);
        user.UpdateProfile(request.DisplayName, request.Bio, request.PreferredCurrency);
        await db.SaveChangesAsync(ct);
        return UserMapper.ToDto(user);
    }

    public async Task<Result<UserDto>> SetAvatarAsync(Stream content, CancellationToken ct)
    {
        var processed = await images.ProcessAsync(content, ct);
        if (processed.IsFailure) return processed.Error!;
        var id = currentUser.RequireUserId();
        var key = $"avatars/{id:N}/{Guid.NewGuid():N}{processed.Value.Extension}";
        await using var stream = new MemoryStream(processed.Value.Thumbnail);
        var stored = await storage.UploadAsync("public", key, stream, processed.Value.ContentType, ct);
        var user = await db.Users.Include(u => u.Roles).FirstAsync(u => u.Id == id, ct);
        user.SetAvatar(stored.Url);
        await db.SaveChangesAsync(ct);
        return UserMapper.ToDto(user);
    }

    public async Task<Result> BecomeHostAsync(CancellationToken ct)
    {
        var id = currentUser.RequireUserId();
        var user = await db.Users.Include(u => u.Roles).FirstAsync(u => u.Id == id, ct);
        if (user.Status != UserStatus.Active) return Error.Forbidden("user.inactive", "Your account is not active.");
        user.AddRole(Roles.Host);
        audit.Record("user.became_host", nameof(User), id.ToString());
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Personal data export (GDPR Art. 15/20) — only the requesting user's own data.</summary>
    public async Task<object> ExportAsync(CancellationToken ct)
    {
        var id = currentUser.RequireUserId();
        var user = await db.Users.AsNoTracking().Include(u => u.Roles).FirstAsync(u => u.Id == id, ct);
        var reservations = await db.Reservations.AsNoTracking().Where(r => r.GuestId == id)
            .Select(r => new { r.Id, r.PropertyId, r.CheckIn, r.CheckOut, r.Guests, r.TotalAmount, r.Currency, Status = r.Status.ToString(), r.CreatedAt })
            .ToListAsync(ct);
        var reviews = await db.Reviews.AsNoTracking().Where(r => r.GuestId == id)
            .Select(r => new { r.Id, r.PropertyId, r.Overall, r.Comment, r.CreatedAt }).ToListAsync(ct);
        var messages = await db.Messages.AsNoTracking().Where(m => m.SenderId == id)
            .Select(m => new { m.Id, m.ConversationId, m.Body, m.SentAt }).ToListAsync(ct);
        var favorites = await db.Favorites.AsNoTracking().Where(f => f.UserId == id).Select(f => new { f.PropertyId, f.CreatedAt }).ToListAsync(ct);
        audit.Record("user.data_exported", nameof(User), id.ToString());
        await db.SaveChangesAsync(ct);
        return new { exportedAt = clock.GetUtcNow(), profile = UserMapper.ToDto(user), reservations, reviews, messages, favorites };
    }

    public async Task<Result> DeleteAccountAsync(CancellationToken ct)
    {
        var id = currentUser.RequireUserId();
        var hasActiveStays = await db.Reservations.AnyAsync(r => (r.GuestId == id || r.HostId == id) &&
            r.Status == Domain.Booking.ReservationStatus.Confirmed, ct);
        if (hasActiveStays)
            return Error.Conflict("user.active_reservations", "Please cancel or complete your upcoming reservations first.");

        var user = await db.Users.FirstAsync(u => u.Id == id, ct);
        user.Anonymize(hasher.Hash(TokenHasher.GenerateToken()));
        var now = clock.GetUtcNow();
        await db.RefreshTokens.Where(t => t.UserId == id && t.RevokedAt == null).ForEachAsync(t => t.Revoke(now), ct);
        await db.Favorites.Where(f => f.UserId == id).ExecuteDeleteAsync(ct);
        await db.Properties.Where(p => p.HostId == id && p.DeletedAt == null).ForEachAsync(p => p.SoftDelete(now), ct);
        audit.Record("user.deleted", nameof(User), id.ToString());
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
