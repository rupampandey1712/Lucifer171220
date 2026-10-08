namespace StaySphere.Contracts.Reviews;

public sealed record CreateReviewRequest(Guid ReservationId, int Overall, int Cleanliness, int Accuracy, int Communication,
    int Location, int CheckIn, int Value, string Comment);

public sealed record ReviewResponseRequest(string Response);

public sealed record ReviewDto(Guid Id, Guid PropertyId, string PropertyTitle, Guid GuestId, string GuestName, string? GuestAvatarUrl,
    int Overall, int Cleanliness, int Accuracy, int Communication, int Location, int CheckIn, int Value,
    string Comment, string? HostResponse, DateTimeOffset? HostRespondedAt, string Status, DateTimeOffset CreatedAt);

public sealed record RatingSummaryDto(double Overall, double Cleanliness, double Accuracy, double Communication, double Location,
    double CheckIn, double Value, int Count);

public sealed record ModerateReviewRequest(bool Hide, string? Note);
