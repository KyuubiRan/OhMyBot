using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OhMyBot.Contracts.Grpc;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

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
            var from = message.From;
            if (from is null)
            {
                return;
            }

            var displayName = string.Join(
                ' ',
                new[] { from.LastName, from.FirstName }.Where(part => !string.IsNullOrWhiteSpace(part)));

            var gatewayRequest = new GatewayCommandRequest(
                message.Chat.Id.ToString(),
                from.Id.ToString(),
                message.MessageId.ToString(),
                text,
                string.IsNullOrWhiteSpace(displayName) ? null : displayName,
                from.Username,
                ToChatType(message.Chat.Type),
                from.FirstName,
                from.LastName,
                ReplyToUserId: message.ReplyToMessage?.From?.Id.ToString(),
                TextMentionUserId: message.GetFirstCommandArgumentTextMentionUserId());

            await commandGateway.RecordUserProfileAsync(gatewayRequest, _options.BotInstanceId, cancellationToken);

            if (!GatewayCommandParser.IsCommand(text, _options.CommandPrefixes))
            {
                return;
            }

            var response = await commandGateway.ExecuteAsync(gatewayRequest, _options.BotInstanceId, cancellationToken);

            await responseRenderer.RenderAsync(message.Chat.Id, response, message.MessageId, cancellationToken);
            return;
        }

        if (update.CallbackQuery is not null)
        {
            var query = update.CallbackQuery;
            if (query.Message is null || string.IsNullOrWhiteSpace(query.Data))
            {
                return;
            }

            var response = await commandGateway.ExecuteCallbackAsync(new CallbackRequest
            {
                Platform = BotPlatform.Telegram,
                BotInstanceId = _options.BotInstanceId,
                ChatId = query.Message.Chat.Id.ToString(),
                UserId = query.From.Id.ToString(),
                MessageId = query.Message.MessageId.ToString(),
                CallbackQueryId = query.Id,
                Payload = query.Data
            }, cancellationToken);

            if (!string.IsNullOrWhiteSpace(response.CallbackAnswerText))
            {
                await botClient.AnswerCallbackQuery(
                    query.Id,
                    response.CallbackAnswerText,
                    showAlert: response.CallbackAnswerAlert,
                    cancellationToken: cancellationToken);
            }
            else
            {
                await botClient.AnswerCallbackQuery(query.Id, cancellationToken: cancellationToken);
            }

            await responseRenderer.RenderAsync(query.Message.Chat.Id, response, null, cancellationToken);
        }
    }

    private static BotChatType ToChatType(ChatType chatType)
    {
        return chatType switch
        {
            ChatType.Private => BotChatType.Private,
            ChatType.Group or ChatType.Supergroup => BotChatType.Group,
            _ => BotChatType.Unspecified
        };
    }

    public Task HandleErrorAsync(
        ITelegramBotClient botClient,
        Exception exception,
        HandleErrorSource source,
        CancellationToken cancellationToken)
    {
        if (source == HandleErrorSource.PollingError)
        {
            return Task.CompletedTask;
        }

        logger.LogError(exception, "Telegram polling error from {Source}.", source);
        return Task.CompletedTask;
    }
}
