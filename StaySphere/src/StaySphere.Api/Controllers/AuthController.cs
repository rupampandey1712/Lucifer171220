using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StaySphere.Api.Common;
using StaySphere.Application.Identity;
using StaySphere.Contracts.Auth;
using StaySphere.Domain.Common;

namespace StaySphere.Api.Controllers;

/// <summary>
/// Registration, login and session management. Access tokens (short-lived JWT) are returned in the body and kept in
/// memory by the SPA; the rotating refresh token travels only in an HttpOnly, Secure, SameSite=Strict cookie scoped to
/// this controller's path, and refresh/logout additionally require the X-Requested-With header (CSRF defence).
/// </summary>
[Route("api/v{version:apiVersion}/auth")]
public sealed class AuthController(IAuthService auth, IWebHostEnvironment env) : ApiControllerBase
{
    public const string RefreshCookie = "ss_rt";
    public const string CsrfHeader = "X-Requested-With";

    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.AuthStrict)]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken ct) =>
        WithCookie(await auth.RegisterAsync(request, ct));

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.AuthStrict)]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct) =>
        WithCookie(await auth.LoginAsync(request, ct));

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AuthResponse>> Refresh(CancellationToken ct)
    {
        if (!Request.Headers.ContainsKey(CsrfHeader))
            return Problem(Error.Forbidden("auth.csrf", "Missing anti-forgery header."));
        var result = await auth.RefreshAsync(Request.Cookies[RefreshCookie] ?? string.Empty, ct);
        if (result.IsFailure) Response.Cookies.Delete(RefreshCookie, CookieOptions(DateTimeOffset.UnixEpoch));
        return WithCookie(result);
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (!Request.Headers.ContainsKey(CsrfHeader))
            return Problem(Error.Forbidden("auth.csrf", "Missing anti-forgery header."));
        await auth.LogoutAsync(Request.Cookies[RefreshCookie], ct);
        Response.Cookies.Delete(RefreshCookie, CookieOptions(DateTimeOffset.UnixEpoch));
        return NoContent();
    }

    [HttpPost("verify-email")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> VerifyEmail(VerifyEmailRequest request, CancellationToken ct) =>
        FromResult(await auth.VerifyEmailAsync(request, ct));

    [HttpPost("resend-verification")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.AuthStrict)]
    public async Task<IActionResult> ResendVerification(ResendVerificationRequest request, CancellationToken ct)
    {
        await auth.ResendVerificationAsync(request.Email, ct);
        return Accepted();
    }

    /// <summary>Always 202 — whether or not the email exists (no account enumeration).</summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.AuthStrict)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken ct)
    {
        await auth.ForgotPasswordAsync(request.Email, ct);
        return Accepted();
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.AuthStrict)]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct) =>
        FromResult(await auth.ResetPasswordAsync(request, ct));

    [HttpPost("change-password")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct) =>
        FromResult(await auth.ChangePasswordAsync(request, ct));

    private ActionResult<AuthResponse> WithCookie(Result<AuthResult> result)
    {
        if (result.IsFailure) return Problem(result.Error!);
        Response.Cookies.Append(RefreshCookie, result.Value.RefreshToken, CookieOptions(result.Value.RefreshTokenExpiresAt));
        return Ok(result.Value.Response);
    }

    internal CookieOptions CookieOptions(DateTimeOffset expires) => new()
    {
        HttpOnly = true,
        Secure = !env.IsEnvironment("Testing"),
        SameSite = SameSiteMode.Strict,
        Path = "/api/v1/auth",
        Expires = expires,
        IsEssential = true,
    };
}
