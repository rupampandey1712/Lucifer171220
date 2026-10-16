using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StaySphere.Application.Abstractions;
using StaySphere.Application.Admin;
using StaySphere.Application.Payments;
using StaySphere.Application.Booking;
using StaySphere.Application.Catalog;
using StaySphere.Application.Common;
using StaySphere.Application.Search;
using StaySphere.Application.Support;
using StaySphere.Contracts.Ai;
using StaySphere.Contracts.Booking;
using StaySphere.Contracts.Search;
using StaySphere.Contracts.Support;
using StaySphere.Domain.Identity;
using StaySphere.Domain.Platform;

namespace StaySphere.Application.Ai;

/// <summary>Per-request record of what the tools did — the reply is built from this, never from model free text alone.</summary>
public sealed class AssistantTurnState
{
    public List<AssistantPropertySuggestionDto> Suggestions { get; } = [];
    public List<string> ToolsUsed { get; } = [];
    public PendingActionDto? PendingAction { get; set; }
}

/// <summary>
/// The only surface the AI agents can act through. Every tool calls an Application service as the signed-in user, so
/// validation, authorization and business rules apply exactly as for the UI. Agents never touch the database directly,
/// and nothing here can take payment. Reservation holds become a <see cref="AiPendingAction"/> that a human confirms.
/// </summary>
public sealed class AssistantToolbox(
    ISearchService search,
    IPropertyService properties,
    IAvailabilityService availability,
    IPricingService pricing,
    IReservationService reservations,
    ISupportService support,
    IWeatherService weather,
    IHostDashboardService hostDashboard,
    IPayoutService payouts,
    IAdminService admin,
    IAppDbContext db,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    public AssistantTurnState State { get; } = new();

    [Description("Search published stays. Dates are ISO yyyy-MM-dd. Returns up to 6 matches with id, price and rating.")]
    public async Task<string> SearchProperties(
        [Description("City, region or country")] string? location = null,
        [Description("Check-in date yyyy-MM-dd")] string? checkIn = null,
        [Description("Check-out date yyyy-MM-dd")] string? checkOut = null,
        [Description("Number of guests")] int? guests = null,
        [Description("Maximum nightly price")] decimal? maxPrice = null,
        [Description("Minimum bedrooms")] int? minBedrooms = null,
        [Description("Amenity codes, e.g. wifi, pool, parking, kitchen, air-conditioning, washer, workspace")] string[]? amenities = null,
        [Description("Property type, e.g. Apartment, House, Villa, Cabin")] string? propertyType = null,
        [Description("Only pet friendly places")] bool? petFriendly = null,
        CancellationToken ct = default)
    {
        State.ToolsUsed.Add(nameof(SearchProperties));
        var query = new SearchPropertiesQuery
        {
            Location = location,
            CheckIn = ParseDate(checkIn),
            CheckOut = ParseDate(checkOut),
            Guests = guests,
            MaxPrice = maxPrice,
            Bedrooms = minBedrooms,
            Amenities = amenities,
            PropertyTypes = propertyType is null ? null : [propertyType],
            PetsAllowed = petFriendly,
            Sort = SearchSort.Recommended,
            PageSize = 6,
        };
        var result = await search.SearchAsync(query, ct);
        foreach (var p in result.Items)
        {
            var reasons = new List<string>();
            if (p.RatingAverage >= 4.7 && p.ReviewCount >= 3) reasons.Add($"highly rated ({p.RatingAverage:0.0}★ from {p.ReviewCount} reviews)");
            if (maxPrice is not null) reasons.Add($"within budget at {p.Currency} {p.NightlyPrice:0}/night");
            if (guests is not null) reasons.Add($"sleeps up to {p.MaxGuests}");
            if (minBedrooms is not null) reasons.Add($"{p.Bedrooms} bedrooms");
            if (amenities is { Length: > 0 }) reasons.Add("has " + string.Join(", ", amenities));
            if (p.InstantBook) reasons.Add("instant book");
            if (reasons.Count == 0) reasons.Add("a popular choice in the area");
            if (State.Suggestions.All(s => s.Id != p.Id))
                State.Suggestions.Add(new AssistantPropertySuggestionDto(p.Id, p.Title, p.City, p.Country, p.NightlyPrice, p.Currency, p.TotalPrice,
                    p.RatingAverage, p.ReviewCount, p.ImageUrls.Count > 0 ? p.ImageUrls[0] : null, string.Join("; ", reasons)));
        }

        return JsonSerializer.Serialize(new
        {
            total = result.TotalCount,
            results = result.Items.Select(p => new
            {
                p.Id, p.Title, p.City, p.Country, nightly = p.NightlyPrice, p.Currency, total = p.TotalPrice, rating = p.RatingAverage,
                reviews = p.ReviewCount, p.MaxGuests, p.Bedrooms, p.PropertyType,
            }),
        }, Json.Options);
    }

    [Description("Get details (amenities, rules, cancellation policy, host) of a property by id.")]
    public async Task<string> GetPropertyDetails([Description("Property id (GUID)")] string propertyId, CancellationToken ct = default)
    {
        State.ToolsUsed.Add(nameof(GetPropertyDetails));
        if (!Guid.TryParse(propertyId, out var id)) return Error("Invalid property id.");
        var p = await properties.GetAsync(id, ct);
        if (p is null) return Error("Property not found.");
        return JsonSerializer.Serialize(new
        {
            p.Id, p.Title, p.PropertyType, p.RoomType, p.MaxGuests, p.Bedrooms, p.Beds, p.Bathrooms, nightly = p.BasePrice, p.CleaningFee, p.Currency,
            city = p.Address?.City, country = p.Address?.Country, rating = p.RatingAverage, reviews = p.ReviewCount, p.CancellationPolicy,
            amenities = p.Amenities.Select(a => a.Name), p.PetsAllowed, p.InstantBook, host = p.Host.DisplayName,
        }, Json.Options);
    }

    [Description("Check whether a property is available for the given dates.")]
    public async Task<string> CheckAvailability(string propertyId, string checkIn, string checkOut, CancellationToken ct = default)
    {
        State.ToolsUsed.Add(nameof(CheckAvailability));
        if (!Guid.TryParse(propertyId, out var id) || ParseDate(checkIn) is not { } ci || ParseDate(checkOut) is not { } co)
            return Error("Provide a property id and dates as yyyy-MM-dd.");
        var a = await availability.GetAsync(id, ci, co, ct);
        if (a is null) return Error("Property not found.");
        var blocked = a.UnavailableDates.Where(d => d >= ci && d < co).ToList();
        return JsonSerializer.Serialize(new { available = blocked.Count == 0, unavailableNights = blocked, minNights = a.MinNights }, Json.Options);
    }

    [Description("Calculate the authoritative total price (nightly, cleaning, service fee, taxes, discounts) for a stay.")]
    public async Task<string> CalculatePrice(string propertyId, string checkIn, string checkOut, int guests, CancellationToken ct = default)
    {
        State.ToolsUsed.Add(nameof(CalculatePrice));
        if (!Guid.TryParse(propertyId, out var id) || ParseDate(checkIn) is not { } ci || ParseDate(checkOut) is not { } co)
            return Error("Provide a property id and dates as yyyy-MM-dd.");
        var quote = await pricing.QuoteAsync(id, new QuoteRequest(ci, co, Math.Max(1, guests), null), ct);
        if (quote.IsFailure) return Error(quote.Error!.Message);
        var q = quote.Value;
        return JsonSerializer.Serialize(new
        {
            q.Nights, q.Currency, q.AverageNightly, q.Accommodation, q.Discount, q.CleaningFee, q.ServiceFee, q.Taxes, q.Total, q.Available,
        }, Json.Options);
    }

    [Description("List the signed-in user's upcoming and recent reservations.")]
    public async Task<string> GetMyReservations(CancellationToken ct = default)
    {
        State.ToolsUsed.Add(nameof(GetMyReservations));
        if (!currentUser.IsAuthenticated) return Error("The user must sign in to see reservations.");
        var page = await reservations.ListMineAsync(null, 1, 10, ct);
        return JsonSerializer.Serialize(page.Items.Select(r => new
        {
            r.Id, r.PropertyTitle, r.City, r.CheckIn, r.CheckOut, r.Guests, r.Status, total = r.TotalAmount, r.Currency,
        }), Json.Options);
    }

    [Description("Prepare a reservation hold for the user. This does NOT book: it creates a proposal the user must confirm with a button. Always tell the user the total and ask them to confirm.")]
    public async Task<string> ProposeReservation(string propertyId, string checkIn, string checkOut, int guests, CancellationToken ct = default)
    {
        State.ToolsUsed.Add(nameof(ProposeReservation));
        if (!currentUser.IsAuthenticated) return Error("The user must sign in before booking.");
        if (!Guid.TryParse(propertyId, out var id) || ParseDate(checkIn) is not { } ci || ParseDate(checkOut) is not { } co)
            return Error("Ask the user for the check-in and check-out dates (yyyy-MM-dd) and the number of guests.");
        var quote = await pricing.QuoteAsync(id, new QuoteRequest(ci, co, Math.Max(1, guests), null), ct);
        if (quote.IsFailure) return Error(quote.Error!.Message);
        if (!quote.Value.Available) return Error("Those dates are not available.");

        var title = await db.Properties.Where(p => p.Id == id).Select(p => p.Title).FirstAsync(ct);
        var q = quote.Value;
        var summary = string.Create(CultureInfo.InvariantCulture,
            $"Hold {title} for {q.Nights} night(s), {ci:d MMM} – {co:d MMM yyyy}, {guests} guest(s). Total {q.Currency} {q.Total:N2} (incl. fees and taxes).");
        var action = AiPendingAction.Create(currentUser.RequireUserId(), nameof(ProposeReservation),
            JsonSerializer.Serialize(new ProposedReservation(id, ci, co, Math.Max(1, guests)), Json.Options), summary, clock.GetUtcNow());
        db.AiPendingActions.Add(action);
        await db.SaveChangesAsync(ct);
        State.PendingAction = new PendingActionDto(action.Id, "reservation_hold", summary, action.ExpiresAt);
        return JsonSerializer.Serialize(new { status = "awaiting_user_confirmation", summary }, Json.Options);
    }

    [Description("Open a support ticket for the signed-in user.")]
    public async Task<string> CreateSupportTicket(string subject, string description, CancellationToken ct = default)
    {
        State.ToolsUsed.Add(nameof(CreateSupportTicket));
        if (!currentUser.IsAuthenticated) return Error("The user must sign in to contact support.");
        var ticket = await support.CreateAsync(new CreateTicketRequest(null, "General", "Normal", subject, description), ct);
        return ticket.IsSuccess
            ? JsonSerializer.Serialize(new { ticketId = ticket.Value.Id, status = ticket.Value.Status }, Json.Options)
            : Error(ticket.Error!.Message);
    }

    [Description("Get the 7-day weather forecast near a property.")]
    public async Task<string> GetWeather(string propertyId, CancellationToken ct = default)
    {
        State.ToolsUsed.Add(nameof(GetWeather));
        if (!Guid.TryParse(propertyId, out var id)) return Error("Invalid property id.");
        var loc = await db.Properties.Where(p => p.Id == id).Select(p => new { p.Latitude, p.Longitude }).FirstOrDefaultAsync(ct);
        if (loc?.Latitude is null) return Error("Property not found.");
        var w = await weather.GetForecastAsync(loc.Latitude.Value, loc.Longitude!.Value, ct);
        return w is null ? Error("Weather is unavailable right now.") : JsonSerializer.Serialize(w.Daily.Take(7), Json.Options);
    }

    // ---------- Recommendation agent ----------

    [Description("Find published stays similar to a given property (same area, type and price band).")]
    public async Task<string> RecommendSimilar([Description("Property id (GUID)")] string propertyId, CancellationToken ct = default)
    {
        State.ToolsUsed.Add(nameof(RecommendSimilar));
        if (!Guid.TryParse(propertyId, out var id)) return Error("Invalid property id.");
        var similar = await search.SimilarAsync(id, ct);
        foreach (var p in similar.Take(6))
            if (State.Suggestions.All(s => s.Id != p.Id))
                State.Suggestions.Add(new AssistantPropertySuggestionDto(p.Id, p.Title, p.City, p.Country, p.NightlyPrice, p.Currency, p.TotalPrice,
                    p.RatingAverage, p.ReviewCount, p.ImageUrls.Count > 0 ? p.ImageUrls[0] : null,
                    string.Create(CultureInfo.InvariantCulture, $"similar {p.PropertyType.ToLowerInvariant()} in {p.City}, {p.RatingAverage:0.0}★")));
        return JsonSerializer.Serialize(similar.Take(6).Select(p => new
        {
            p.Id, p.Title, p.City, nightly = p.NightlyPrice, p.Currency, rating = p.RatingAverage, reviews = p.ReviewCount, p.PropertyType, p.Bedrooms,
        }), Json.Options);
    }

    // ---------- Host agent (host's own data only: the services scope every query to the current host) ----------

    [Description("The host's dashboard: bookings, revenue, occupancy, rating and per-listing performance.")]
    public async Task<string> GetHostDashboard(CancellationToken ct = default)
    {
        State.ToolsUsed.Add(nameof(GetHostDashboard));
        if (!IsHost) return Error("Only hosts can use this.");
        var d = await hostDashboard.GetAsync(ct);
        return JsonSerializer.Serialize(new
        {
            d.TotalBookings, d.Revenue, d.Currency, d.UpcomingReservations, occupancyPercent = Math.Round(d.OccupancyRate * 100, 1),
            d.AverageNightlyPrice, d.Rating,
            listings = d.Properties.Select(p => new { p.Title, p.Bookings, p.Revenue, occupancyPercent = Math.Round(p.OccupancyRate * 100, 1), p.Rating }),
        }, Json.Options);
    }

    [Description("The host's listings with status, price, rating and upcoming reservations.")]
    public async Task<string> GetHostListings(CancellationToken ct = default)
    {
        State.ToolsUsed.Add(nameof(GetHostListings));
        if (!IsHost) return Error("Only hosts can use this.");
        var list = await properties.ListForHostAsync(ct);
        return JsonSerializer.Serialize(list.Select(p => new
        {
            p.Id, p.Title, p.Status, p.City, price = p.BasePrice, p.Currency, rating = p.RatingAverage, reviews = p.ReviewCount, p.UpcomingReservations,
            completePercent = p.CompletionPercent,
        }), Json.Options);
    }

    [Description("The host's reservations. scope: requests (awaiting the host's answer), upcoming, past or cancelled.")]
    public async Task<string> GetHostReservations(string scope = "upcoming", CancellationToken ct = default)
    {
        State.ToolsUsed.Add(nameof(GetHostReservations));
        if (!IsHost) return Error("Only hosts can use this.");
        var allowed = new[] { "requests", "upcoming", "past", "cancelled" };
        var s = allowed.Contains(scope, StringComparer.OrdinalIgnoreCase) ? scope.ToLowerInvariant() : "upcoming";
        var page = await reservations.ListForHostAsync(s, 1, 10, ct);
        return JsonSerializer.Serialize(page.Items.Select(r => new
        {
            r.Id, r.PropertyTitle, guest = r.GuestName, r.CheckIn, r.CheckOut, r.Guests, r.Status, total = r.TotalAmount, r.Currency, r.ApprovalDeadline,
        }), Json.Options);
    }

    [Description("The host's earnings balances per currency (available, pending, paid out) and recent payouts.")]
    public async Task<string> GetHostEarnings(CancellationToken ct = default)
    {
        State.ToolsUsed.Add(nameof(GetHostEarnings));
        if (!IsHost) return Error("Only hosts can use this.");
        var summary = await payouts.GetSummaryAsync(ct);
        return JsonSerializer.Serialize(new
        {
            balances = summary.Balances,
            recentPayouts = summary.Payouts.Take(5).Select(p => new { p.Amount, p.Currency, p.Status, p.CreatedAt }),
            hasPayoutAccount = summary.Account is not null,
        }, Json.Options);
    }

    // ---------- Support agent ----------

    [Description("Explain what the user would be refunded if they cancelled a reservation now (server-side policy calculation).")]
    public async Task<string> PreviewCancellation([Description("Reservation id (GUID)")] string reservationId, CancellationToken ct = default)
    {
        State.ToolsUsed.Add(nameof(PreviewCancellation));
        if (!currentUser.IsAuthenticated) return Error("The user must sign in.");
        if (!Guid.TryParse(reservationId, out var id)) return Error("Invalid reservation id.");
        var preview = await reservations.PreviewCancellationAsync(id, ct);
        return preview.IsSuccess ? JsonSerializer.Serialize(preview.Value, Json.Options) : Error(preview.Error!.Message);
    }

    [Description("Prepare a cancellation for the user. This does NOT cancel: it creates a proposal the user must confirm with a button. Always tell the user the refund and ask them to confirm.")]
    public async Task<string> ProposeCancellation([Description("Reservation id (GUID)")] string reservationId, CancellationToken ct = default)
    {
        State.ToolsUsed.Add(nameof(ProposeCancellation));
        if (!currentUser.IsAuthenticated) return Error("The user must sign in.");
        if (!Guid.TryParse(reservationId, out var id)) return Error("Invalid reservation id.");
        // Same authorization and policy as the Cancel button: 404 for someone else's reservation, 409 if it can't be cancelled.
        var preview = await reservations.PreviewCancellationAsync(id, ct);
        if (preview.IsFailure) return Error(preview.Error!.Message);
        var title = await db.Reservations.Where(r => r.Id == id).Join(db.Properties, r => r.PropertyId, p => p.Id, (r, p) => p.Title).FirstAsync(ct);
        var pv = preview.Value;
        var summary = string.Create(CultureInfo.InvariantCulture,
            $"Cancel your stay at {title}. Refund {pv.Currency} {pv.RefundAmount:N2}; non-refundable {pv.Currency} {pv.NonRefundable:N2}. {pv.Explanation}");
        var action = AiPendingAction.Create(currentUser.RequireUserId(), nameof(ProposeCancellation),
            JsonSerializer.Serialize(new ProposedCancellation(id), Json.Options), summary, clock.GetUtcNow());
        db.AiPendingActions.Add(action);
        await db.SaveChangesAsync(ct);
        State.PendingAction = new PendingActionDto(action.Id, "reservation_cancellation", summary, action.ExpiresAt);
        return JsonSerializer.Serialize(new { status = "awaiting_user_confirmation", summary }, Json.Options);
    }

    // ---------- Admin analytics agent ----------

    [Description("Platform metrics: users, hosts, properties, bookings, revenue, refunds, cancellation rate, open reports/tickets and 30-day trends.")]
    public async Task<string> GetPlatformStats(CancellationToken ct = default)
    {
        State.ToolsUsed.Add(nameof(GetPlatformStats));
        if (!currentUser.IsInRole(Roles.Admin)) return Error("Only administrators can use this.");
        var d = await admin.DashboardAsync(ct);
        return JsonSerializer.Serialize(new
        {
            d.Users, d.Hosts, d.Properties, d.PublishedProperties, d.Bookings, d.Revenue, d.Refunds, d.Currency,
            cancellationRatePercent = Math.Round(d.CancellationRate * 100, 1), d.OpenReports, d.OpenTickets, d.FraudAlerts,
            bookingsLast7Days = d.DailyBookings.TakeLast(7).Sum(p => p.Value), bookingsPrevious7Days = d.DailyBookings.SkipLast(7).TakeLast(7).Sum(p => p.Value),
            revenueLast7Days = d.DailyRevenue.TakeLast(7).Sum(p => p.Value), newUsersLast7Days = d.NewUsers.TakeLast(7).Sum(p => p.Value),
        }, Json.Options);
    }

    [Description("The most recent unacknowledged fraud/risk alerts.")]
    public async Task<string> GetFraudAlerts(CancellationToken ct = default)
    {
        State.ToolsUsed.Add(nameof(GetFraudAlerts));
        if (!currentUser.IsInRole(Roles.Admin)) return Error("Only administrators can use this.");
        var page = await admin.FraudAlertsAsync(1, 10, ct);
        return JsonSerializer.Serialize(page.Items.Where(a => !a.Acknowledged).Select(a => new { a.RuleCode, a.Score, a.Decision, a.Details, a.CreatedAt }), Json.Options);
    }

    private bool IsHost => currentUser.IsInRole(Roles.Host) || currentUser.IsInRole(Roles.Admin);

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private static string Error(string message) => JsonSerializer.Serialize(new { error = message }, Json.Options);
}

public sealed record ProposedReservation(Guid PropertyId, DateOnly CheckIn, DateOnly CheckOut, int Guests);

public sealed record ProposedCancellation(Guid ReservationId);
