namespace OhMyBot.Core.Infrastructure.Messaging;

using OhMyBot.Contracts.Grpc;

public interface ICommandProgressPublisher
{
    Task PublishProgressAsync(
        BotPlatform platform,
        string botInstanceId,
        string chatId,
        string message,
        string messageKey,
        string? editMessageId = null,
        CancellationToken cancellationToken = default);
}
