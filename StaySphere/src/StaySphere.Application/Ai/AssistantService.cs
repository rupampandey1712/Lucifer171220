using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StaySphere.Application.Abstractions;
using StaySphere.Application.Booking;
using StaySphere.Application.Common;
using StaySphere.Contracts.Ai;
using StaySphere.Contracts.Booking;
using StaySphere.Domain.Common;
using StaySphere.Domain.Platform;

namespace StaySphere.Application.Ai;

public sealed class AiOptions
{
    public const string Section = "AI";
    /// <summary>
    /// Gemini (default) | FoundryLocal | Ollama | OpenAI (any OpenAI-compatible endpoint, incl. Azure OpenAI) | Rules
    /// (offline, deterministic). LLM providers run the Microsoft Agent Framework agent; Rules is also the fallback.
    /// </summary>
    public string Provider { get; set; } = AiProviders.Gemini;
    /// <summary>Optional for Gemini/OpenAI/Ollama (provider default), required for FoundryLocal (its port is dynamic).</summary>
    public string? BaseUrl { get; set; }
    /// <summary>Empty = provider default (Gemini only); required for the other LLM providers.</summary>
    public string? Model { get; set; }
    /// <summary>Never commit this. Falls back to the GEMINI_API_KEY / GOOGLE_API_KEY / OPENAI_API_KEY environment variables.</summary>
    public string? ApiKey { get; set; }
    public int MaxTurns { get; set; } = 20;
    public int MaxMessageLength { get; set; } = 1000;
    public int TimeoutSeconds { get; set; } = 60;
}

public static class AiProviders
{
    public const string Gemini = "Gemini";
    public const string FoundryLocal = "FoundryLocal";
    public const string Ollama = "Ollama";
    public const string OpenAI = "OpenAI";
    public const string Rules = "Rules";

    /// <summary>Alias that always points at Google's current Flash model. Pin a specific id with AI:Model for production.</summary>
    public const string DefaultGeminiModel = "gemini-flash-latest";
}

/// <summary>
/// The effective AI configuration. <see cref="UsesAgent"/> is false when the provider is Rules or when an LLM provider is
/// missing something it needs (e.g. no API key); the assistant then answers with the rule engine and
/// <see cref="DisabledReason"/> says how to enable the agent.
/// </summary>
public sealed record AiProviderSettings(string Provider, string? Model, Uri? BaseUrl, string? ApiKey, string? DisabledReason)
{
    public bool UsesAgent => DisabledReason is null && Provider != AiProviders.Rules;

    public static AiProviderSettings Resolve(AiOptions options, Func<string, string?> environment)
    {
        var provider = options.Provider.Trim();
        var model = Blank(options.Model);
        var key = Blank(options.ApiKey);
        var baseUrl = Blank(options.BaseUrl);
        Uri? url = null;
        if (baseUrl is not null && (!Uri.TryCreate(baseUrl, UriKind.Absolute, out url) || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps)))
            throw new InvalidOperationException($"AI:BaseUrl '{baseUrl}' must be an absolute http(s) URL.");

        AiProviderSettings Disabled(string name, string reason) => new(name, model, url, null, reason);

        if (provider.Equals(AiProviders.Rules, StringComparison.OrdinalIgnoreCase))
            return new(AiProviders.Rules, null, null, null, null);

        if (provider.Equals(AiProviders.Gemini, StringComparison.OrdinalIgnoreCase))
        {
            key ??= Blank(environment("GEMINI_API_KEY")) ?? Blank(environment("GOOGLE_API_KEY"));
            return key is null
                ? Disabled(AiProviders.Gemini, "No Gemini API key. Set GEMINI_API_KEY (or AI:ApiKey in user secrets); get one at https://aistudio.google.com/apikey.")
                : new(AiProviders.Gemini, model ?? AiProviders.DefaultGeminiModel, url, key, null);
        }

        if (provider.Equals(AiProviders.FoundryLocal, StringComparison.OrdinalIgnoreCase))
        {
            if (url is null) return Disabled(AiProviders.FoundryLocal, "Set AI:BaseUrl to Foundry Local's endpoint, e.g. http://localhost:5273/v1 (run `foundry service status` for the port).");
            if (model is null) return Disabled(AiProviders.FoundryLocal, "Set AI:Model to a loaded Foundry Local model id (run `foundry model list`).");
            return new(AiProviders.FoundryLocal, model, url, key ?? "foundry-local", null);
        }

        if (provider.Equals(AiProviders.Ollama, StringComparison.OrdinalIgnoreCase))
        {
            if (model is null) return Disabled(AiProviders.Ollama, "Set AI:Model to a pulled Ollama model that supports tool calling, e.g. llama3.2.");
            return new(AiProviders.Ollama, model, url ?? new Uri("http://localhost:11434"), null, null);
        }

        if (provider.Equals(AiProviders.OpenAI, StringComparison.OrdinalIgnoreCase))
        {
            key ??= Blank(environment("OPENAI_API_KEY"));
            if (model is null) return Disabled(AiProviders.OpenAI, "Set AI:Model.");
            if (key is null) return Disabled(AiProviders.OpenAI, "Set AI:ApiKey or OPENAI_API_KEY.");
            return new(AiProviders.OpenAI, model, url, key, null);
        }

        throw new InvalidOperationException(
            $"Unknown AI:Provider '{options.Provider}'. Use Gemini, FoundryLocal, Ollama, OpenAI or Rules.");
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record AssistantTurn(string Role, string Content);

public sealed class AssistantMemory
{
    public List<AssistantTurn> Turns { get; set; } = [];
    public List<Guid> LastSuggestions { get; set; } = [];
    public string? LastCheckIn { get; set; }
    public string? LastCheckOut { get; set; }
    public int? LastGuests { get; set; }
}

/// <summary>An agent runtime (LLM-backed or rule-based) that answers using only the toolbox.</summary>
public interface IAssistantEngine
{
    string Name { get; }
    Task<string> RunAsync(AssistantMemory memory, string userMessage, AssistantToolbox toolbox, CancellationToken ct);
}

public interface IAssistantService
{
    Task<Result<AssistantReplyDto>> SendAsync(AssistantMessageRequest request, CancellationToken ct);
    Task<Result<ConfirmActionResultDto>> ConfirmAsync(Guid actionId, bool approve, CancellationToken ct);
}

public sealed partial class AssistantService(
    IEnumerable<IAssistantEngine> engines,
    AssistantToolbox toolbox,
    ICacheService cache,
    IAppDbContext db,
    IPricingService pricing,
    IReservationService reservations,
    ICurrentUser currentUser,
    IAuditLogger audit,
    TimeProvider clock,
    IOptions<AiOptions> options,
    ILogger<AssistantService> logger) : IAssistantService
{
    public async Task<Result<AssistantReplyDto>> SendAsync(AssistantMessageRequest request, CancellationToken ct)
    {
        var opts = options.Value;
        var text = request.Message?.Trim() ?? string.Empty;
        if (text.Length == 0) return Domain.Common.Error.Validation("ai.empty", "Please type a message.");
        if (text.Length > opts.MaxMessageLength) return Domain.Common.Error.Validation("ai.too_long", $"Messages are limited to {opts.MaxMessageLength} characters.");

        var conversationId = request.ConversationId ?? Guid.CreateVersion7();
        var key = $"ai:conversation:{currentUser.UserId?.ToString() ?? "anon"}:{conversationId}";
        var memory = await cache.GetAsync<AssistantMemory>(key, ct) ?? new AssistantMemory();
        if (memory.Turns.Count / 2 >= opts.MaxTurns)
            return new Error("ai.conversation_limit", "This conversation is long — please start a new one.", ErrorType.TooManyRequests);

        var safeText = RedactPii(text);
        var engine = SelectEngine(opts.Provider);
        string reply;
        string used = engine.Name;
        try
        {
            reply = await engine.RunAsync(memory, safeText, toolbox, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Graceful degradation: the LLM is optional; the rule engine still answers from real data.
            logger.LogWarning(ex, "AI engine {Engine} failed; falling back to rules", engine.Name);
            var fallback = SelectEngine("Rules");
            used = fallback.Name;
            reply = await fallback.RunAsync(memory, safeText, toolbox, ct);
        }

        var state = toolbox.State;
        memory.Turns.Add(new AssistantTurn("user", safeText));
        memory.Turns.Add(new AssistantTurn("assistant", reply));
        if (state.Suggestions.Count > 0)
        {
            memory.LastSuggestions.Clear();
            memory.LastSuggestions.AddRange(state.Suggestions.Select(s => s.Id));
        }

        await cache.SetAsync(key, memory, TimeSpan.FromHours(2), ct);
        if (currentUser.IsAuthenticated)
        {
            audit.Record("ai.message", "AiConversation", conversationId.ToString(), new { tools = state.ToolsUsed, engine = used });
            await db.SaveChangesAsync(ct);
        }

        return new AssistantReplyDto(conversationId, reply, state.Suggestions.Take(6).ToList(), state.PendingAction, state.ToolsUsed.Distinct().ToList(), used);
    }

    /// <summary>The human-in-the-loop gate: only an explicit approval from the owner executes a proposed action.</summary>
    public async Task<Result<ConfirmActionResultDto>> ConfirmAsync(Guid actionId, bool approve, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var action = await db.AiPendingActions.FirstOrDefaultAsync(a => a.Id == actionId, ct);
        if (action is null || action.UserId != userId) return Domain.Common.Error.NotFound("ai.action_not_found", "That request was not found.");
        if (!action.CanExecute(userId, clock.GetUtcNow()))
            return Domain.Common.Error.Conflict("ai.action_expired", "This request has expired or was already handled.");

        if (!approve)
        {
            action.Reject();
            audit.Record("ai.action_rejected", "AiPendingAction", actionId.ToString());
            await db.SaveChangesAsync(ct);
            return new ConfirmActionResultDto(actionId, "Rejected", "No problem — nothing was booked.", null);
        }

        if (action.ToolName != nameof(AssistantToolbox.ProposeReservation))
            return Domain.Common.Error.Validation("ai.action_unsupported", "Unsupported action.");

        // Replays the STORED arguments through the normal booking path (fresh server quote, availability, rules).
        var args = JsonSerializer.Deserialize<ProposedReservation>(action.ArgumentsJson, Json.Options)!;
        var quote = await pricing.QuoteAsync(args.PropertyId, new QuoteRequest(args.CheckIn, args.CheckOut, args.Guests, null), ct);
        if (quote.IsFailure) return quote.Error!;
        var hold = await reservations.CreateHoldAsync(
            new CreateReservationRequest(args.PropertyId, args.CheckIn, args.CheckOut, args.Guests, quote.Value.QuoteToken, null), ct);
        if (hold.IsFailure) return hold.Error!;

        action.Approve(JsonSerializer.Serialize(new { reservationId = hold.Value.Id }, Json.Options));
        audit.Record("ai.action_approved", "AiPendingAction", actionId.ToString(), new { reservationId = hold.Value.Id });
        await db.SaveChangesAsync(ct);
        return new ConfirmActionResultDto(actionId, "Approved",
            "Your dates are held for 10 minutes. Complete payment on the checkout page to confirm.", hold.Value.Id);
    }

    private IAssistantEngine SelectEngine(string provider) =>
        engines.FirstOrDefault(e => string.Equals(e.Name, provider, StringComparison.OrdinalIgnoreCase))
        ?? engines.First(e => e.Name == RuleBasedAssistantEngine.EngineName);

    /// <summary>Removes emails, phone numbers and card-like numbers before text reaches any model or log.</summary>
    public static string RedactPii(string text)
    {
        text = EmailRegex().Replace(text, "[email]");
        text = CardRegex().Replace(text, m => IsDateLike(m.Value) ? m.Value : "[card]");
        return PhoneRegex().Replace(text, m => IsDateLike(m.Value) || m.Value.Count(char.IsDigit) < 9 ? m.Value : "[phone]");
    }

    // ISO dates ("2030-05-01", or two in a row) look like digit runs but are needed for bookings — never redact them.
    private static bool IsDateLike(string value) => IsoDatesRegex().IsMatch(value.Trim());

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}(?:\s+\d{4}-\d{2}-\d{2})*$")]
    private static partial Regex IsoDatesRegex();

    [GeneratedRegex(@"[^\s@]+@[^\s@]+\.[^\s@]+")]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"\b(?:\d[ -]?){13,19}\b")]
    private static partial Regex CardRegex();

    [GeneratedRegex(@"\+?\d[\d\s().-]{8,}\d")]
    private static partial Regex PhoneRegex();
}

/// <summary>
/// Deterministic, offline agent: parses intent with patterns and calls the same tools an LLM would. Always available,
/// used in tests, and the fallback when the configured LLM is unreachable.
/// </summary>
public sealed partial class RuleBasedAssistantEngine(IAppDbContext db, TimeProvider clock) : IAssistantEngine
{
    public const string EngineName = "Rules";

    private static readonly Dictionary<string, string> AmenityWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["wifi"] = "wifi", ["wi-fi"] = "wifi", ["internet"] = "wifi", ["pool"] = "pool", ["parking"] = "parking",
        ["kitchen"] = "kitchen", ["air conditioning"] = "air-conditioning", ["aircon"] = "air-conditioning", ["a/c"] = "air-conditioning",
        ["washer"] = "washer", ["washing machine"] = "washer", ["workspace"] = "workspace", ["desk"] = "workspace",
        ["hot tub"] = "hot-tub", ["gym"] = "gym", ["fireplace"] = "fireplace", ["beach"] = "beachfront", ["ev charger"] = "ev-charger",
    };

    private static readonly string[] Types = ["Apartment", "House", "Villa", "Hotel", "Cabin", "Cottage", "Hostel", "Resort", "GuestHouse", "FarmStay"];

    public string Name => EngineName;

    public async Task<string> RunAsync(AssistantMemory memory, string message, AssistantToolbox tools, CancellationToken ct)
    {
        var m = message.ToLowerInvariant();
        var (checkIn, checkOut) = ParseDates(message, memory);
        var guests = ParseInt(GuestsRegex(), m) ?? memory.LastGuests;
        if (checkIn is not null && checkOut is not null)
        {
            memory.LastCheckIn = Iso(checkIn.Value);
            memory.LastCheckOut = Iso(checkOut.Value);
        }

        memory.LastGuests = guests;

        if (Regex.IsMatch(m, @"\b(my|upcoming)\s+(trips?|reservations?|bookings?|stays?)\b"))
        {
            var json = await tools.GetMyReservations(ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return "Please sign in so I can look up your reservations.";
            var lines = doc.RootElement.EnumerateArray().Take(5).Select(r =>
                $"• {r.GetProperty("propertyTitle").GetString()} in {r.GetProperty("city").GetString()}: {r.GetProperty("checkIn").GetString()} → {r.GetProperty("checkOut").GetString()} ({r.GetProperty("status").GetString()})").ToList();
            return lines.Count == 0 ? "You don't have any reservations yet. Want me to find a place?" : "Here are your recent reservations:\n" + string.Join("\n", lines);
        }

        if (Regex.IsMatch(m, @"\b(book|reserve)\b") && memory.LastSuggestions.Count > 0)
        {
            var target = PickReferenced(m, memory.LastSuggestions);
            if (checkIn is null || checkOut is null)
                return "Happy to help you book. Which dates would you like? Please give check-in and check-out as YYYY-MM-DD, plus the number of guests.";
            var result = await tools.ProposeReservation(target.ToString(), Iso(checkIn.Value), Iso(checkOut.Value), guests ?? 1, ct);
            using var doc = JsonDocument.Parse(result);
            if (doc.RootElement.TryGetProperty("error", out var err)) return $"I couldn't prepare that booking: {err.GetString()}";
            return $"{doc.RootElement.GetProperty("summary").GetString()}\n\nPlease review and press **Confirm** to place a 10-minute hold — nothing is booked until you confirm, and payment happens on the checkout page.";
        }

        if (Regex.IsMatch(m, @"\bcompare\b") && memory.LastSuggestions.Count >= 2)
        {
            var sb = new StringBuilder("Here's a quick comparison:\n");
            foreach (var id in memory.LastSuggestions.Take(3))
            {
                using var doc = JsonDocument.Parse(await tools.GetPropertyDetails(id.ToString(), ct));
                var r = doc.RootElement;
                if (r.TryGetProperty("error", out _)) continue;
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"• {r.GetProperty("title").GetString()} — {r.GetProperty("currency").GetString()} {r.GetProperty("nightly").GetDecimal():0}/night, {r.GetProperty("bedrooms").GetInt32()} bedrooms, sleeps {r.GetProperty("maxGuests").GetInt32()}, {r.GetProperty("rating").GetDouble():0.0}★, {r.GetProperty("cancellationPolicy").GetString()} cancellation");
            }

            return sb.ToString().TrimEnd();
        }

        if (Regex.IsMatch(m, @"\bwhy\b.*\b(expensive|price|cost)") && memory.LastSuggestions.Count > 0)
        {
            var id = PickReferenced(m, memory.LastSuggestions);
            var ci = checkIn ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(14);
            var co = checkOut ?? ci.AddDays(3);
            using var doc = JsonDocument.Parse(await tools.CalculatePrice(id.ToString(), Iso(ci), Iso(co), guests ?? 2, ct));
            var r = doc.RootElement;
            if (r.TryGetProperty("error", out var e)) return e.GetString()!;
            var cur = r.GetProperty("currency").GetString();
            return string.Create(CultureInfo.InvariantCulture,
                $"For {r.GetProperty("nights").GetInt32()} nights the price breaks down as: accommodation {cur} {r.GetProperty("accommodation").GetDecimal():N2} (avg {r.GetProperty("averageNightly").GetDecimal():N2}/night — seasonal and weekend rates apply), cleaning {r.GetProperty("cleaningFee").GetDecimal():N2}, service fee {r.GetProperty("serviceFee").GetDecimal():N2}, taxes {r.GetProperty("taxes").GetDecimal():N2}, discounts −{r.GetProperty("discount").GetDecimal():N2}. Total {cur} {r.GetProperty("total").GetDecimal():N2}.");
        }

        if (Regex.IsMatch(m, @"\b(support|help desk|complain|problem with)\b") && Regex.IsMatch(m, @"\b(ticket|contact|open)\b"))
        {
            var json = await tools.CreateSupportTicket("Assistant request", message, ct);
            return json.Contains("ticketId", StringComparison.Ordinal)
                ? "I've opened a support ticket for you — our team will reply in your Support inbox."
                : "Please sign in so I can open a support ticket for you.";
        }

        // Default: search.
        var location = await FindLocationAsync(m, ct);
        var maxPrice = ParseDecimal(PriceRegex(), message);
        var bedrooms = ParseInt(BedroomsRegex(), m);
        if (Regex.IsMatch(m, @"family[- ]friendly|with kids|children")) { guests ??= 4; bedrooms ??= 2; }
        var amenities = AmenityWords.Where(kv => m.Contains(kv.Key, StringComparison.Ordinal)).Select(kv => kv.Value).Distinct().ToArray();
        var type = Types.FirstOrDefault(t => m.Contains(t.ToLowerInvariant(), StringComparison.Ordinal));
        var pets = Regex.IsMatch(m, @"\b(pet|pets|dog|cat)\b") ? true : (bool?)null;
        var tripDays = ParseInt(TripRegex(), m);

        var ciText = checkIn is null ? null : Iso(checkIn.Value);
        var coText = checkOut is null ? null : Iso(checkOut.Value);
        await tools.SearchProperties(location, ciText, coText, guests, maxPrice, bedrooms, amenities.Length == 0 ? null : amenities, type, pets, ct);
        var found = tools.State.Suggestions;
        var relaxed = false;
        if (found.Count == 0 && (type is not null || amenities.Length > 0 || bedrooms is not null))
        {
            // Relax the softer constraints (type, amenities, bedrooms) but never budget, dates, guests or location.
            relaxed = true;
            await tools.SearchProperties(location, ciText, coText, guests, maxPrice, null, null, null, pets, ct);
        }

        if (found.Count == 0)
            return "I couldn't find stays matching that. Try widening your budget, changing dates, or choosing another destination.";

        var where = location is null ? "" : $" in {location}";
        var intro = tripDays is { } days
            ? $"Here's a plan for {days} days{where}: base yourself at one of these stays, spend day 1 settling in and exploring the neighbourhood, use the middle days for the main sights and a day trip, and keep the last day relaxed before check-out. Top picks:"
            : relaxed
                ? $"Nothing matched every detail, so I loosened the room type/amenity/bedroom filters. Closest options{where}:"
                : $"I found {found.Count} great option{(found.Count == 1 ? "" : "s")}{where}:";
        var list = string.Join("\n", found.Take(3).Select((s, i) =>
            string.Create(CultureInfo.InvariantCulture, $"{i + 1}. {s.Title} ({s.City}) — {s.Currency} {s.NightlyPrice:0}/night, {s.Rating:0.0}★. Why: {s.Reason}.")));
        return $"{intro}\n{list}\n\nSay \"compare\", \"why is the first one expensive\", or \"book the second one from YYYY-MM-DD to YYYY-MM-DD for 2 guests\".";
    }

    private async Task<string?> FindLocationAsync(string message, CancellationToken ct)
    {
        var places = await db.Properties.AsNoTracking().Where(p => p.Address != null && p.Status == Domain.Catalog.PropertyStatus.Published)
            .Select(p => new { p.Address!.City, p.Address.Country }).Distinct().ToListAsync(ct);
        var city = places.Select(p => p.City).Distinct().OrderByDescending(c => c.Length)
            .FirstOrDefault(c => message.Contains(c.ToLowerInvariant(), StringComparison.Ordinal));
        return city ?? places.Select(p => p.Country).Distinct().FirstOrDefault(c => message.Contains(c.ToLowerInvariant(), StringComparison.Ordinal));
    }

    private static Guid PickReferenced(string m, List<Guid> ids)
    {
        var index = m switch
        {
            _ when Regex.IsMatch(m, @"\b(second|2nd|#2|number 2)\b") => 1,
            _ when Regex.IsMatch(m, @"\b(third|3rd|#3|number 3)\b") => 2,
            _ => 0,
        };
        return ids[Math.Min(index, ids.Count - 1)];
    }

    private (DateOnly?, DateOnly?) ParseDates(string message, AssistantMemory memory)
    {
        var matches = DateRegex().Matches(message);
        var dates = matches.Select(x => DateOnly.TryParseExact(x.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : (DateOnly?)null)
            .Where(d => d is not null).Select(d => d!.Value).ToList();
        if (dates.Count >= 2) return (dates[0], dates[1]);
        var nights = ParseInt(NightsRegex(), message.ToLowerInvariant());
        if (dates.Count == 1 && nights is not null) return (dates[0], dates[0].AddDays(nights.Value));
        DateOnly? Parse(string? s) => s is null ? null : DateOnly.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return (Parse(memory.LastCheckIn), Parse(memory.LastCheckOut));
    }

    private static int? ParseInt(Regex regex, string text)
    {
        var match = regex.Match(text);
        if (!match.Success) return null;
        var group = match.Groups.Cast<Group>().Skip(1).FirstOrDefault(g => g.Success);
        return group is not null && int.TryParse(group.Value, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static decimal? ParseDecimal(Regex regex, string text) =>
        regex.Match(text) is { Success: true } match && decimal.TryParse(match.Groups[1].Value.Replace(",", ""), CultureInfo.InvariantCulture, out var v) ? v : null;

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\b(\d{1,2})\s*(?:guests?|people|persons|adults|travell?ers|pax)\b|\bfor\s+(?:a\s+family\s+of\s+)?(\d{1,2})\b")]
    private static partial Regex GuestsRegex();

    [GeneratedRegex(@"\b(\d{1,2})[- ]?(?:bed(?:room)?s?|br)\b")]
    private static partial Regex BedroomsRegex();

    [GeneratedRegex(@"(?:under|below|less than|max(?:imum)?|up to|budget(?: of)?)\s*[£$€₹]?\s*([\d,]+(?:\.\d+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex PriceRegex();

    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}")]
    private static partial Regex DateRegex();

    [GeneratedRegex(@"\b(\d{1,2})\s*nights?\b")]
    private static partial Regex NightsRegex();

    [GeneratedRegex(@"\b(\d{1,2})[- ]day\b")]
    private static partial Regex TripRegex();
}
