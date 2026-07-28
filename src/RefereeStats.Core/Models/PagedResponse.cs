namespace RefereeStats.Core.Models;

/// <summary>Paginated response wrapper — mirrors WhistleIQ schema: content, page, size, totalElements, totalPages.</summary>
public sealed class PagedResponse<T>
{
    public IReadOnlyList<T> Content { get; init; } = [];
    public int Page { get; init; }
    public int Size { get; init; }
    public int TotalElements { get; init; }
    public int TotalPages { get; init; }

    /// <summary>Creates a paged response from a full list, applying offset/limit.</summary>
    public static PagedResponse<T> Create(IReadOnlyList<T> all, int page, int size)
    {
        var safePage = Math.Max(0, page);
        var safeSize = Math.Clamp(size, 1, 100);
        var totalElements = all.Count;
        var totalPages = (int)Math.Ceiling((double)totalElements / safeSize);

        var content = all
            .Skip(safePage * safeSize)
            .Take(safeSize)
            .ToList();

        return new PagedResponse<T>
        {
            Content = content,
            Page = safePage,
            Size = safeSize,
            TotalElements = totalElements,
            TotalPages = totalPages,
        };
    }
}
