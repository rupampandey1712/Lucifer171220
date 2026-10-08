using System.Globalization;
using Microsoft.EntityFrameworkCore;
using StaySphere.Application.Abstractions;
using StaySphere.Application.Admin;
using StaySphere.Application.Common;
using StaySphere.Application.Engagement;
using StaySphere.Application.Payments;
using StaySphere.Application.Reviews;
using StaySphere.Contracts.Events;

namespace StaySphere.Application.Events;

/// <summary>Turns business events into in-app, real-time and email notifications.</summary>
public sealed class NotificationEventHandler(INotificationService notifications, IAppDbContext db) :
    IIntegrationEventHandler<UserRegisteredEvent>,
    IIntegrationEventHandler<ReservationConfirmedEvent>,
    IIntegrationEventHandler<ReservationCancelledEvent>,
    IIntegrationEventHandler<ReservationExpiredEvent>,
    IIntegrationEventHandler<PaymentFailedEvent>,
    IIntegrationEventHandler<RefundCompletedEvent>,
    IIntegrationEventHandler<ReservationCompletedEvent>,
    IIntegrationEventHandler<ReviewCreatedEvent>,
    IIntegrationEventHandler<MessageSentEvent>,
    IIntegrationEventHandler<PropertyPublishedEvent>
{
    public Task HandleAsync(UserRegisteredEvent e, CancellationToken ct) =>
        notifications.NotifyAsync(e.UserId, "welcome", "Welcome to StaySphere", $"Hi {e.DisplayName}, start exploring stays around the world.", "/search", ct);

    public async Task HandleAsync(ReservationConfirmedEvent e, CancellationToken ct)
    {
        var info = await InfoAsync(e.ReservationId, ct);
        await notifications.NotifyAsync(e.GuestId, "reservation.confirmed", "Your booking is confirmed",
            $"{info.Title} · {info.Dates}. Total paid {Money(e.Total, e.Currency)}.", $"/trips/{e.ReservationId}", ct, email: true);
        await notifications.NotifyAsync(e.HostId, "host.booking_received", "New booking received",
            $"{info.Guest} booked {info.Title} for {info.Dates}.", "/host/reservations", ct, email: true);
    }

    public async Task HandleAsync(ReservationCancelledEvent e, CancellationToken ct)
    {
        var info = await InfoAsync(e.ReservationId, ct);
        var refund = e.RefundAmount > 0 ? $" A refund of {Money(e.RefundAmount, e.Currency)} is on its way." : string.Empty;
        await notifications.NotifyAsync(e.GuestId, "reservation.cancelled", "Reservation cancelled", $"{info.Title} · {info.Dates} was cancelled.{refund}",
            $"/trips/{e.ReservationId}", ct, email: true);
        await notifications.NotifyAsync(e.HostId, "host.reservation_cancelled", "A reservation was cancelled",
            $"{info.Guest}'s stay at {info.Title} ({info.Dates}) was cancelled.", "/host/reservations", ct);
    }

    public Task HandleAsync(ReservationExpiredEvent e, CancellationToken ct) =>
        notifications.NotifyAsync(e.GuestId, "reservation.expired", "Your hold expired",
            "We released the dates because payment wasn't completed in time. You can try booking again.", $"/trips/{e.ReservationId}", ct);

    public Task HandleAsync(PaymentFailedEvent e, CancellationToken ct) =>
        notifications.NotifyAsync(e.PayerId, "payment.failed", "Payment failed", $"Your payment could not be completed: {e.Reason}", $"/trips/{e.ReservationId}", ct);

    public Task HandleAsync(RefundCompletedEvent e, CancellationToken ct) =>
        notifications.NotifyAsync(e.PayerId, "refund.processed", "Refund processed",
            $"We refunded {Money(e.Amount, e.Currency)} to your original payment method.", $"/trips/{e.ReservationId}", ct, email: true);

    public async Task HandleAsync(ReservationCompletedEvent e, CancellationToken ct)
    {
        var info = await InfoAsync(e.ReservationId, ct);
        await notifications.NotifyAsync(e.GuestId, "review.reminder", "How was your stay?",
            $"Share your experience at {info.Title} to help other travellers.", $"/trips/{e.ReservationId}", ct, email: true);
    }

    public async Task HandleAsync(ReviewCreatedEvent e, CancellationToken ct)
    {
        if (e.HostId == Guid.Empty) return; // Moderation re-publish, not a new review.
        var title = await db.Properties.Where(p => p.Id == e.PropertyId).Select(p => p.Title).FirstOrDefaultAsync(ct);
        await notifications.NotifyAsync(e.HostId, "host.review_received", "You received a new review",
            $"A guest rated {title} {e.Overall}/5.", "/host/reviews", ct);
    }

    public async Task HandleAsync(MessageSentEvent e, CancellationToken ct)
    {
        var sender = await db.Users.Where(u => u.Id == e.SenderId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct);
        await notifications.NotifyAsync(e.RecipientId, "message", $"New message from {sender}", "Open your inbox to reply.",
            $"/messages/{e.ConversationId}", ct);
    }

    public Task HandleAsync(PropertyPublishedEvent e, CancellationToken ct) =>
        notifications.NotifyAsync(e.HostId, "host.property_published", "Your listing is live", "Guests can now find and book your place.",
            $"/property/{e.PropertyId}", ct);

    private async Task<(string Title, string Dates, string Guest)> InfoAsync(Guid reservationId, CancellationToken ct)
    {
        var r = await db.Reservations.AsNoTracking().Where(x => x.Id == reservationId)
            .Select(x => new
            {
                x.CheckIn, x.CheckOut,
                Title = db.Properties.Where(p => p.Id == x.PropertyId).Select(p => p.Title).First(),
                Guest = db.Users.Where(u => u.Id == x.GuestId).Select(u => u.DisplayName).First(),
            }).FirstAsync(ct);
        return (r.Title, $"{r.CheckIn:d MMM} – {r.CheckOut:d MMM yyyy}", r.Guest);
    }

    private static string Money(decimal amount, string currency) => string.Create(CultureInfo.InvariantCulture, $"{currency} {amount:N2}");
}

/// <summary>Payments context reacting to cancellations: issues the refund computed by the cancellation policy.</summary>
public sealed class RefundOnCancellationHandler(IPaymentService payments) : IIntegrationEventHandler<ReservationCancelledEvent>
{
    public async Task HandleAsync(ReservationCancelledEvent e, CancellationToken ct)
    {
        if (e.RefundAmount <= 0) return;
        var result = await payments.RefundForCancelledReservationAsync(e.ReservationId, ct);
        if (result.IsFailure) throw new InvalidOperationException($"Refund failed: {result.Error!.Message}"); // retried by the bus
    }
}

public sealed class ReviewStatsHandler(IReviewService reviews) : IIntegrationEventHandler<ReviewCreatedEvent>
{
    public Task HandleAsync(ReviewCreatedEvent e, CancellationToken ct) => reviews.RecalculatePropertyRatingAsync(e.PropertyId, ct);
}

/// <summary>Search/caching side effects of catalog changes (the "search indexer" for the SQL search provider is cache invalidation).</summary>
public sealed class SearchIndexHandler(ICacheService cache) :
    IIntegrationEventHandler<PropertyPublishedEvent>, IIntegrationEventHandler<PropertyChangedEvent>
{
    public async Task HandleAsync(PropertyPublishedEvent e, CancellationToken ct)
    {
        await cache.RemoveAsync(CacheKeys.Property(e.PropertyId), ct);
        await cache.RemoveAsync(CacheKeys.Destinations, ct);
    }

    public Task HandleAsync(PropertyChangedEvent e, CancellationToken ct) => cache.RemoveAsync(CacheKeys.Property(e.PropertyId), ct);
}

public sealed class FraudEventHandler(IFraudService fraud) :
    IIntegrationEventHandler<PaymentFailedEvent>,
    IIntegrationEventHandler<ReservationHeldEvent>,
    IIntegrationEventHandler<ReservationCancelledEvent>,
    IIntegrationEventHandler<UserRegisteredEvent>
{
    public Task HandleAsync(PaymentFailedEvent e, CancellationToken ct) => fraud.EvaluateUserAsync(e.PayerId, "payment_failed", ct);
    public Task HandleAsync(ReservationHeldEvent e, CancellationToken ct) => fraud.EvaluateUserAsync(e.GuestId, "held", ct);
    public Task HandleAsync(ReservationCancelledEvent e, CancellationToken ct) => fraud.EvaluateUserAsync(e.GuestId, "cancelled", ct);
    public Task HandleAsync(UserRegisteredEvent e, CancellationToken ct) => fraud.EvaluateUserAsync(e.UserId, "registered", ct);
}
