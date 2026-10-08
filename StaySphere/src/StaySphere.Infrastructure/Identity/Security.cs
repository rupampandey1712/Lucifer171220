using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using StaySphere.Application.Abstractions;
using StaySphere.Application.Common;
using StaySphere.Domain.Identity;
using IPasswordHasher = StaySphere.Application.Abstractions.IPasswordHasher;

namespace StaySphere.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string Section = "Jwt";
    public string Issuer { get; set; } = "staysphere";
    public string Audience { get; set; } = "staysphere-web";
    /// <summary>HMAC signing key (≥ 32 bytes). Comes from user-secrets/env locally and Key Vault in Azure.</summary>
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 15;
}

public sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider clock) : IJwtTokenService
{
    public (string Token, DateTimeOffset ExpiresAt) CreateAccessToken(User user)
    {
        var o = options.Value;
        var now = clock.GetUtcNow();
        var expires = now.AddMinutes(o.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(JwtRegisteredClaimNames.Name, user.DisplayName),
            new("sstamp", user.SecurityStamp),
        };
        claims.AddRange(user.Roles.Select(r => new Claim(ClaimTypes.Role, r.Role)));

        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.SigningKey)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(o.Issuer, o.Audience, claims, now.UtcDateTime, expires.UtcDateTime, credentials);
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}

/// <summary>ASP.NET Core Identity's PBKDF2 hasher (HMAC-SHA512, salted, iterated).</summary>
public sealed class IdentityPasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<object> _inner = new(Options.Create(new PasswordHasherOptions { IterationCount = 210_000 }));
    private static readonly object User = new();

    public string Hash(string password) => _inner.HashPassword(User, password);

    public bool Verify(string hash, string password)
    {
        try
        {
            return _inner.VerifyHashedPassword(User, hash, password) != PasswordVerificationResult.Failed;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public sealed class QuoteOptions
{
    public const string Section = "Quotes";
    public string SigningKey { get; set; } = string.Empty;
}

/// <summary>HMAC-SHA256 signed, base64url-encoded quote payloads (tamper-evident, stateless).</summary>
public sealed class HmacQuoteTokenService(IOptions<QuoteOptions> options) : IQuoteTokenService
{
    public string Create(QuoteTokenPayload payload)
    {
        var body = Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload, Json.Options));
        return $"{body}.{Sign(body)}";
    }

    public QuoteTokenPayload? Read(string token)
    {
        var parts = token?.Split('.') ?? [];
        if (parts.Length != 2) return null;
        var expected = Encoding.ASCII.GetBytes(Sign(parts[0]));
        if (!CryptographicOperations.FixedTimeEquals(expected, Encoding.ASCII.GetBytes(parts[1]))) return null;
        try
        {
            return JsonSerializer.Deserialize<QuoteTokenPayload>(FromBase64Url(parts[0]), Json.Options);
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            return null;
        }
    }

    private string Sign(string body) =>
        Base64Url(HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.Value.SigningKey), Encoding.UTF8.GetBytes(body)));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}
