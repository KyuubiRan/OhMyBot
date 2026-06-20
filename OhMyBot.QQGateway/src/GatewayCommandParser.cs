namespace OhMyBot.QQGateway;

internal static class GatewayCommandParser
{
    public static readonly string[] DefaultPrefixes = ["/", "!", "."];

    public static (string Command, string[] Args) Parse(string text, IReadOnlyCollection<string> prefixes)
    {
        var normalizedText = text.Trim();
        if (normalizedText.Length == 0)
        {
            return (string.Empty, []);
        }

        var normalizedPrefixes = NormalizePrefixes(prefixes);
        if (!TryFindPrefix(normalizedText, normalizedPrefixes, out var prefix))
        {
            return (string.Empty, []);
        }

        var parts = normalizedText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return (string.Empty, []);
        }

        return (parts[0][prefix.Length..].ToLowerInvariant(), parts.Skip(1).ToArray());
    }

    public static string NormalizeRouteKey(string value, IReadOnlyCollection<string> prefixes)
    {
        var routeKey = value.Trim();
        var normalizedPrefixes = NormalizePrefixes(prefixes);
        if (TryFindPrefix(routeKey, normalizedPrefixes, out var prefix))
        {
            routeKey = routeKey[prefix.Length..];
        }

        return routeKey.ToLowerInvariant();
    }

    public static string[] NormalizePrefixes(IReadOnlyCollection<string>? prefixes)
    {
        var normalized = (prefixes is { Count: > 0 } ? prefixes : DefaultPrefixes)
            .Where(prefix => !string.IsNullOrWhiteSpace(prefix))
            .Select(prefix => prefix.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(prefix => prefix.Length)
            .ToArray();

        return normalized.Length == 0 ? DefaultPrefixes : normalized;
    }

    private static bool TryFindPrefix(string text, IReadOnlyCollection<string> prefixes, out string prefix)
    {
        prefix = prefixes.FirstOrDefault(prefix => text.StartsWith(prefix, StringComparison.Ordinal)) ?? string.Empty;
        return prefix.Length > 0;
    }
}
