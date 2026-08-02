namespace OhMyBot.Core.Commanding.Presentation;

/// <summary>
/// 面板分页的纯算法：页数换算、页码归一化、当前页切片。
/// 不含任何面向用户的文案——「第 N/M 页」这类措辞属于插件，由各插件自行拼接。
/// </summary>
public static class Pagination
{
    /// <summary>总页数；空集合也返回 1，保证「第 1/1 页」成立。</summary>
    public static int TotalPages(int totalCount, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        return Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
    }

    /// <summary>把（可能越界的）页码夹到 [0, 总页数-1]。</summary>
    public static int NormalizePage(int page, int totalCount, int pageSize)
    {
        return Math.Clamp(page, 0, TotalPages(totalCount, pageSize) - 1);
    }

    /// <summary>取当前页的条目。</summary>
    public static IEnumerable<T> Slice<T>(IEnumerable<T> items, int page, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        return items.Skip(page * pageSize).Take(pageSize);
    }
}
