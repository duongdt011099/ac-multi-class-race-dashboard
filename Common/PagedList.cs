namespace MulticlassRace.Common;

/// <summary>One page of an already filtered and sorted sequence.</summary>
public sealed class PagedList<T>
{
    private PagedList(IReadOnlyList<T> items, int page, int pageSize, int totalCount)
    {
        Items = items;
        Page = page;
        PageSize = pageSize;
        TotalCount = totalCount;
    }

    public IReadOnlyList<T> Items { get; }

    /// <summary>The page actually shown, after clamping the requested page into range.</summary>
    public int Page { get; }

    public int PageSize { get; }

    public int TotalCount { get; }

    public int TotalPages => TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    /// <summary>Zero-based position of the first item, for numbering rows across pages.</summary>
    public int FirstIndex => (Page - 1) * PageSize;

    public static PagedList<T> Create(IEnumerable<T> source, int requestedPage, int pageSize)
    {
        if (pageSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        }

        var all = source as IReadOnlyList<T> ?? source.ToList();
        var totalPages = all.Count == 0 ? 1 : (int)Math.Ceiling(all.Count / (double)pageSize);
        var page = Math.Clamp(requestedPage, 1, totalPages);

        var items = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new PagedList<T>(items, page, pageSize, all.Count);
    }
}
