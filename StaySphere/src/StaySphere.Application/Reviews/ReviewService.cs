using Microsoft.EntityFrameworkCore;
using StaySphere.Application.Abstractions;
using StaySphere.Application.Common;
using StaySphere.Contracts;
using StaySphere.Contracts.Reviews;
using StaySphere.Domain.Common;
using StaySphere.Domain.Reviews;

namespace StaySphere.Application.Reviews;

public interface IReviewService
{
    Task<Result<ReviewDto>> CreateAsync(CreateReviewRequest request, CancellationToken ct);
    Task<PagedResult<ReviewDto>> ListForPropertyAsync(Guid propertyId, int page, int pageSize, CancellationToken ct);
    Task<RatingSummaryDto> SummaryAsync(Guid propertyId, CancellationToken ct);
    Task<PagedResult<ReviewDto>> ListForHostAsync(int page, int pageSize, CancellationToken ct);
    Task<PagedResult<ReviewDto>> ListAllAsync(string? status, int page, int pageSize, CancellationToken ct);
    Task<Result> RespondAsync(Guid reviewId, string response, CancellationToken ct);
    Task<Result> ModerateAsync(Guid reviewId, ModerateReviewRequest request, CancellationToken ct);
    Task RecalculatePropertyRatingAsync(Guid propertyId, CancellationToken ct);
}

public sealed class ReviewService(IAppDbContext db, ICurrentUser currentUser, ICacheService cache, IAuditLogger audit, TimeProvider clock) : IReviewService
{
    public async Task<Result<ReviewDto>> CreateAsync(CreateReviewRequest r, CancellationToken ct)
    {
        var guestId = currentUser.RequireUserId();
        var reservation = await db.Reservations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.ReservationId, ct);
        if (reservation is null || reservation.GuestId != guestId)
            return Error.Forbidden("review.not_your_stay", "You can only review stays you have booked.");
        if (await db.Reviews.AnyAsync(x => x.ReservationId == r.ReservationId, ct))
            return Error.Conflict("review.duplicate", "You have already reviewed this stay.");

        var review = Review.Submit(reservation, guestId,
            new ReviewRatings(r.Overall, r.Cleanliness, r.Accuracy, r.Communication, r.Location, r.CheckIn, r.Value), r.Comment, clock.GetUtcNow());
        if (review.IsFailure) return review.Error!;

        db.Reviews.Add(review.Value);
        audit.Record("review.created", nameof(Review), review.Value.Id.ToString());
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (db.IsUniqueViolation(ex))
        {
            return Error.Conflict("review.duplicate", "You have already reviewed this stay.");
        }

        await RecalculatePropertyRatingAsync(reservation.PropertyId, ct);
        return (await ProjectAsync(db.Reviews.Where(x => x.Id == review.Value.Id), ct)).Single();
    }

    public async Task<PagedResult<ReviewDto>> ListForPropertyAsync(Guid propertyId, int page, int pageSize, CancellationToken ct) =>
        await PageAsync(db.Reviews.Where(r => r.PropertyId == propertyId && r.Status == ReviewStatus.Published).OrderByDescending(r => r.CreatedAt),
            page, pageSize, ct);

    public async Task<RatingSummaryDto> SummaryAsync(Guid propertyId, CancellationToken ct)
    {
        var s = await db.Reviews.AsNoTracking().Where(r => r.PropertyId == propertyId && r.Status == ReviewStatus.Published)
            .GroupBy(_ => 1)
            .Select(g => new RatingSummaryDto(g.Average(r => (double)r.Overall), g.Average(r => (double)r.Cleanliness), g.Average(r => (double)r.Accuracy),
                g.Average(r => (double)r.Communication), g.Average(r => (double)r.Location), g.Average(r => (double)r.CheckIn),
                g.Average(r => (double)r.Value), g.Count()))
            .FirstOrDefaultAsync(ct);
        return s ?? new RatingSummaryDto(0, 0, 0, 0, 0, 0, 0, 0);
    }

    public async Task<PagedResult<ReviewDto>> ListForHostAsync(int page, int pageSize, CancellationToken ct)
    {
        var hostId = currentUser.RequireUserId();
        var propertyIds = db.Properties.Where(p => p.HostId == hostId).Select(p => p.Id);
        return await PageAsync(db.Reviews.Where(r => propertyIds.Contains(r.PropertyId)).OrderByDescending(r => r.CreatedAt), page, pageSize, ct);
    }

    public async Task<PagedResult<ReviewDto>> ListAllAsync(string? status, int page, int pageSize, CancellationToken ct)
    {
        var q = db.Reviews.AsQueryable();
        if (Enum.TryParse<ReviewStatus>(status, true, out var s)) q = q.Where(r => r.Status == s);
        return await PageAsync(q.OrderByDescending(r => r.CreatedAt), page, pageSize, ct);
    }

    public async Task<Result> RespondAsync(Guid reviewId, string response, CancellationToken ct)
    {
        var review = await db.Reviews.FirstOrDefaultAsync(r => r.Id == reviewId, ct);
        if (review is null) return Error.NotFound("review.not_found", "Review not found.");
        var hostId = await db.Properties.Where(p => p.Id == review.PropertyId).Select(p => p.HostId).FirstAsync(ct);
        var result = review.Respond(currentUser.RequireUserId(), hostId, response, clock.GetUtcNow());
        if (result.IsFailure) return result;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> ModerateAsync(Guid reviewId, ModerateReviewRequest request, CancellationToken ct)
    {
        var review = await db.Reviews.FirstOrDefaultAsync(r => r.Id == reviewId, ct);
        if (review is null) return Error.NotFound("review.not_found", "Review not found.");
        review.Moderate(request.Hide, request.Note);
        audit.Record(request.Hide ? "review.hidden" : "review.restored", nameof(Review), reviewId.ToString(), new { request.Note });
        await db.SaveChangesAsync(ct);
        await RecalculatePropertyRatingAsync(review.PropertyId, ct);
        return Result.Success();
    }

    public async Task RecalculatePropertyRatingAsync(Guid propertyId, CancellationToken ct)
    {
        var stats = await db.Reviews.Where(r => r.PropertyId == propertyId && r.Status == ReviewStatus.Published)
            .GroupBy(_ => 1).Select(g => new { Avg = g.Average(r => (double)r.Overall), Count = g.Count() }).FirstOrDefaultAsync(ct);
        var property = await db.Properties.FirstOrDefaultAsync(p => p.Id == propertyId, ct);
        if (property is null) return;
        property.ApplyReviewStats(stats?.Avg ?? 0, stats?.Count ?? 0);
        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync(CacheKeys.Property(propertyId), ct);
    }

    private async Task<PagedResult<ReviewDto>> PageAsync(IOrderedQueryable<Review> query, int page, int pageSize, CancellationToken ct)
    {
        (page, pageSize) = Paging.Normalize(page, pageSize);
        var total = await query.CountAsync(ct);
        var items = await ProjectAsync(query.Skip((page - 1) * pageSize).Take(pageSize), ct);
        return new PagedResult<ReviewDto>(items, page, pageSize, total);
    }

    private Task<List<ReviewDto>> ProjectAsync(IQueryable<Review> query, CancellationToken ct) =>
        query.AsNoTracking().Select(r => new ReviewDto(r.Id, r.PropertyId,
                db.Properties.Where(p => p.Id == r.PropertyId).Select(p => p.Title).First(),
                r.GuestId,
                db.Users.Where(u => u.Id == r.GuestId).Select(u => u.DisplayName).First(),
                db.Users.Where(u => u.Id == r.GuestId).Select(u => u.AvatarUrl).First(),
                r.Overall, r.Cleanliness, r.Accuracy, r.Communication, r.Location, r.CheckIn, r.Value, r.Comment,
                r.HostResponse, r.HostRespondedAt, r.Status.ToString(), r.CreatedAt))
            .ToListAsync(ct);
}
