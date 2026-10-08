namespace StaySphere.Contracts;

/// <summary>Offset page (admin tables, lists). Search additionally returns <see cref="NextCursor"/> for cursor paging.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, string? NextCursor = null)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public sealed record IdResponse(Guid Id);
