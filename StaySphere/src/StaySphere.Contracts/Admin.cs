namespace StaySphere.Contracts.Admin;

public sealed record TimePointDto(DateOnly Date, decimal Value);

public sealed record AdminDashboardDto(int Users, int Hosts, int Properties, int PublishedProperties, int Bookings, decimal Revenue,
    decimal Refunds, int OpenReports, int OpenTickets, int FraudAlerts, double CancellationRate,
    IReadOnlyList<TimePointDto> DailyBookings, IReadOnlyList<TimePointDto> DailyRevenue, IReadOnlyList<TimePointDto> NewUsers,
    IReadOnlyList<TimePointDto> NewProperties, string Currency);

public sealed record AdminUserDto(Guid Id, string Email, string DisplayName, IReadOnlyList<string> Roles, string Status,
    bool EmailConfirmed, DateTimeOffset CreatedAt, DateTimeOffset? LastLoginAt);

public sealed record AdminPropertyDto(Guid Id, string Title, Guid HostId, string HostName, string Status, string City, decimal BasePrice,
    string Currency, int ReviewCount, double RatingAverage, DateTimeOffset CreatedAt);

public sealed record AuditLogDto(long Id, Guid? ActorId, string? ActorName, string Action, string EntityType, string? EntityId,
    string? Details, string? IpAddress, string? CorrelationId, DateTimeOffset At);

public sealed record FraudAlertDto(Guid Id, string SubjectType, Guid SubjectId, Guid? UserId, string RuleCode, int Score, string Decision,
    string Details, bool Acknowledged, DateTimeOffset CreatedAt);

public sealed record ReportDto(Guid Id, Guid ReporterId, string ReporterName, string TargetType, Guid TargetId, string Reason, string Status, DateTimeOffset CreatedAt);

public sealed record HostDashboardDto(int TotalBookings, decimal Revenue, int UpcomingReservations, double OccupancyRate,
    decimal AverageNightlyPrice, double Rating, string Currency,
    IReadOnlyList<TimePointDto> RevenueByMonth, IReadOnlyList<TimePointDto> BookingsByMonth,
    IReadOnlyList<PropertyPerformanceDto> Properties);

public sealed record PropertyPerformanceDto(Guid PropertyId, string Title, int Bookings, decimal Revenue, double OccupancyRate, double Rating);

public sealed record CreateCouponRequest(string Code, decimal PercentOff, decimal? AmountOff, string? Currency, decimal MinimumSpend,
    DateTimeOffset ValidFrom, DateTimeOffset ValidTo, int MaxRedemptions);

public sealed record CouponDto(Guid Id, string Code, decimal PercentOff, decimal? AmountOff, string? Currency, decimal MinimumSpend,
    DateTimeOffset ValidFrom, DateTimeOffset ValidTo, int MaxRedemptions, int Redemptions, bool IsActive);
