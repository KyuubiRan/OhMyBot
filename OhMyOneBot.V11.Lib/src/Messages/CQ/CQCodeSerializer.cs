using System.Text;

namespace OhMyOneBot.V11.Lib.Messages.CQ;

public static class CQCodeSerializer
{
    public static string Escape(string input, bool isParamValue = false)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var span = input.AsSpan();
        StringBuilder? sb = null;

        for (var i = 0; i < span.Length; i++)
        {
            var escaped = GetEscapeSequence(span[i], isParamValue);
            if (escaped is null)
            {
                sb?.Append(span[i]);
                continue;
            }

            sb ??= new StringBuilder(input.Length + 8).Append(span[..i]);
            sb.Append(escaped);
        }

        return sb?.ToString() ?? input;
    }

    public static string Serialize(CQCode code)
    {
        var sb = new StringBuilder(16);
        sb.Append("[CQ:");
        sb.Append(code.Type);

        if (code.Parameters.Count == 0)
        {
            sb.Append(']');
            return sb.ToString();
        }

        foreach (var kv in code.Parameters)
        {
            if (string.IsNullOrWhiteSpace(kv.Key) || string.IsNullOrWhiteSpace(kv.Value))
                throw new FormatException($"Invalid parameter format: {kv.Key}");

            sb.Append(',');
            sb.Append(kv.Key);
            sb.Append('=');
            AppendEscaped(sb, kv.Value.AsSpan(), true);
        }

        sb.Append(']');
        return sb.ToString();
    }

    public static CQCode Deserialize(string code)
    {
        if (string.IsNullOrEmpty(code))
            throw new FormatException("Invalid CQ code format.");

        var span = code.AsSpan();

        if (!span.StartsWith("[CQ:".AsSpan()) || span[^1] != ']')
            throw new FormatException("Invalid CQ code format.");

        // 去掉前缀 [CQ: 和末尾 ]
        var content = span[4..^1];

        var commaIndex = content.IndexOf(',');
        if (commaIndex < 0)
        {
            if (content.IsEmpty || content.IsWhiteSpace())
                throw new FormatException("Invalid CQ code format, cannot parse CQ code type.");

            return new CQCode
            {
                Type = content.ToString()
            };
        }

        var typeSpan = content[..commaIndex];
        if (typeSpan.IsEmpty || content.IsWhiteSpace())
            throw new FormatException("Invalid CQ code format, cannot parse CQ code type.");

        var parameters = ParseParameters(content[(commaIndex + 1)..]);

        return new CQCode
        {
            Type = typeSpan.ToString(),
            Parameters = parameters
        };
    }

    private static Dictionary<string, string> ParseParameters(ReadOnlySpan<char> span)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);

        var pos = 0;
        while (pos < span.Length)
        {
            var pairEnd = span[pos..].IndexOf(',');
            ReadOnlySpan<char> pair;

            if (pairEnd < 0)
            {
                pair = span[pos..];
                pos = span.Length;
            }
            else
            {
                pair = span.Slice(pos, pairEnd);
                pos += pairEnd + 1;
            }

            if (pair.IsEmpty)
                throw new FormatException("Invalid CQ code parameter format: empty parameter.");

            var eqIndex = pair.IndexOf('=');
            if (eqIndex <= 0 || eqIndex == pair.Length - 1)
                throw new FormatException($"Invalid CQ code parameter format: {pair.ToString()}");

            var keySpan = pair[..eqIndex].Trim();
            var valueSpan = pair[(eqIndex + 1)..].Trim();

            if (keySpan.IsEmpty || valueSpan.IsEmpty)
                throw new FormatException($"Invalid CQ code parameter format: {pair.ToString()}");

            var key = keySpan.ToString();
            var value = Unescape(valueSpan);

            dict.Add(key, value);
        }

        return dict;
    }

    private static string Unescape(ReadOnlySpan<char> span)
    {
        if (span.IndexOf('&') < 0)
            return span.ToString();

        StringBuilder? sb = null;

        for (var i = 0; i < span.Length; i++)
        {
            if (span[i] != '&')
            {
                sb?.Append(span[i]);
                continue;
            }

            if (TryMatchEscape(span[i..], out var ch, out var consumed))
            {
                sb ??= new StringBuilder(span.Length).Append(span[..i]);
                sb.Append(ch);
                i += consumed - 1;
                continue;
            }

            sb?.Append(span[i]);
        }

        return sb?.ToString() ?? span.ToString();
    }

    private static void AppendEscaped(StringBuilder sb, ReadOnlySpan<char> span, bool isParamValue)
    {
        foreach (var t in span)
        {
            var escaped = GetEscapeSequence(t, isParamValue);
            if (escaped is null)
                sb.Append(t);
            else
                sb.Append(escaped);
        }
    }

    private static string? GetEscapeSequence(char ch, bool isParamValue)
    {
        return ch switch
        {
            '&' => "&amp;",
            '[' => "&#91;",
            ']' => "&#93;",
            ',' when isParamValue => "&#44;",
            _ => null
        };
    }

    private static bool TryMatchEscape(ReadOnlySpan<char> span, out char ch, out int consumed)
    {
        // &amp;
        if (span.StartsWith("&amp;".AsSpan()))
        {
            ch = '&';
            consumed = 5;
            return true;
        }

        // &#91;
        if (span.StartsWith("&#91;".AsSpan()))
        {
            ch = '[';
            consumed = 5;
            return true;
        }

        // &#93;
        if (span.StartsWith("&#93;".AsSpan()))
        {
            ch = ']';
            consumed = 5;
            return true;
        }

        // &#44;
        if (span.StartsWith("&#44;".AsSpan()))
        {
            ch = ',';
            consumed = 5;
            return true;
        }

        ch = '\0';
        consumed = 0;
        return false;
    }
}