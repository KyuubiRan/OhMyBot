using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Messaging;

namespace OhMyBot.Core.Admin;

public sealed class PushMessageAdminCommand(INotificationPublisher publisher) : IAdminCommand
{
    private static readonly AdminCommandDefinition CommandDefinition = new(
        "pushmsg",
        "pushmsg -p <telegram|qq> -uid <uid> -m <message>",
        "Push a private message through a platform gateway.",
        [],
        [
            new AdminCommandOptionDefinition("-p, --platform", "target platform: telegram, qq"),
            new AdminCommandOptionDefinition("-uid, --uid", "target platform user id"),
            new AdminCommandOptionDefinition("-m, --message", "message text")
        ],
        [
            "pushmsg -p telegram -uid 123456 -m \"hello world\""
        ]);

    private static readonly IReadOnlyDictionary<string, AdminCommandOptionSpec> OptionSpecs =
        new Dictionary<string, AdminCommandOptionSpec>(StringComparer.OrdinalIgnoreCase)
        {
            ["p"] = new("platform", IsFlag: false),
            ["platform"] = new("platform", IsFlag: false),
            ["uid"] = new("uid", IsFlag: false),
            ["m"] = new("message", IsFlag: false),
            ["message"] = new("message", IsFlag: false)
        };

    public AdminCommandDefinition Definition => CommandDefinition;

    public async Task<AdminCommandResult> ExecuteAsync(IReadOnlyList<string> args, CancellationToken cancellationToken = default)
    {
        AdminCommandParseResult parsed;
        try
        {
            parsed = AdminCommandParser.ParseOptions(args, OptionSpecs);
        }
        catch (InvalidOperationException exception)
        {
            return UsageError(exception.Message);
        }

        if (!parsed.Options.TryGetValue("platform", out var platformValue) || !TryParsePlatform(platformValue, out var platform))
        {
            return UsageError("Invalid platform. Supported values: telegram, qq.");
        }

        if (!parsed.Options.TryGetValue("uid", out var uid) || string.IsNullOrWhiteSpace(uid))
        {
            return UsageError("Platform user id cannot be empty.");
        }

        if (!parsed.Options.TryGetValue("message", out var message) || string.IsNullOrWhiteSpace(message))
        {
            return UsageError("Message cannot be empty.");
        }

        await publisher.PublishAsync(platform, FormatBotInstanceId(platform), uid.Trim(), [message], cancellationToken);
        return AdminCommandResult.Ok($"Message queued: platform={FormatPlatform(platform)}, uid={uid.Trim()}.");
    }

    private AdminCommandResult UsageError(string message)
    {
        return AdminCommandResult.Error($"{message}{Environment.NewLine}{Environment.NewLine}usage: {Definition.Usage}");
    }

    private static bool TryParsePlatform(string value, out BotPlatform platform)
    {
        platform = value.Trim().ToLowerInvariant() switch
        {
            "telegram" or "tg" => BotPlatform.Telegram,
            "qq" => BotPlatform.Qq,
            _ => BotPlatform.Unspecified
        };

        return platform is not BotPlatform.Unspecified;
    }

    private static string FormatPlatform(BotPlatform platform)
    {
        return platform switch
        {
            BotPlatform.Telegram => "telegram",
            BotPlatform.Qq => "qq",
            _ => "unspecified"
        };
    }

    private static string FormatBotInstanceId(BotPlatform platform)
    {
        return platform switch
        {
            BotPlatform.Telegram => "telegram-default",
            BotPlatform.Qq => "qq-default",
            _ => "default"
        };
    }
}
