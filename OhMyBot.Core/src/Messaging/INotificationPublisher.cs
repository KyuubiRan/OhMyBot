namespace OhMyBot.Core.Messaging;

using OhMyBot.Contracts.Grpc;

public interface INotificationPublisher
{
    Task PublishAsync(
        BotPlatform platform,
        string botInstanceId,
        string chatId,
        IReadOnlyList<string> messages,
        CancellationToken cancellationToken = default);

    Task PublishTelegramAsync(
        string botInstanceId,
        string chatId,
        IReadOnlyList<string> messages,
        CancellationToken cancellationToken = default);
}
