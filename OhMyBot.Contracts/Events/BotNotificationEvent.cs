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

    /// <summary>
    /// 与 <see cref="Messages"/> 同序的 QQ 编号菜单 token（<c>QqMessage.menu_token</c>）；
    /// 元素为空表示该条消息没有菜单。QQ 网关发出消息后据此调 BindQqMenu 绑定选项，
    /// 让主动推送的通知也能被回复序号选择。其它平台忽略本字段。
    /// </summary>
    public IReadOnlyList<string>? MenuTokens { get; init; }

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
        DateTimeOffset createdAt,
        IReadOnlyList<string>? menuTokens = null)
    {
        return new BotNotificationEvent(GetEventType(platform), platform, botInstanceId, chatId, messages, createdAt)
        {
            MenuTokens = menuTokens
        };
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
