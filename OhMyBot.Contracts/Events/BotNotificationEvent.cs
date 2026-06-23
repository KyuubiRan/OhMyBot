using OhMyBot.Contracts.Grpc;

namespace OhMyBot.Contracts.Events;

public sealed record BotNotificationEvent(
    string Type,
    BotPlatform Platform,
    string BotInstanceId,
    string ChatId,
    IReadOnlyList<string> Messages,
    DateTimeOffset CreatedAt)
{
    public const string TelegramEventType = "notification.telegram";
    public const string QqEventType = "notification.qq";
    public const string EventType = TelegramEventType;

    public static string GetEventType(BotPlatform platform)
    {
        return platform switch
        {
            BotPlatform.Telegram => TelegramEventType,
            BotPlatform.Qq => QqEventType,
            _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unsupported notification platform.")
        };
    }

    public static BotNotificationEvent Create(
        BotPlatform platform,
        string botInstanceId,
        string chatId,
        IReadOnlyList<string> messages,
        DateTimeOffset createdAt)
    {
        return new BotNotificationEvent(GetEventType(platform), platform, botInstanceId, chatId, messages, createdAt);
    }

    public static BotNotificationEvent Telegram(
        string botInstanceId,
        string chatId,
        IReadOnlyList<string> messages,
        DateTimeOffset createdAt)
    {
        return Create(BotPlatform.Telegram, botInstanceId, chatId, messages, createdAt);
    }
}
