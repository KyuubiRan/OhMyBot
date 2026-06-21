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
    public const string EventType = "notification.telegram";

    public static BotNotificationEvent Telegram(
        string botInstanceId,
        string chatId,
        IReadOnlyList<string> messages,
        DateTimeOffset createdAt)
    {
        return new BotNotificationEvent(EventType, BotPlatform.Telegram, botInstanceId, chatId, messages, createdAt);
    }
}
