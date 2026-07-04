using System.Text;

namespace OhMyBot.Core.Commanding.Presentation;

/// <summary>
/// Telegram MarkdownV2 文本工具。渲染从网关移入 Core 后，转义逻辑集中在这里，
/// 供各平台 presenter 复用（原先分散在 TelegramGateway 的各 renderer 内）。
/// </summary>
public static class MarkdownV2
{
    /// <summary>转义 MarkdownV2 普通文本中的全部保留字符。</summary>
    public static string Escape(string value)
    {
        return value
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal)
            .Replace("*", @"\*", StringComparison.Ordinal)
            .Replace("[", @"\[", StringComparison.Ordinal)
            .Replace("]", @"\]", StringComparison.Ordinal)
            .Replace("(", @"\(", StringComparison.Ordinal)
            .Replace(")", @"\)", StringComparison.Ordinal)
            .Replace("~", @"\~", StringComparison.Ordinal)
            .Replace("`", @"\`", StringComparison.Ordinal)
            .Replace(">", @"\>", StringComparison.Ordinal)
            .Replace("#", @"\#", StringComparison.Ordinal)
            .Replace("+", @"\+", StringComparison.Ordinal)
            .Replace("-", @"\-", StringComparison.Ordinal)
            .Replace("=", @"\=", StringComparison.Ordinal)
            .Replace("|", @"\|", StringComparison.Ordinal)
            .Replace("{", @"\{", StringComparison.Ordinal)
            .Replace("}", @"\}", StringComparison.Ordinal)
            .Replace(".", @"\.", StringComparison.Ordinal)
            .Replace("!", @"\!", StringComparison.Ordinal);
    }

    /// <summary>转义 code span（反引号）内部内容：只需处理 <c>\</c> 与 <c>`</c>。</summary>
    public static string Code(string value)
    {
        return value
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("`", @"\`", StringComparison.Ordinal);
    }

    /// <summary>把内容包成 code span。</summary>
    public static string CodeSpan(string value) => $"`{Code(value)}`";

    /// <summary>
    /// 把带 <c>`反引号`</c> 标记的文本渲染成合法 MarkdownV2：反引号外的部分整体转义，
    /// 反引号内的部分作为 code span。用于源文案里用反引号表示等宽片段的场景。
    /// </summary>
    public static string Inline(string text)
    {
        var parts = text.Split('`');
        for (var index = 0; index < parts.Length; index++)
        {
            parts[index] = index % 2 == 0
                ? Escape(parts[index])
                : $"`{Code(parts[index])}`";
        }

        return string.Concat(parts);
    }

    /// <summary>去掉源文案里的反引号标记，得到纯文本（QQ 等无 Markdown 平台使用）。</summary>
    public static string StripMarks(string text) => text.Replace("`", string.Empty, StringComparison.Ordinal);

    /// <summary>
    /// 把已渲染成 MarkdownV2 的文本还原为纯文本：去掉保留字符的反斜杠转义（<c>\X</c> → <c>X</c>）并移除反引号。
    /// 供 QQ 菜单转换器复用 Telegram 面板文本时使用（对未转义/纯文本输入是安全的近似幂等操作）。
    /// </summary>
    public static string ToPlain(string text)
    {
        var builder = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            if (current == '\\' && index + 1 < text.Length && IsReserved(text[index + 1]))
            {
                builder.Append(text[index + 1]);
                index++;
                continue;
            }

            if (current == '`')
            {
                continue;
            }

            builder.Append(current);
        }

        return builder.ToString();
    }

    private static bool IsReserved(char value)
    {
        return value is '\\' or '_' or '*' or '[' or ']' or '(' or ')' or '~' or '`'
            or '>' or '#' or '+' or '-' or '=' or '|' or '{' or '}' or '.' or '!';
    }
}
