using StaySphere.Domain.Booking;
using StaySphere.Domain.Common;

namespace StaySphere.Domain.Reviews;

public enum ReviewStatus
{
    Published,
    Hidden,
}

public sealed record ReviewRatings(int Overall, int Cleanliness, int Accuracy, int Communication, int Location, int CheckIn, int Value)
{
    public bool IsValid() => new[] { Overall, Cleanliness, Accuracy, Communication, Location, CheckIn, Value }.All(r => r is >= 1 and <= 5);
}

public sealed class Review : AggregateRoot
{
    public static readonly TimeSpan ReviewWindow = TimeSpan.FromDays(30);

    private Review() { }

    public Guid ReservationId { get; private set; }
    public Guid PropertyId { get; private set; }
    public Guid GuestId { get; private set; }
    public int Overall { get; private set; }
    public int Cleanliness { get; private set; }
    public int Accuracy { get; private set; }
    public int Communication { get; private set; }
    public int Location { get; private set; }
    public int CheckIn { get; private set; }
    public int Value { get; private set; }
    public string Comment { get; private set; } = default!;
    public ReviewStatus Status { get; private set; }
    public string? ModerationNote { get; private set; }
    public string? HostResponse { get; private set; }
    public DateTimeOffset? HostRespondedAt { get; private set; }

    /// <summary>Guests can only review their own completed stays, after checkout, within 30 days.</summary>
    public static Result<Review> Submit(Reservation reservation, Guid guestId, ReviewRatings ratings, string comment, DateTimeOffset now)
    {
        if (reservation.GuestId != guestId)
            return Error.Forbidden("review.not_your_stay", "You can only review your own stays.");
        if (reservation.Status != ReservationStatus.Completed)
            return Error.Validation("review.not_completed", "You can review a stay once it is completed.");
        var checkout = new DateTimeOffset(reservation.CheckOut.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        if (now < checkout)
            return Error.Validation("review.before_checkout", "You can review a stay after checkout.");
        if (now - checkout > ReviewWindow)
            return Error.Validation("review.window_closed", "The review window for this stay has closed.");
        if (!ratings.IsValid())
            return Error.Validation("review.ratings", "Ratings must be between 1 and 5.");
        var text = comment.Trim();
        if (text.Length is < 10 or > 2000)
            return Error.Validation("review.comment", "Reviews must be between 10 and 2000 characters.");

        var review = new Review
        {
            ReservationId = reservation.Id,
            PropertyId = reservation.PropertyId,
            GuestId = guestId,
            Overall = ratings.Overall,
            Cleanliness = ratings.Cleanliness,
            Accuracy = ratings.Accuracy,
            Communication = ratings.Communication,
            Location = ratings.Location,
            CheckIn = ratings.CheckIn,
            Value = ratings.Value,
            Comment = text,
            Status = ReviewStatus.Published,
            CreatedAt = now,
            UpdatedAt = now,
        };
        review.Raise(new ReviewCreatedDomainEvent(review.Id, review.PropertyId, reservation.HostId, review.Overall));
        return review;
    }

    public Result Respond(Guid hostId, Guid propertyHostId, string response, DateTimeOffset now)
    {
        if (hostId != propertyHostId) return Error.Forbidden("review.not_host", "Only the host can respond.");
        if (HostResponse is not null) return Error.Conflict("review.already_responded", "You have already responded to this review.");
        if (string.IsNullOrWhiteSpace(response) || response.Length > 1000)
            return Error.Validation("review.response", "Responses must be between 1 and 1000 characters.");
        HostResponse = response.Trim();
        HostRespondedAt = now;
        return Result.Success();
    }

    public void Moderate(bool hide, string? note)
    {
        Status = hide ? ReviewStatus.Hidden : ReviewStatus.Published;
        ModerationNote = note;
        Raise(new ReviewCreatedDomainEvent(Id, PropertyId, Guid.Empty, Overall));
    }
}

public sealed record ReviewCreatedDomainEvent(Guid ReviewId, Guid PropertyId, Guid HostId, int Overall) : IDomainEvent;
