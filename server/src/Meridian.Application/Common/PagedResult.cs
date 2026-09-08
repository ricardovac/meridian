namespace Meridian.Application.Common;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public static class Paging
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public static (int Page, int PageSize) Normalize(int? page, int? pageSize)
    {
        var p = page.GetValueOrDefault(1);
        var size = pageSize.GetValueOrDefault(DefaultPageSize);
        return (Math.Max(p, 1), Math.Clamp(size, 1, MaxPageSize));
    }
}
