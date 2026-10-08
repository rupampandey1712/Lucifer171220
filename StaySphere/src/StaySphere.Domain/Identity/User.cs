using StaySphere.Domain.Common;
using StaySphere.Domain.ValueObjects;

namespace StaySphere.Domain.Identity;

public static class Roles
{
    public const string Guest = "Guest";
    public const string Host = "Host";
    public const string Admin = "Admin";
    public const string Support = "Support";

    public static readonly IReadOnlyList<string> All = [Guest, Host, Admin, Support];
}

public enum UserStatus
{
    Active,
    Suspended,
    Deleted,
}

public sealed class User : AggregateRoot
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly List<UserRole> _roles = [];

    private User() { }

    public string Email { get; private set; } = default!;
    public string NormalizedEmail { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;
    public string DisplayName { get; private set; } = default!;
    public string? Bio { get; private set; }
    public string? AvatarUrl { get; private set; }
    public string PreferredCurrency { get; private set; } = "USD";
    public bool EmailConfirmed { get; private set; }
    public UserStatus Status { get; private set; } = UserStatus.Active;
    public int AccessFailedCount { get; private set; }
    public DateTimeOffset? LockoutEnd { get; private set; }
    public string SecurityStamp { get; private set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset? LastLoginAt { get; private set; }
    public IReadOnlyCollection<UserRole> Roles => _roles;

    public static User Register(EmailAddress email, string displayName, string passwordHash, DateTimeOffset now)
    {
        var user = new User
        {
            Email = email.Value,
            NormalizedEmail = email.Normalized,
            DisplayName = displayName.Trim(),
            PasswordHash = passwordHash,
            CreatedAt = now,
            UpdatedAt = now,
        };
        user._roles.Add(new UserRole(user.Id, Identity.Roles.Guest));
        user.Raise(new UserRegisteredDomainEvent(user.Id, user.Email, user.DisplayName));
        return user;
    }

    public bool IsInRole(string role) => _roles.Any(r => r.Role == role);

    public void AddRole(string role)
    {
        if (!Identity.Roles.All.Contains(role))
            throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown role.");
        if (!IsInRole(role))
            _roles.Add(new UserRole(Id, role));
    }

    public bool IsLockedOut(DateTimeOffset now) => LockoutEnd is { } end && end > now;

    public bool CanSignIn(DateTimeOffset now) => Status == UserStatus.Active && !IsLockedOut(now);

    public void RecordFailedLogin(DateTimeOffset now)
    {
        AccessFailedCount++;
        if (AccessFailedCount >= MaxFailedAttempts)
        {
            LockoutEnd = now.Add(LockoutDuration);
            AccessFailedCount = 0;
        }
    }

    public void RecordSuccessfulLogin(DateTimeOffset now)
    {
        AccessFailedCount = 0;
        LockoutEnd = null;
        LastLoginAt = now;
    }

    public void ConfirmEmail()
    {
        if (EmailConfirmed) return;
        EmailConfirmed = true;
        Raise(new EmailVerifiedDomainEvent(Id, Email));
    }

    public void ChangePassword(string newHash)
    {
        PasswordHash = newHash;
        RotateSecurityStamp();
    }

    public void UpdateProfile(string displayName, string? bio, string preferredCurrency)
    {
        DisplayName = displayName.Trim();
        Bio = bio?.Trim();
        PreferredCurrency = preferredCurrency.ToUpperInvariant();
    }

    public void SetAvatar(string url) => AvatarUrl = url;

    public void Suspend()
    {
        Status = UserStatus.Suspended;
        RotateSecurityStamp();
        Raise(new UserSuspendedDomainEvent(Id));
    }

    public void Reinstate() => Status = UserStatus.Active;

    /// <summary>GDPR-style erasure: removes personal data but keeps the row so reviews/bookings stay consistent.</summary>
    public void Anonymize(string anonymizedPasswordHash)
    {
        Email = $"deleted-{Id:N}@anonymized.invalid";
        NormalizedEmail = Email.ToUpperInvariant();
        DisplayName = "Former user";
        Bio = null;
        AvatarUrl = null;
        PasswordHash = anonymizedPasswordHash;
        Status = UserStatus.Deleted;
        RotateSecurityStamp();
    }

    private void RotateSecurityStamp() => SecurityStamp = Guid.NewGuid().ToString("N");
}

public sealed class UserRole
{
    private UserRole() { }

    public UserRole(Guid userId, string role)
    {
        UserId = userId;
        Role = role;
    }

    public Guid UserId { get; private set; }
    public string Role { get; private set; } = default!;
}

public sealed record UserRegisteredDomainEvent(Guid UserId, string Email, string DisplayName) : IDomainEvent;
public sealed record EmailVerifiedDomainEvent(Guid UserId, string Email) : IDomainEvent;
public sealed record UserSuspendedDomainEvent(Guid UserId) : IDomainEvent;
