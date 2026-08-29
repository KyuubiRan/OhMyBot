using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Infrastructure.Messaging;

namespace OhMyBot.Core.Commanding.Commands;

internal sealed class CommandProgressReporter(
    CommandRequest request,
    ICommandProgressPublisher publisher,
    string? editMessageId = null) : ICommandProgressReporter
{
    private readonly string _messageKey = "command-progress:" + Guid.NewGuid().ToString("N");
    private int _hasReported;

    public bool HasReported => Volatile.Read(ref _hasReported) != 0;

    public async Task ReportAsync(string message, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        await publisher.PublishProgressAsync(
            request.Platform,
            request.BotInstanceId,
            request.ChatId,
            message,
            _messageKey,
            editMessageId,
            cancellationToken);
        Interlocked.Exchange(ref _hasReported, 1);
    }

    public CommandResponse ApplyTo(CommandResponse response)
    {
        if (!HasReported
            || request.Platform != BotPlatform.Telegram
            || !string.IsNullOrWhiteSpace(editMessageId))
        {
            return response;
        }

        return response.AsTelegramEdit(_messageKey);
    }
}
