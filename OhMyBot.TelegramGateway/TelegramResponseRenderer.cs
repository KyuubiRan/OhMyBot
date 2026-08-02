using OhMyBot.Contracts.Grpc;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace OhMyBot.TelegramGateway;

// 通用发送器：Core 已产出最终 Telegram 内容（MarkdownV2/纯文本 + 按钮 + 回复/编辑），
// 网关只负责把每条 TelegramMessage 发出去，不再做任何按命令/按类型的渲染。
public sealed class TelegramResponseRenderer(ITelegramBotClient botClient)
{
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
            if (string.IsNullOrWhiteSpace(message.Text))
            {
                continue;
            }

            var parseMode = ToParseMode(message.ParseMode);
            var replyMarkup = CreateReplyMarkup(message.ButtonRows);

            if (!string.IsNullOrWhiteSpace(message.EditMessageId) && int.TryParse(message.EditMessageId, out var editMessageId))
            {
                await botClient.EditMessageText(
                    chatId,
                    editMessageId,
                    message.Text,
                    parseMode: parseMode,
                    replyMarkup: replyMarkup,
                    cancellationToken: cancellationToken);
            }
            else
            {
                await botClient.SendMessage(
                    chatId,
                    message.Text,
                    parseMode: parseMode,
                    replyParameters: CreateReplyParameters(message.ReplyToMessageId, fallbackReplyToMessageId),
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
