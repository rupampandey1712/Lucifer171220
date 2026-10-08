using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StaySphere.Application.Abstractions;
using StaySphere.Application.Common;
using StaySphere.Contracts.Auth;
using StaySphere.Domain.Common;
using StaySphere.Domain.Identity;
using StaySphere.Domain.ValueObjects;

namespace StaySphere.Application.Identity;

public sealed class AppOptions
{
    public const string Section = "App";
    public string PublicWebUrl { get; set; } = "http://localhost:5173";
    public int RefreshTokenDays { get; set; } = 14;
}

public interface IAuthService
{
    Task<Result<AuthResult>> RegisterAsync(RegisterRequest request, CancellationToken ct);
    Task<Result<AuthResult>> LoginAsync(LoginRequest request, CancellationToken ct);
    Task<Result<AuthResult>> RefreshAsync(string refreshToken, CancellationToken ct);
    Task LogoutAsync(string? refreshToken, CancellationToken ct);
    Task<Result> VerifyEmailAsync(VerifyEmailRequest request, CancellationToken ct);
    Task ResendVerificationAsync(string email, CancellationToken ct);
    Task ForgotPasswordAsync(string email, CancellationToken ct);
    Task<Result> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct);
    Task<Result> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct);
    Task<Result<AuthResult>> IssueForCurrentUserAsync(CancellationToken ct);
}

public sealed class AuthService(
    IAppDbContext db,
    IPasswordHasher hasher,
    IJwtTokenService jwt,
    IEmailSender email,
    IAuditLogger audit,
    ICurrentUser currentUser,
    TimeProvider clock,
    IOptions<AppOptions> options,
    ILogger<AuthService> logger) : IAuthService
{
    private static readonly Error InvalidCredentials = Error.Unauthorized("auth.invalid_credentials", "Email or password is incorrect.");
    private static readonly Error InvalidRefresh = Error.Unauthorized("auth.invalid_refresh", "Your session has expired. Please sign in again.");

    // Verifying against a fixed hash when the user does not exist keeps response timing similar (no user enumeration).
    private readonly Lazy<string> _dummyHash = new(() => hasher.Hash(Guid.NewGuid().ToString()));

    public async Task<Result<AuthResult>> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var emailResult = EmailAddress.Create(request.Email);
        if (emailResult.IsFailure) return emailResult.Error!;
        var address = emailResult.Value;

        if (await db.Users.AnyAsync(u => u.NormalizedEmail == address.Normalized, ct))
            return Error.Conflict("auth.email_taken", "An account with this email already exists.");

        var now = clock.GetUtcNow();
        var user = User.Register(address, request.DisplayName, hasher.Hash(request.Password), now);
        db.Users.Add(user);
        var rawToken = CreateUserToken(user.Id, UserTokenPurpose.EmailVerification, TimeSpan.FromDays(2));
        audit.Record("user.registered", nameof(User), user.Id.ToString(), actorOverride: user.Id);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (db.IsUniqueViolation(ex))
        {
            return Error.Conflict("auth.email_taken", "An account with this email already exists.");
        }

        await SendVerificationEmailAsync(user, rawToken, ct);
        return await IssueTokensAsync(user, Guid.CreateVersion7(), ct);
    }

    public async Task<Result<AuthResult>> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var normalized = request.Email.Trim().ToUpperInvariant();
        var user = await db.Users.Include(u => u.Roles).FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);
        var now = clock.GetUtcNow();

        if (user is null)
        {
            hasher.Verify(_dummyHash.Value, request.Password);
            return InvalidCredentials;
        }

        if (user.IsLockedOut(now))
            return new Error("auth.locked_out", "Too many failed attempts. Try again in a few minutes.", ErrorType.TooManyRequests);

        if (!hasher.Verify(user.PasswordHash, request.Password))
        {
            user.RecordFailedLogin(now);
            audit.Record("auth.login_failed", nameof(User), user.Id.ToString(), actorOverride: user.Id);
            await db.SaveChangesAsync(ct);
            return InvalidCredentials;
        }

        if (user.Status != UserStatus.Active)
            return Error.Forbidden("auth.suspended", "This account is not active. Please contact support.");

        user.RecordSuccessfulLogin(now);
        audit.Record("auth.login", nameof(User), user.Id.ToString(), actorOverride: user.Id);
        return await IssueTokensAsync(user, Guid.CreateVersion7(), ct);
    }

    public async Task<Result<AuthResult>> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return InvalidRefresh;
        var hash = TokenHasher.Hash(refreshToken);
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        var now = clock.GetUtcNow();
        if (token is null) return InvalidRefresh;

        if (token.RevokedAt is not null)
        {
            // Reuse of a rotated token: assume theft and revoke every token in the family.
            await db.RefreshTokens.Where(t => t.FamilyId == token.FamilyId && t.RevokedAt == null)
                .ForEachAsync(t => t.Revoke(now), ct);
            audit.Record("auth.refresh_reuse_detected", nameof(User), token.UserId.ToString(), actorOverride: token.UserId);
            await db.SaveChangesAsync(ct);
            logger.LogWarning("Refresh token reuse detected for user {UserId}; token family revoked", token.UserId);
            return InvalidRefresh;
        }

        if (!token.IsActive(now)) return InvalidRefresh;

        var user = await db.Users.Include(u => u.Roles).FirstOrDefaultAsync(u => u.Id == token.UserId, ct);
        if (user is null || user.Status != UserStatus.Active) return InvalidRefresh;

        return await IssueTokensAsync(user, token.FamilyId, ct, rotating: token);
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return;
        var hash = TokenHasher.Hash(refreshToken);
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null) return;
        token.Revoke(clock.GetUtcNow());
        audit.Record("auth.logout", nameof(User), token.UserId.ToString(), actorOverride: token.UserId);
        await db.SaveChangesAsync(ct);
    }

    public async Task<Result> VerifyEmailAsync(VerifyEmailRequest request, CancellationToken ct)
    {
        var result = await ConsumeTokenAsync(request.UserId, UserTokenPurpose.EmailVerification, request.Token, ct);
        if (result.IsFailure) return result.Error!;
        var user = await db.Users.FirstAsync(u => u.Id == request.UserId, ct);
        user.ConfirmEmail();
        audit.Record("user.email_verified", nameof(User), user.Id.ToString(), actorOverride: user.Id);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task ResendVerificationAsync(string emailAddress, CancellationToken ct)
    {
        var normalized = emailAddress.Trim().ToUpperInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);
        if (user is null || user.EmailConfirmed) return;
        var raw = CreateUserToken(user.Id, UserTokenPurpose.EmailVerification, TimeSpan.FromDays(2));
        await db.SaveChangesAsync(ct);
        await SendVerificationEmailAsync(user, raw, ct);
    }

    public async Task ForgotPasswordAsync(string emailAddress, CancellationToken ct)
    {
        var normalized = emailAddress.Trim().ToUpperInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalized && u.Status == UserStatus.Active, ct);
        if (user is null) return; // Always succeed: no account enumeration.

        var raw = CreateUserToken(user.Id, UserTokenPurpose.PasswordReset, TimeSpan.FromHours(1));
        audit.Record("auth.password_reset_requested", nameof(User), user.Id.ToString(), actorOverride: user.Id);
        await db.SaveChangesAsync(ct);

        var link = $"{options.Value.PublicWebUrl}/reset-password?userId={user.Id}&token={WebUtility.UrlEncode(raw)}";
        await TrySendAsync(new EmailMessage(user.Email, "Reset your StaySphere password",
            EmailTemplates.Layout("Reset your password",
                $"<p>Hi {WebUtility.HtmlEncode(user.DisplayName)},</p><p>Use the button below to choose a new password. The link expires in 1 hour.</p>",
                link, "Reset password")), ct);
    }

    public async Task<Result> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct)
    {
        var result = await ConsumeTokenAsync(request.UserId, UserTokenPurpose.PasswordReset, request.Token, ct);
        if (result.IsFailure) return result.Error!;
        var user = await db.Users.FirstAsync(u => u.Id == request.UserId, ct);
        user.ChangePassword(hasher.Hash(request.NewPassword));
        await RevokeAllRefreshTokensAsync(user.Id, ct);
        audit.Record("auth.password_reset", nameof(User), user.Id.ToString(), actorOverride: user.Id);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
        if (!hasher.Verify(user.PasswordHash, request.CurrentPassword))
            return Error.Validation("auth.wrong_password", "Your current password is incorrect.");
        user.ChangePassword(hasher.Hash(request.NewPassword));
        await RevokeAllRefreshTokensAsync(user.Id, ct);
        audit.Record("auth.password_changed", nameof(User), user.Id.ToString());
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<AuthResult>> IssueForCurrentUserAsync(CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var user = await db.Users.Include(u => u.Roles).FirstAsync(u => u.Id == userId, ct);
        return await IssueTokensAsync(user, Guid.CreateVersion7(), ct);
    }

    private async Task<Result<AuthResult>> IssueTokensAsync(User user, Guid familyId, CancellationToken ct, RefreshToken? rotating = null)
    {
        var now = clock.GetUtcNow();
        var raw = TokenHasher.GenerateToken(48);
        var refresh = RefreshToken.Issue(user.Id, TokenHasher.Hash(raw), familyId, now,
            TimeSpan.FromDays(options.Value.RefreshTokenDays), currentUser.IpAddress);
        db.RefreshTokens.Add(refresh);
        rotating?.Revoke(now, refresh.Id);
        await db.SaveChangesAsync(ct);

        var (access, expires) = jwt.CreateAccessToken(user);
        return new AuthResult(new AuthResponse(access, expires, UserMapper.ToDto(user)), raw, refresh.ExpiresAt);
    }

    private string CreateUserToken(Guid userId, UserTokenPurpose purpose, TimeSpan lifetime)
    {
        var raw = TokenHasher.GenerateToken();
        db.UserTokens.Add(UserToken.Create(userId, purpose, TokenHasher.Hash(raw), clock.GetUtcNow().Add(lifetime)));
        return raw;
    }

    private async Task<Result> ConsumeTokenAsync(Guid userId, UserTokenPurpose purpose, string rawToken, CancellationToken ct)
    {
        var hash = TokenHasher.Hash(rawToken ?? string.Empty);
        var token = await db.UserTokens.FirstOrDefaultAsync(t => t.UserId == userId && t.Purpose == purpose && t.TokenHash == hash, ct);
        return token is null
            ? Error.Validation("token.invalid", "This link is invalid or has expired.")
            : token.Use(clock.GetUtcNow());
    }

    private Task RevokeAllRefreshTokensAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        return db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ForEachAsync(t => t.Revoke(now), ct);
    }

    private Task SendVerificationEmailAsync(User user, string rawToken, CancellationToken ct)
    {
        var link = $"{options.Value.PublicWebUrl}/verify-email?userId={user.Id}&token={WebUtility.UrlEncode(rawToken)}";
        return TrySendAsync(new EmailMessage(user.Email, "Confirm your StaySphere email",
            EmailTemplates.Layout("Welcome to StaySphere",
                $"<p>Hi {WebUtility.HtmlEncode(user.DisplayName)},</p><p>Please confirm your email address to finish setting up your account.</p>",
                link, "Confirm email")), ct);
    }

    private async Task TrySendAsync(EmailMessage message, CancellationToken ct)
    {
        try
        {
            await email.SendAsync(message, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Email is best-effort here; the user can request another link.
            logger.LogWarning(ex, "Failed to send {Subject} email", message.Subject);
        }
    }
}

public static class UserMapper
{
    public static UserDto ToDto(User user) =>
        new(user.Id, user.Email, user.DisplayName, user.AvatarUrl, user.Bio, user.PreferredCurrency, user.EmailConfirmed,
            user.Roles.Select(r => r.Role).OrderBy(r => r).ToList(), user.CreatedAt);
}

public static class EmailTemplates
{
    public static string Layout(string heading, string bodyHtml, string? actionUrl = null, string? actionText = null)
    {
        var button = actionUrl is null
            ? string.Empty
            : $"""<p style="margin:28px 0"><a href="{WebUtility.HtmlEncode(actionUrl)}" style="background:#0f766e;color:#fff;padding:12px 22px;border-radius:10px;text-decoration:none;font-weight:600">{WebUtility.HtmlEncode(actionText)}</a></p>""";
        return $"""
            <div style="font-family:Inter,Segoe UI,Arial,sans-serif;max-width:560px;margin:0 auto;padding:24px;color:#0f172a">
              <div style="font-size:22px;font-weight:700;color:#0f766e;margin-bottom:24px">StaySphere</div>
              <h1 style="font-size:20px;margin:0 0 16px">{WebUtility.HtmlEncode(heading)}</h1>
              {bodyHtml}
              {button}
              <p style="color:#64748b;font-size:12px;margin-top:32px">You are receiving this email because of activity on your StaySphere account.</p>
            </div>
            """;
    }
}
