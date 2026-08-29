using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Google.Protobuf;
using OhMyBot.Contracts;
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
    private readonly SemaphoreSlim _updateConcurrency = new(
        int.Clamp(options.Value.MaxConcurrentUpdates, 1, 64));

    public async Task HandleUpdateAsync(
        ITelegramBotClient botClient,
        Update update,
        CancellationToken cancellationToken)
    {
        await _updateConcurrency.WaitAsync(cancellationToken);
        _ = ProcessUpdateWithLeaseAsync(botClient, update, cancellationToken);
    }

    private async Task ProcessUpdateWithLeaseAsync(
        ITelegramBotClient botClient,
        Update update,
        CancellationToken cancellationToken)
    {
        try
        {
            await ProcessUpdateAsync(botClient, update, cancellationToken);
        }
        finally
        {
            _updateConcurrency.Release();
        }
    }

    private async Task ProcessUpdateAsync(
        ITelegramBotClient botClient,
        Update update,
        CancellationToken cancellationToken)
    {
        try
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
                    await RecordUserProfileSafeAsync(gatewayRequest, cancellationToken);
                    return;
                }

                if (!commandGateway.CanHandle(text))
                {
                    await RecordUserProfileSafeAsync(gatewayRequest, cancellationToken);
                    return;
                }

                await ExecuteMessageCommandAsync(botClient, message, null, gatewayRequest, text, cancellationToken);
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
                await ExecuteCallbackAsync(botClient, query.Id, query.Message.Chat.Id, callbackRequest, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled exception while processing Telegram update in background.");
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
        ITelegramBotClient botClient,
        Message message,
        int? processingMessageId,
        GatewayCommandRequest gatewayRequest,
        string text,
        CancellationToken cancellationToken)
    {
        long? progressStageStartedAt = null;
        try
        {
            await RecordUserProfileSafeAsync(gatewayRequest, cancellationToken);
            await RecordCommandTargetProfileAsync(message, text, cancellationToken);
            var progressStyle = commandGateway.GetProgressStyle(text);
            if (progressStyle == CommandProgressStyle.MediaConversion
                && HasReplyMedia(message.ReplyToMessage)
                && HasCommandArguments(text))
            {
                processingMessageId = await SendProcessingMessageSafeAsync(
                    botClient,
                    message,
                    "下载中...",
                    cancellationToken);
                if (processingMessageId is not null)
                {
                    progressStageStartedAt = Stopwatch.GetTimestamp();
                }
            }

            if (commandGateway.AcceptsReplyMedia(text))
            {
                gatewayRequest = gatewayRequest with
                {
                    ReplyMedia = await DownloadReplyMediaAsync(botClient, message.ReplyToMessage, cancellationToken)
                };
            }

            if (processingMessageId is not null)
            {
                await WaitForMinimumProgressStageAsync(progressStageStartedAt, cancellationToken);
                await EditProcessingMessageSafeAsync(
                    botClient,
                    message.Chat.Id,
                    processingMessageId.Value,
                    "转换中...",
                    cancellationToken);
                progressStageStartedAt = Stopwatch.GetTimestamp();
            }

            var response = await commandGateway.ExecuteAsync(gatewayRequest, _options.BotInstanceId, cancellationToken);
            if (processingMessageId is not null)
            {
                if (HasTelegramMedia(response))
                {
                    await WaitForMinimumProgressStageAsync(progressStageStartedAt, cancellationToken);
                    await EditProcessingMessageSafeAsync(
                        botClient,
                        message.Chat.Id,
                        processingMessageId.Value,
                        "上传中...",
                        cancellationToken);
                    progressStageStartedAt = Stopwatch.GetTimestamp();
                    await responseRenderer.RenderAsync(message.Chat.Id, response, message.MessageId, cancellationToken);
                    await WaitForMinimumProgressStageAsync(progressStageStartedAt, cancellationToken);
                    await DeleteProcessingMessageSafeAsync(
                        botClient,
                        message.Chat.Id,
                        processingMessageId.Value,
                        cancellationToken);
                    return;
                }

                await WaitForMinimumProgressStageAsync(progressStageStartedAt, cancellationToken);
                EditFirstTelegramText(response, processingMessageId.Value);
            }

            await responseRenderer.RenderAsync(message.Chat.Id, response, message.MessageId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Failed to execute Telegram command in background.");
            await WaitForMinimumProgressStageAsync(progressStageStartedAt, cancellationToken);
            await RenderFailureSafeAsync(message.Chat.Id, processingMessageId, message.MessageId, exception, cancellationToken);
        }
    }

    private async Task WaitForMinimumProgressStageAsync(
        long? stageStartedAt,
        CancellationToken cancellationToken)
    {
        if (stageStartedAt is null)
        {
            return;
        }

        var minimumMilliseconds = int.Clamp(
            _options.MediaProgressMinimumStageMilliseconds,
            0,
            5_000);
        var remaining = TimeSpan.FromMilliseconds(minimumMilliseconds)
            - Stopwatch.GetElapsedTime(stageStartedAt.Value);
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, cancellationToken);
        }
    }

    private bool HasCommandArguments(string text)
    {
        var (_, args) = GatewayCommandParser.Parse(
            text,
            _options.CommandPrefixes,
            stripBotMention: true);
        return args.Length > 0;
    }

    private static bool HasReplyMedia(Message? repliedMessage)
    {
        return repliedMessage is
        {
            Photo.Length: > 0
        } or
        {
            Animation: not null
        } or
        {
            Video: not null
        } or
        {
            Sticker: not null
        } or
        {
            Document: not null
        };
    }

    private static bool HasTelegramMedia(CommandResponse response)
    {
        return response.PlatformResponseCase == CommandResponse.PlatformResponseOneofCase.Telegram
            && response.Telegram.Messages.Any(item =>
                item.Document is { Content.Length: > 0 }
                || item.Sticker is { Content.Length: > 0 });
    }

    private static void EditFirstTelegramText(CommandResponse response, int messageId)
    {
        if (response.PlatformResponseCase != CommandResponse.PlatformResponseOneofCase.Telegram)
        {
            return;
        }

        var message = response.Telegram.Messages.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.Text));
        if (message is null)
        {
            return;
        }

        message.EditMessageId = messageId.ToString();
        message.ReplyToMessageId = string.Empty;
    }

    private async Task<int?> SendProcessingMessageSafeAsync(
        ITelegramBotClient botClient,
        Message commandMessage,
        string text,
        CancellationToken cancellationToken)
    {
        try
        {
            var sent = await botClient.SendMessage(
                commandMessage.Chat.Id,
                text,
                replyParameters: new ReplyParameters
                {
                    MessageId = commandMessage.MessageId,
                    AllowSendingWithoutReply = true
                },
                cancellationToken: cancellationToken);
            logger.LogInformation(
                "Sent Telegram command progress message {MessageId} for chat {ChatId}.",
                sent.MessageId,
                commandMessage.Chat.Id);
            return sent.MessageId;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Failed to send Telegram command progress message.");
            return null;
        }
    }

    private async Task EditProcessingMessageSafeAsync(
        ITelegramBotClient botClient,
        ChatId chatId,
        int messageId,
        string text,
        CancellationToken cancellationToken)
    {
        try
        {
            await botClient.EditMessageText(chatId, messageId, text, cancellationToken: cancellationToken);
            logger.LogInformation(
                "Edited Telegram command progress message {MessageId} for chat {ChatId}.",
                messageId,
                chatId);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Failed to edit Telegram command progress message {MessageId}.", messageId);
        }
    }

    private async Task DeleteProcessingMessageSafeAsync(
        ITelegramBotClient botClient,
        ChatId chatId,
        int messageId,
        CancellationToken cancellationToken)
    {
        try
        {
            await botClient.DeleteMessage(chatId, messageId, cancellationToken);
            logger.LogInformation(
                "Deleted Telegram command progress message {MessageId} for chat {ChatId}.",
                messageId,
                chatId);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Failed to delete Telegram command progress message {MessageId}.", messageId);
        }
    }

    private static async Task<CommandMedia?> DownloadReplyMediaAsync(
        ITelegramBotClient botClient,
        Message? repliedMessage,
        CancellationToken cancellationToken)
    {
        var photo = repliedMessage?.Photo?
            .OrderByDescending(item => item.FileSize ?? 0)
            .ThenByDescending(item => (long)item.Width * item.Height)
            .FirstOrDefault();
        if (photo is not null)
        {
            return await DownloadFileAsync(
                botClient,
                photo,
                $"photo_{photo.FileUniqueId}.jpg",
                "image/jpeg",
                CommandMediaKind.Photo,
                photo.Width,
                photo.Height,
                cancellationToken);
        }

        if (repliedMessage?.Animation is { } animation)
        {
            return await DownloadFileAsync(
                botClient,
                animation,
                string.IsNullOrWhiteSpace(animation.FileName)
                    ? $"animation_{animation.FileUniqueId}.gif"
                    : animation.FileName,
                string.IsNullOrWhiteSpace(animation.MimeType) ? "image/gif" : animation.MimeType,
                CommandMediaKind.Animation,
                animation.Width,
                animation.Height,
                cancellationToken);
        }

        if (repliedMessage?.Video is { } video)
        {
            return await DownloadFileAsync(
                botClient,
                video,
                string.IsNullOrWhiteSpace(video.FileName)
                    ? $"video_{video.FileUniqueId}.mp4"
                    : video.FileName,
                string.IsNullOrWhiteSpace(video.MimeType) ? "video/mp4" : video.MimeType,
                CommandMediaKind.Video,
                video.Width,
                video.Height,
                cancellationToken);
        }

        if (repliedMessage?.Sticker is { } sticker)
        {
            var (fileName, contentType) = sticker.IsVideo
                ? ($"sticker_{sticker.FileUniqueId}.webm", "video/webm")
                : sticker.IsAnimated
                    ? ($"sticker_{sticker.FileUniqueId}.tgs", "application/x-tgsticker")
                    : ($"sticker_{sticker.FileUniqueId}.webp", "image/webp");
            return await DownloadFileAsync(
                botClient,
                sticker,
                fileName,
                contentType,
                CommandMediaKind.Sticker,
                sticker.Width,
                sticker.Height,
                cancellationToken);
        }

        if (repliedMessage?.Document is { } document)
        {
            return await DownloadFileAsync(
                botClient,
                document,
                string.IsNullOrWhiteSpace(document.FileName)
                    ? $"document_{document.FileUniqueId}"
                    : document.FileName,
                string.IsNullOrWhiteSpace(document.MimeType)
                    ? "application/octet-stream"
                    : document.MimeType,
                CommandMediaKind.Document,
                0,
                0,
                cancellationToken);
        }

        return null;
    }

    private static async Task<CommandMedia> DownloadFileAsync(
        ITelegramBotClient botClient,
        FileBase file,
        string fileName,
        string contentType,
        CommandMediaKind kind,
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        if (file.FileSize is > CommandMediaLimits.MaxContentBytes)
        {
            throw new InvalidDataException("The replied media exceeds the command media limit.");
        }

        await using var stream = new SizeLimitedMemoryStream(CommandMediaLimits.MaxContentBytes);
        await botClient.GetInfoAndDownloadFile(file, stream, cancellationToken);
        return new CommandMedia
        {
            FileName = fileName,
            ContentType = contentType,
            Content = ByteString.CopyFrom(stream.GetBuffer(), 0, checked((int)stream.Length)),
            Width = width,
            Height = height,
            Kind = kind
        };
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

}
