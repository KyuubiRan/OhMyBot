namespace OhMyBot.Core.Messaging;

public interface INotificationPublisher
{
    Task PublishTelegramAsync(
        string botInstanceId,
        string chatId,
        IReadOnlyList<string> messages,
        CancellationToken cancellationToken = default);
}
