namespace OhMyBot.Core.Commanding.Presentation;

/// <summary>
/// 面板正文的拼接机制。空值占位符、分隔符等文案一律由调用方传入，
/// 以保持「不在 Core 拼平台最终文案」的边界。
/// </summary>
public static class TextLayout
{
    /// <summary>用 <paramref name="separator"/> 连接标签；序列为空时返回 <paramref name="emptyText"/>。</summary>
    public static string JoinOrEmpty(IEnumerable<string> values, string separator, string emptyText)
    {
        var array = values as string[] ?? [.. values];
        return array.Length == 0 ? emptyText : string.Join(separator, array);
    }

    /// <summary>按行拼接，跳过 null——用于条件行（例如仅国服账号才有的社区任务行）。</summary>
    public static string JoinLines(params string?[] lines)
    {
        return string.Join('\n', lines.Where(line => line is not null));
    }
}
