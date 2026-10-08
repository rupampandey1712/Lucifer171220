namespace StaySphere.Contracts.Auth;

public sealed record RegisterRequest(string Email, string Password, string DisplayName);
public sealed record LoginRequest(string Email, string Password);
public sealed record VerifyEmailRequest(Guid UserId, string Token);
public sealed record ResendVerificationRequest(string Email);
public sealed record ForgotPasswordRequest(string Email);
public sealed record ResetPasswordRequest(Guid UserId, string Token, string NewPassword);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record UserDto(Guid Id, string Email, string DisplayName, string? AvatarUrl, string? Bio, string PreferredCurrency,
    bool EmailConfirmed, IReadOnlyList<string> Roles, DateTimeOffset CreatedAt);

public sealed record AuthResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt, UserDto User);

/// <summary>Internal: the refresh token is never serialized to the body; the API writes it into an HttpOnly cookie.</summary>
public sealed record AuthResult(AuthResponse Response, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);

public sealed record UpdateProfileRequest(string DisplayName, string? Bio, string PreferredCurrency);
