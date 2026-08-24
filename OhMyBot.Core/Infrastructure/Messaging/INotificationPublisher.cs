namespace OhMyBot.Core.Infrastructure.Messaging;

using OhMyBot.Contracts.Grpc;

public interface INotificationPublisher
{
    /// <param name="menuTokens">
    /// 与 <paramref name="messages"/> 同序的 QQ 编号菜单 token；元素为空表示该条无菜单。
    /// 仅 QQ 平台有效，用于让主动推送的消息也能被回复序号选择。
    /// </param>
    Task PublishAsync(
        BotPlatform platform,
        string botInstanceId,
        string chatId,
        IReadOnlyList<string> messages,
        IReadOnlyList<string>? menuTokens = null,
        CancellationToken cancellationToken = default);

    Task PublishTelegramAsync(
        string botInstanceId,
        string chatId,
        IReadOnlyList<string> messages,
        CancellationToken cancellationToken = default);
}
