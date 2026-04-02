using System.Collections.Frozen;
using System.Text;

namespace OhMyOneBot.V11.Lib.CQ;

public static class CQCodeSerializer
{
    internal static readonly FrozenDictionary<char, string> EscapeChars = new Dictionary<char, string>
    {
        { '&', "&amp;" },
        { '[', "&#91;" },
        { ']', "&#93;" },
        { ',', "&#44;" }, // Only For param values
    }.ToFrozenDictionary();

    internal static readonly FrozenDictionary<string, char> EscapeCharsReversed =
        EscapeChars.Select(kv => new KeyValuePair<string, char>(kv.Value, kv.Key)).ToFrozenDictionary();

    public static string Escape(string input, bool isParamValue = false)
    {
        return EscapeChars.Where(kv => isParamValue || kv.Key != ',').Aggregate(input, (current, kv) => current.Replace(kv.Key.ToString(), kv.Value));
    }

    public static string Serialize(CQCode code)
    {
        var sb = new StringBuilder("[CQ:");
        sb.Append(code.Type);

        if (code.Parameters.Count <= 0) 
            return sb.Append(']').ToString();
        
        sb.Append(',');
        sb.Append(string.Join(",", code.Parameters.Select(kv =>
        {
            if (kv.Key.IsWhiteSpace() || kv.Value.IsWhiteSpace())
                throw new FormatException($"Invalid parameter format: {kv.Key}");
            return $"{kv.Key}={Escape(kv.Value, true)}";
        })));

        return sb.Append(']').ToString();
    }

    public static CQCode Deserialize(string code)
    {
        if (!code.StartsWith("[CQ:") && !code.EndsWith(']'))
            throw new FormatException("Invalid CQ code format.");

        var content = code[4..];
        var typeIndex = content.IndexOf(',');

        var hasParam = true;
        if (typeIndex == -1)
        {
            hasParam = false;
            typeIndex = content.IndexOf(']');
        }

        if (typeIndex == -1)
            throw new FormatException("Invalid CQ code format, cannot parse CQ code type.");

        var type = content[..typeIndex];

        if (!hasParam)
            return new CQCode { Type = type };

        var @params = content[(typeIndex + 1)..^1].Split(',').Select(p =>
        {
            var kv = p.Split('=', 2);
            if (kv.Length != 2)
                throw new FormatException($"Invalid CQ code parameter format: {p}");

            var key = kv[0];
            var value = kv[1];
            value = EscapeCharsReversed.Aggregate(value, (current, c) => current.Replace(c.Key, c.Value.ToString()));

            return new KeyValuePair<string, string>(key.Trim(), value.Trim());
        }).ToDictionary(kv => kv.Key, kv => kv.Value);

        return new CQCode { Type = type, Parameters = @params };
    }
}