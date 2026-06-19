using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;

namespace OhMyBot.TelegramGateway;

public sealed class TelegramUpdateHandler(
    TelegramCommandGateway commandGateway,
    TelegramResponseRenderer responseRenderer,
    IOptions<TelegramGatewayOptions> options,
    ILogger<TelegramUpdateHandler> logger) : IUpdateHandler
{
    private readonly TelegramGatewayOptions _options = options.Value;

    public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        if (update.Message is { Text: { Length: > 0 } text } message)
        {
            if (!text.StartsWith('/'))
            {
                return;
            }

            var from = message.From;
            if (from is null)
            {
                return;
            }

            var displayName = string.Join(
                ' ',
                new[] { from.FirstName, from.LastName }.Where(part => !string.IsNullOrWhiteSpace(part)));

            var response = await commandGateway.ExecuteAsync(new GatewayCommandRequest(
                message.Chat.Id.ToString(),
                from.Id.ToString(),
                message.MessageId.ToString(),
                text,
                string.IsNullOrWhiteSpace(displayName) ? null : displayName,
                from.Username), _options.BotInstanceId, cancellationToken);

            await responseRenderer.RenderAsync(message.Chat.Id, response, message.MessageId, cancellationToken);
            return;
        }

        if (update.CallbackQuery is not null)
        {
            logger.LogDebug("Ignoring Telegram callback query {CallbackQueryId}; callback routing is not implemented yet.",
                update.CallbackQuery.Id);
        }
    }

    public Task HandleErrorAsync(
        ITelegramBotClient botClient,
        Exception exception,
        HandleErrorSource source,
        CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Telegram polling error from {Source}.", source);
        return Task.CompletedTask;
    }
}
