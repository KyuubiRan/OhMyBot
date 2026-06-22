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

            if (!GatewayCommandParser.IsCommand(text, _options.CommandPrefixes))
            {
                _ = Task.Run(
                    () => RecordUserProfileSafeAsync(gatewayRequest, cancellationToken),
                    CancellationToken.None);
                return;
            }

            if (!commandGateway.CanHandle(text))
            {
                _ = Task.Run(
                    () => RecordUserProfileSafeAsync(gatewayRequest, cancellationToken),
                    CancellationToken.None);
                return;
            }

            if (!IsSignInCommand(text))
            {
                await ExecuteMessageCommandAsync(message, null, gatewayRequest, text, cancellationToken);
                return;
            }

            var processingMessage = await botClient.SendMessage(
                message.Chat.Id,
                "正在签到...",
                replyParameters: new ReplyParameters
                {
                    MessageId = message.MessageId,
                    AllowSendingWithoutReply = true
                },
                cancellationToken: cancellationToken);

            _ = Task.Run(
                () => ExecuteMessageCommandAsync(message, processingMessage.MessageId, gatewayRequest, text, cancellationToken),
                CancellationToken.None);
            return;
        }

        if (update.CallbackQuery is not null)
        {
            var query = update.CallbackQuery;
            if (query.Message is null || string.IsNullOrWhiteSpace(query.Data))
            {
                return;
            }

            var callbackRequest = new CallbackRequest
            {
                Platform = BotPlatform.Telegram,
                BotInstanceId = _options.BotInstanceId,
                ChatId = query.Message.Chat.Id.ToString(),
                UserId = query.From.Id.ToString(),
                MessageId = query.Message.MessageId.ToString(),
                CallbackQueryId = query.Id,
                Payload = query.Data
            };

            await botClient.AnswerCallbackQuery(query.Id, "正在处理...", cancellationToken: cancellationToken);

            _ = Task.Run(
                () => ExecuteCallbackAsync(query.Message.Chat.Id, callbackRequest, cancellationToken),
                CancellationToken.None);
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

    private async Task RecordCommandTargetProfileAsync(
        Message message,
        string text,
        CancellationToken cancellationToken)
    {
        var (command, _) = GatewayCommandParser.Parse(text, _options.CommandPrefixes, stripBotMention: true);
        if (!string.Equals(command, "info", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(command, "setpriv", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var target = message.GetFirstCommandArgumentTextMentionUser() ?? message.ReplyToMessage?.From;
        if (target is null)
        {
            return;
        }

        await commandGateway.RecordUserProfileAsync(
            new GatewayCommandRequest(
                message.Chat.Id.ToString(),
                target.Id.ToString(),
                message.MessageId.ToString(),
                text,
                Username: target.Username,
                ChatType: ToChatType(message.Chat.Type),
                FirstName: target.FirstName,
                LastName: target.LastName),
            _options.BotInstanceId,
            cancellationToken);
    }

    private async Task ExecuteMessageCommandAsync(
        Message message,
        int? processingMessageId,
        GatewayCommandRequest gatewayRequest,
        string text,
        CancellationToken cancellationToken)
    {
        try
        {
            await RecordUserProfileSafeAsync(gatewayRequest, cancellationToken);
            await RecordCommandTargetProfileAsync(message, text, cancellationToken);
            var response = await commandGateway.ExecuteAsync(gatewayRequest, _options.BotInstanceId, cancellationToken);
            if (processingMessageId is not null && string.IsNullOrWhiteSpace(response.EditMessageId))
            {
                response.EditMessageId = processingMessageId.Value.ToString();
                response.ReplyToMessageId = string.Empty;
            }

            await responseRenderer.RenderAsync(message.Chat.Id, response, message.MessageId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Failed to execute Telegram command in background.");
            await RenderFailureSafeAsync(message.Chat.Id, processingMessageId, message.MessageId, exception, cancellationToken);
        }
    }

    private async Task ExecuteCallbackAsync(
        ChatId chatId,
        CallbackRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await commandGateway.ExecuteCallbackAsync(request, cancellationToken);
            await responseRenderer.RenderAsync(chatId, response, null, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Failed to execute Telegram callback in background.");
            if (int.TryParse(request.MessageId, out var messageId))
            {
                await RenderFailureSafeAsync(chatId, messageId, exception, cancellationToken);
            }
        }
    }

    private Task RenderFailureSafeAsync(
        ChatId chatId,
        int editMessageId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        return RenderFailureSafeAsync(chatId, editMessageId, null, exception, cancellationToken);
    }

    private async Task RenderFailureSafeAsync(
        ChatId chatId,
        int? editMessageId,
        int? fallbackReplyToMessageId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        try
        {
            await responseRenderer.RenderAsync(
                chatId,
                new CommandResponse
                {
                    Code = 1,
                    ErrorCode = "GatewayExecutionFailed",
                    Message = "执行失败：" + exception.GetBaseException().Message,
                    EditMessageId = editMessageId?.ToString() ?? string.Empty,
                    ReplyToMessageId = string.Empty
                },
                fallbackReplyToMessageId,
                cancellationToken);
        }
        catch (Exception renderException) when (renderException is not OperationCanceledException)
        {
            logger.LogError(renderException, "Failed to render Telegram failure message.");
        }
    }

    private async Task RecordUserProfileSafeAsync(
        GatewayCommandRequest gatewayRequest,
        CancellationToken cancellationToken)
    {
        try
        {
            await commandGateway.RecordUserProfileAsync(gatewayRequest, _options.BotInstanceId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogDebug(exception, "Failed to record Telegram user profile.");
        }
    }

    private bool IsSignInCommand(string text)
    {
        var (_, args) = GatewayCommandParser.Parse(
            text,
            _options.CommandPrefixes,
            stripBotMention: true);

        return args.Any(arg => string.Equals(arg, "signin", StringComparison.OrdinalIgnoreCase));
    }
}
