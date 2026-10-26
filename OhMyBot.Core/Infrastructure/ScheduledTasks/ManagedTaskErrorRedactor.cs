using System.Text.RegularExpressions;

namespace OhMyBot.Core.Infrastructure.ScheduledTasks;

public static partial class ManagedTaskErrorRedactor
{
    public static string Redact(string value)
    {
        value = UrlPattern().Replace(value, match =>
        {
            if (!Uri.TryCreate(match.Value, UriKind.Absolute, out var uri)) return "[URL]";
            var builder = new UriBuilder(uri) { UserName = "", Password = "", Query = "", Fragment = "" };
            return builder.Uri.GetLeftPart(UriPartial.Path) + (uri.Query.Length > 0 ? "?[REDACTED]" : "");
        });
        value = TelegramTokenPattern().Replace(value, "bot[REDACTED]");
        value = BearerPattern().Replace(value, "$1[REDACTED]");
        return SecretPattern().Replace(value, "$1[REDACTED]");
    }

    [GeneratedRegex("https?://[^\\s\\\"<>]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    [GeneratedRegex("bot[0-9]+:[A-Za-z0-9_-]+", RegexOptions.IgnoreCase)]
    private static partial Regex TelegramTokenPattern();

    [GeneratedRegex("(Bearer\\s+)[A-Za-z0-9._~+/-]+", RegexOptions.IgnoreCase)]
    private static partial Regex BearerPattern();

    [GeneratedRegex("([A-Za-z0-9_-]*(?:password|passwd|token|secret|cookie|authorization|api[-_]?key|access[-_]?key|cred)[A-Za-z0-9_-]*\\s*[\\\"']?\\s*[:=]\\s*)[^\\r\\n]+", RegexOptions.IgnoreCase)]
    private static partial Regex SecretPattern();
}
