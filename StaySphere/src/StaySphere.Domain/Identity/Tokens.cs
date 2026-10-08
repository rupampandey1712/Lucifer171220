using StaySphere.Domain.Common;

namespace StaySphere.Domain.Identity;

/// <summary>
/// Long-lived refresh token. Only a SHA-256 hash is stored. Tokens rotate on every use; tokens issued from
/// the same login share a <see cref="FamilyId"/> so a replayed (already rotated) token revokes the whole family.
/// </summary>
public sealed class RefreshToken : Entity
{
    private RefreshToken() { }

    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = default!;
    public Guid FamilyId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public Guid? ReplacedByTokenId { get; private set; }
    public string? CreatedByIp { get; private set; }

    public static RefreshToken Issue(Guid userId, string tokenHash, Guid familyId, DateTimeOffset now, TimeSpan lifetime, string? ip) =>
        new()
        {
            UserId = userId,
            TokenHash = tokenHash,
            FamilyId = familyId,
            CreatedAt = now,
            ExpiresAt = now.Add(lifetime),
            CreatedByIp = ip,
        };

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    public void Revoke(DateTimeOffset now, Guid? replacedBy = null)
    {
        RevokedAt ??= now;
        ReplacedByTokenId ??= replacedBy;
    }
}

public enum UserTokenPurpose
{
    EmailVerification,
    PasswordReset,
}

/// <summary>Single-use, time-limited token for email verification / password reset (hash stored only).</summary>
public sealed class UserToken : Entity
{
    private UserToken() { }

    public Guid UserId { get; private set; }
    public UserTokenPurpose Purpose { get; private set; }
    public string TokenHash { get; private set; } = default!;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? UsedAt { get; private set; }

    public static UserToken Create(Guid userId, UserTokenPurpose purpose, string tokenHash, DateTimeOffset expiresAt) =>
        new() { UserId = userId, Purpose = purpose, TokenHash = tokenHash, ExpiresAt = expiresAt };

    public Result Use(DateTimeOffset now)
    {
        if (UsedAt is not null || ExpiresAt <= now)
            return Error.Validation("token.invalid", "This link is invalid or has expired.");
        UsedAt = now;
        return Result.Success();
    }
}
