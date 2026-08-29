using OhMyBot.Contracts.Grpc;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace OhMyBot.TelegramGateway;

// 通用发送器：Core 已产出最终 Telegram 内容（MarkdownV2/纯文本 + 按钮 + 回复/编辑），
// 网关只负责把每条 TelegramMessage 发出去，不再做任何按命令/按类型的渲染。
public sealed class TelegramResponseRenderer(
    ITelegramBotClient botClient,
    TelegramProgressMessageStore progressMessages)
{
    private static readonly TimeSpan ProgressMessageWait = TimeSpan.FromSeconds(2);

    public async Task RenderAsync(
        ChatId chatId,
        CommandResponse response,
        int? fallbackReplyToMessageId,
        CancellationToken cancellationToken = default)
    {
        if (response.PlatformResponseCase != CommandResponse.PlatformResponseOneofCase.Telegram)
        {
            return;
        }

        foreach (var message in response.Telegram.Messages)
        {
            var parseMode = ToParseMode(message.ParseMode);
            var replyMarkup = CreateReplyMarkup(message.ButtonRows);
            var replyParameters = CreateReplyParameters(message.ReplyToMessageId, fallbackReplyToMessageId);

            if (message.Sticker is { Content.Length: > 0 } sticker)
            {
                await using var stream = new MemoryStream(sticker.Content.ToByteArray(), writable: false);
                await botClient.SendSticker(
                    chatId,
                    InputFile.FromStream(stream, sticker.FileName),
                    replyParameters: replyParameters,
                    replyMarkup: replyMarkup,
                    cancellationToken: cancellationToken);
                continue;
            }

            if (message.Document is { Content.Length: > 0 } document)
            {
                await using var stream = new MemoryStream(document.Content.ToByteArray(), writable: false);
                await botClient.SendDocument(
                    chatId,
                    InputFile.FromStream(stream, document.FileName),
                    caption: string.IsNullOrWhiteSpace(message.Text) ? null : message.Text,
                    parseMode: parseMode,
                    replyParameters: replyParameters,
                    replyMarkup: replyMarkup,
                    cancellationToken: cancellationToken);
                continue;
            }

            if (string.IsNullOrWhiteSpace(message.Text))
            {
                continue;
            }

            var logicalEditMessageKey = string.Empty;
            int? editMessageId = null;
            if (!string.IsNullOrWhiteSpace(message.EditMessageId))
            {
                if (int.TryParse(message.EditMessageId, out var parsedEditMessageId))
                {
                    editMessageId = parsedEditMessageId;
                }
                else
                {
                    logicalEditMessageKey = message.EditMessageId;
                    editMessageId = await progressMessages.WaitForAsync(
                        logicalEditMessageKey,
                        ProgressMessageWait,
                        cancellationToken);
                }
            }

            if (editMessageId is not null)
            {
                try
                {
                    await botClient.EditMessageText(
                        chatId,
                        editMessageId.Value,
                        message.Text,
                        parseMode: parseMode,
                        replyMarkup: replyMarkup,
                        cancellationToken: cancellationToken);
                }
                finally
                {
                    if (logicalEditMessageKey.Length > 0)
                    {
                        progressMessages.Complete(logicalEditMessageKey);
                    }
                }
            }
            else
            {
                if (logicalEditMessageKey.Length > 0)
                {
                    progressMessages.Complete(logicalEditMessageKey);
                }

                await botClient.SendMessage(
                    chatId,
                    message.Text,
                    parseMode: parseMode,
                    replyParameters: replyParameters,
                    replyMarkup: replyMarkup,
                    cancellationToken: cancellationToken);
            }
        }
    }

    private static ParseMode ToParseMode(TelegramParseMode parseMode)
    {
        return parseMode switch
        {
            TelegramParseMode.MarkdownV2 => ParseMode.MarkdownV2,
            TelegramParseMode.Html => ParseMode.Html,
            _ => ParseMode.None
        };
    }

    private static ReplyParameters? CreateReplyParameters(string replyToMessageId, int? fallbackReplyToMessageId)
    {
        if (int.TryParse(replyToMessageId, out var parsedMessageId))
        {
            return new ReplyParameters
            {
                MessageId = parsedMessageId,
                AllowSendingWithoutReply = true
            };
        }

        return fallbackReplyToMessageId is null
            ? null
            : new ReplyParameters
            {
                MessageId = fallbackReplyToMessageId.Value,
                AllowSendingWithoutReply = true
            };
    }

    private static InlineKeyboardMarkup? CreateReplyMarkup(IEnumerable<ResponseButtonRow> buttonRows)
    {
        var rows = buttonRows
            .Select(row => row.Buttons
                .Where(button => !string.IsNullOrWhiteSpace(button.Text) && !string.IsNullOrWhiteSpace(button.Payload))
                .Select(button => InlineKeyboardButton.WithCallbackData(button.Text, button.Payload))
                .ToArray())
            .Where(row => row.Length > 0)
            .ToArray();

        return rows.Length == 0 ? null : new InlineKeyboardMarkup(rows);
    }
}
