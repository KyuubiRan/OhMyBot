namespace OhMyBot.Core.Admin;

public static class AdminCommandParser
{
    public static IReadOnlyList<string> Tokenize(string commandLine)
    {
        var tokens = new List<string>();
        var current = new List<char>();
        var inQuotes = false;
        var escaping = false;

        foreach (var character in commandLine)
        {
            if (escaping)
            {
                current.Add(character);
                escaping = false;
                continue;
            }

            if (character == '\\' && inQuotes)
            {
                escaping = true;
                continue;
            }

            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                AddCurrentToken(tokens, current);
                continue;
            }

            current.Add(character);
        }

        if (escaping)
        {
            current.Add('\\');
        }

        AddCurrentToken(tokens, current);
        return tokens;
    }

    public static AdminCommandParseResult ParseOptions(
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, AdminCommandOptionSpec> optionSpecs)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < args.Count; index++)
        {
            var token = args[index];
            if (!token.StartsWith('-') || token.Length < 2)
            {
                throw new InvalidOperationException($"Unexpected argument '{token}'.");
            }

            var separatorIndex = token.IndexOf('=');
            var rawName = separatorIndex > 0 ? token[..separatorIndex] : token;
            var valueFromAssignment = separatorIndex > 0 ? token[(separatorIndex + 1)..] : null;
            var alias = rawName.TrimStart('-');

            if (!optionSpecs.TryGetValue(alias, out var spec))
            {
                throw new InvalidOperationException($"Unknown option '{rawName}'.");
            }

            if (spec.IsFlag)
            {
                if (valueFromAssignment is not null)
                {
                    throw new InvalidOperationException($"Option '{rawName}' does not accept a value.");
                }

                flags.Add(spec.Name);
                continue;
            }

            var value = valueFromAssignment;
            if (value is null)
            {
                if (index + 1 >= args.Count || args[index + 1].StartsWith('-'))
                {
                    throw new InvalidOperationException($"Option '{rawName}' requires a value.");
                }

                value = args[index + 1];
                index++;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Option '{rawName}' requires a value.");
            }

            options[spec.Name] = value;
        }

        return new AdminCommandParseResult(options, flags);
    }

    private static void AddCurrentToken(List<string> tokens, List<char> current)
    {
        if (current.Count == 0)
        {
            return;
        }

        tokens.Add(new string(current.ToArray()));
        current.Clear();
    }
}

public sealed record AdminCommandOptionSpec(string Name, bool IsFlag);

public sealed record AdminCommandParseResult(
    IReadOnlyDictionary<string, string> Options,
    IReadOnlySet<string> Flags);
