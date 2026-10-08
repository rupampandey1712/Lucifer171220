using System.Text.RegularExpressions;
using StaySphere.Domain.Common;

namespace StaySphere.Domain.ValueObjects;

public sealed partial record EmailAddress
{
    private EmailAddress(string value) => Value = value;

    public string Value { get; }

    public string Normalized => Value.ToUpperInvariant();

    public static Result<EmailAddress> Create(string? input)
    {
        var trimmed = input?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > 256 || !Pattern().IsMatch(trimmed))
            return Error.Validation("email.invalid", "A valid email address is required.");
        return new EmailAddress(trimmed.ToLowerInvariant());
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public override string ToString() => Value;
}

/// <summary>Postal address owned by a property.</summary>
public sealed record Address(string Line1, string? Line2, string City, string? Region, string? PostalCode, string CountryCode, string Country);
