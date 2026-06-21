using OhMyBot.Contracts.Grpc;

namespace OhMyBot.Core.Commands;

public static class CommandDsl
{
    public static string Normalize(string command)
    {
        return command.Trim().TrimStart('/').ToLowerInvariant();
    }

    public static IReadOnlyList<string> NormalizeAliases(string name, IReadOnlyList<string>? aliases)
    {
        if (aliases is null || aliases.Count == 0)
        {
            return [];
        }

        var normalizedName = Normalize(name);
        return aliases
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Select(Normalize)
            .Where(alias => alias.Length > 0 && !string.Equals(alias, normalizedName, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static SupportedPlatforms ToSupportedPlatform(BotPlatform platform)
    {
        return platform switch
        {
            BotPlatform.Telegram => SupportedPlatforms.Telegram,
            BotPlatform.Qq => SupportedPlatforms.QQ,
            _ => SupportedPlatforms.None
        };
    }

    public static SupportedChatTypes ToSupportedChatType(BotChatType chatType)
    {
        return chatType switch
        {
            BotChatType.Private => SupportedChatTypes.Private,
            BotChatType.Group => SupportedChatTypes.Group,
            _ => SupportedChatTypes.None
        };
    }
}

