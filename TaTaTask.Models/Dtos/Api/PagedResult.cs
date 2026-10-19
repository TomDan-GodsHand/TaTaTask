namespace TaTaTask.Models.Dtos.Api;

/// <summary>对外接口统一的分页信封。</summary>
public class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();

    /// <summary>当前页码，从 1 开始。</summary>
    public int Page { get; init; }

    public int PageSize { get; init; }

    /// <summary>符合筛选条件的总条数（不是当前页条数）。</summary>
    public int Total { get; init; }

    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(Total / (double)PageSize);

    public bool HasNext => Page < TotalPages;

    public bool HasPrevious => Page > 1;

    public static PagedResult<T> Create(IReadOnlyList<T> items, int page, int pageSize, int total) => new()
    {
        Items = items,
        Page = page,
        PageSize = pageSize,
        Total = total,
    };
}

/// <summary>分页参数归一化：页码从 1 开始，每页默认 20 条、上限 100 条。</summary>
public static class PageQuery
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public static (int Page, int PageSize) Normalize(int? page, int? pageSize)
    {
        if (page is < 1 or > 1000000) throw new ArgumentException("page 需为 1–1000000");
        if (pageSize is < 1 or > MaxPageSize) throw new ArgumentException("pageSize 需为 1–100");
        var p = page ?? 1;
        var size = pageSize ?? DefaultPageSize;
        return (p, size);
    }
}
