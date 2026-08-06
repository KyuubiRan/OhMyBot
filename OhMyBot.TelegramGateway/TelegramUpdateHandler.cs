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

            if (IsSignInCommand(text))
            {
                // 签到类指令可能耗时，放到后台执行避免阻塞轮询；不再发送“请稍等...”占位消息，直接回复结果。
                _ = Task.Run(
                    () => ExecuteMessageCommandAsync(message, null, gatewayRequest, text, cancellationToken),
                    CancellationToken.None);
                return;
            }

            await ExecuteMessageCommandAsync(message, null, gatewayRequest, text, cancellationToken);
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
                ChatType = ToChatType(query.Message.Chat.Type),
                CallbackQueryId = query.Id,
                Payload = query.Data
            };

            // 不再预先回应“正在处理...”，否则回调会被标记为已响应；改为执行完成后再回应（可携带真正的提示文案）。
            _ = Task.Run(
                () => ExecuteCallbackAsync(botClient, query.Id, query.Message.Chat.Id, callbackRequest, cancellationToken),
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
        ITelegramBotClient botClient,
        string callbackQueryId,
        ChatId chatId,
        CallbackRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await commandGateway.ExecuteCallbackAsync(request, cancellationToken);
            await responseRenderer.RenderAsync(chatId, response, null, cancellationToken);
            // 执行完成后再回应回调，关闭按钮加载动画；如有提示文案则一并显示。
            await AnswerCallbackSafeAsync(botClient, callbackQueryId, response.CallbackAnswerText, response.CallbackAnswerAlert, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Failed to execute Telegram callback in background.");
            await AnswerCallbackSafeAsync(botClient, callbackQueryId, null, false, cancellationToken);
            if (int.TryParse(request.MessageId, out var messageId))
            {
                await RenderFailureSafeAsync(chatId, messageId, exception, cancellationToken);
            }
        }
    }

    private static async Task AnswerCallbackSafeAsync(
        ITelegramBotClient botClient,
        string callbackQueryId,
        string? text,
        bool showAlert,
        CancellationToken cancellationToken)
    {
        try
        {
            await botClient.AnswerCallbackQuery(
                callbackQueryId,
                string.IsNullOrEmpty(text) ? null : text,
                showAlert: showAlert,
                cancellationToken: cancellationToken);
        }
        catch (Exception)
        {
            // 回调可能已超时/已回应，忽略即可。
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
            // 与 Core 一致：异常原文可能带内网地址/上游 API 细节，只回关联 id，细节进日志。
            // 前缀 gw- 是给排查用的：这个 id 只存在于网关日志里，Core 的 journal 怎么搜都搜不到。
            // 拿到带 gw- 的 id 直接查 ohmybot-telegram，不带的查 ohmybot-core。
            var errorId = $"gw-{Guid.NewGuid().ToString("N")[..6]}";
            logger.LogError(exception, "Telegram gateway execution failed. errorId={ErrorId}.", errorId);
            var failureMessage = new TelegramMessage
            {
                Text = $"执行失败，请稍后重试。（错误 id: {errorId}）",
                ParseMode = TelegramParseMode.None
            };
            if (editMessageId is not null)
            {
                failureMessage.EditMessageId = editMessageId.Value.ToString();
            }

            await responseRenderer.RenderAsync(
                chatId,
                new CommandResponse
                {
                    Code = 1,
                    ErrorCode = "GatewayExecutionFailed",
                    Telegram = new TelegramResponse { Messages = { failureMessage } }
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
