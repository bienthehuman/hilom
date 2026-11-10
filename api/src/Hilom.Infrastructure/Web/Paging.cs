namespace Hilom.Infrastructure.Web;

/// <summary>One page of a list, as every list endpoint returns it (docs/CONVENTIONS.md §3).</summary>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

/// <summary>Paging and sorting parameters shared by list endpoints.</summary>
public static class Paging
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    public static (int Page, int PageSize) Normalize(int? page, int? pageSize) =>
        (Math.Max(1, page ?? 1), Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize));

    /// <summary>Parses <c>name</c> or <c>-name</c> into a field and direction. Unknown fields fall back to the default.</summary>
    public static (string Field, bool Descending) ParseSort(string? sort, IReadOnlyCollection<string> allowed, string fallback)
    {
        ArgumentNullException.ThrowIfNull(allowed);
        var value = sort?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (string.IsNullOrEmpty(value))
        {
            return (fallback, false);
        }

        var descending = value.StartsWith('-');
        var field = descending ? value[1..] : value;
        return allowed.Contains(field) ? (field, descending) : (fallback, false);
    }
}
