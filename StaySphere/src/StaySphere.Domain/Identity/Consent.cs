using StaySphere.Domain.Common;

namespace StaySphere.Domain.Identity;

public enum ConsentPurpose
{
    /// <summary>Accepted at sign-up; can't be withdrawn on its own (delete the account instead).</summary>
    Terms,
    /// <summary>Accepted at sign-up; can't be withdrawn on its own (delete the account instead).</summary>
    Privacy,
    /// <summary>Opt-in: newsletters and offers.</summary>
    MarketingEmails,
    /// <summary>Opt-in: product analytics beyond what the service strictly needs.</summary>
    ProductAnalytics,
}

/// <summary>
/// One consent decision. Records are append-only: the current state of a purpose is its latest record, and the full
/// history is kept as evidence (who, what, which policy version, when, from where).
/// </summary>
public sealed class ConsentRecord : Entity
{
    public const string CurrentPolicyVersion = "2026-10";

    private ConsentRecord() { }

    public Guid UserId { get; private set; }
    public ConsentPurpose Purpose { get; private set; }
    public bool Granted { get; private set; }
    public string PolicyVersion { get; private set; } = default!;
    public DateTimeOffset RecordedAt { get; private set; }
    public string? IpAddress { get; private set; }

    public static bool IsRequired(ConsentPurpose purpose) => purpose is ConsentPurpose.Terms or ConsentPurpose.Privacy;

    public static ConsentRecord Record(Guid userId, ConsentPurpose purpose, bool granted, string? ipAddress, DateTimeOffset now) =>
        new()
        {
            UserId = userId, Purpose = purpose, Granted = granted, PolicyVersion = CurrentPolicyVersion, RecordedAt = now,
            IpAddress = ipAddress is { Length: > 45 } ? ipAddress[..45] : ipAddress,
        };
}
