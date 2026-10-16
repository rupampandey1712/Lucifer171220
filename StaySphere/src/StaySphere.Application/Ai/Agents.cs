using StaySphere.Application.Abstractions;
using StaySphere.Contracts.Ai;
using StaySphere.Domain.Identity;

namespace StaySphere.Application.Ai;

/// <summary>
/// One specialised agent: its instructions, the ONLY tools it may call, and who may talk to it. Engines (LLM or rules) receive
/// the definition and expose just <see cref="Tools"/>; the toolbox re-checks roles on every privileged tool as well.
/// </summary>
public sealed record AgentDefinition(
    string Key,
    string Name,
    string DisplayName,
    string Description,
    string Instructions,
    IReadOnlyList<string> Tools,
    bool RequiresSignIn,
    IReadOnlyList<string> Roles)
{
    public bool CanUse(ICurrentUser user) =>
        (!RequiresSignIn || user.IsAuthenticated) && (Roles.Count == 0 || Roles.Any(user.IsInRole));

    public AgentInfoDto ToDto() => new(Key, Name, DisplayName, Description, Tools);
}

public static class AgentCatalog
{
    public const string TravelKey = "travel";

    public static readonly AgentDefinition Travel = new(
        TravelKey, "TravelAssistantAgent", "Travel assistant",
        "Finds stays, checks availability and prices, and prepares bookings you confirm.",
        """
        You help guests find and compare stays and plan trips. Recommend at most 3 places and say why each fits.
        To book, call ProposeReservation; it does NOT book. Tell the user the total and that they must press Confirm.
        """,
        [nameof(AssistantToolbox.SearchProperties), nameof(AssistantToolbox.GetPropertyDetails), nameof(AssistantToolbox.CheckAvailability),
         nameof(AssistantToolbox.CalculatePrice), nameof(AssistantToolbox.GetMyReservations), nameof(AssistantToolbox.ProposeReservation),
         nameof(AssistantToolbox.CreateSupportTicket), nameof(AssistantToolbox.GetWeather)],
        RequiresSignIn: false, Roles: []);

    public static readonly AgentDefinition Recommendation = new(
        "recommendation", "PropertyRecommendationAgent", "Recommendations",
        "Suggests stays that match your taste and finds places similar to ones you like.",
        """
        You recommend properties. Use SearchProperties for new requests and RecommendSimilar when the user likes a specific place.
        Explain each recommendation with concrete facts from tool results (rating, price, size, amenities). Never invent properties.
        """,
        [nameof(AssistantToolbox.SearchProperties), nameof(AssistantToolbox.RecommendSimilar), nameof(AssistantToolbox.GetPropertyDetails),
         nameof(AssistantToolbox.CalculatePrice)],
        RequiresSignIn: false, Roles: []);

    public static readonly AgentDefinition Host = new(
        "host", "HostAssistantAgent", "Host assistant",
        "Summarises your listings, booking requests, performance and earnings.",
        """
        You help a host run their listings. Use the host tools to answer about listings, pending booking requests, performance and
        earnings. You can read data only: tell the host to accept or decline requests and change prices in the Hosting pages.
        """,
        [nameof(AssistantToolbox.GetHostDashboard), nameof(AssistantToolbox.GetHostListings), nameof(AssistantToolbox.GetHostReservations),
         nameof(AssistantToolbox.GetHostEarnings)],
        RequiresSignIn: true, Roles: [Roles.Host, Roles.Admin]);

    public static readonly AgentDefinition Support = new(
        "support", "SupportAgent", "Support",
        "Helps with your reservations, refunds and cancellations, and opens support tickets.",
        """
        You are a support agent for the signed-in user. Look up their reservations, explain cancellation refunds with
        PreviewCancellation, and open tickets. To cancel, call ProposeCancellation: it does NOT cancel. Tell the user the refund and
        that they must press Confirm. Never promise refunds beyond what PreviewCancellation returns.
        """,
        [nameof(AssistantToolbox.GetMyReservations), nameof(AssistantToolbox.PreviewCancellation), nameof(AssistantToolbox.ProposeCancellation),
         nameof(AssistantToolbox.CreateSupportTicket)],
        RequiresSignIn: true, Roles: []);

    public static readonly AgentDefinition AdminAnalytics = new(
        "admin", "AdminAnalyticsAgent", "Admin analytics",
        "Answers questions about platform metrics, trends and fraud alerts.",
        """
        You answer platform analytics questions for administrators using GetPlatformStats and GetFraudAlerts. Quote exact numbers
        from the tools, mention the currency, and point out notable trends. You can read data only.
        """,
        [nameof(AssistantToolbox.GetPlatformStats), nameof(AssistantToolbox.GetFraudAlerts)],
        RequiresSignIn: true, Roles: [Roles.Admin]);

    public static readonly IReadOnlyList<AgentDefinition> All = [Travel, Recommendation, Host, Support, AdminAnalytics];

    public static AgentDefinition? Find(string? key) =>
        string.IsNullOrWhiteSpace(key) ? Travel : All.FirstOrDefault(a => string.Equals(a.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));
}
