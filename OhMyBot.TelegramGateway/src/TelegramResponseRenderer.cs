using OhMyBot.Contracts.Grpc;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace OhMyBot.TelegramGateway;

public sealed class TelegramResponseRenderer(ITelegramBotClient botClient)
{
    public async Task RenderAsync(
        ChatId chatId,
        CommandResponse response,
        int? fallbackReplyToMessageId,
        CancellationToken cancellationToken = default)
    {
        var textMessages = response.Messages.Select(message => message.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();

        if (response.Error is not null)
        {
            textMessages = [$"{response.Error.Code}: {response.Error.Message}"];
        }

        if (textMessages.Length == 0)
        {
            return;
        }

        var replyParameters = CreateReplyParameters(response.ReplyToMessageId, fallbackReplyToMessageId);
        var replyMarkup = CreateReplyMarkup(response.Buttons);

        for (var i = 0; i < textMessages.Length; i++)
        {
            await botClient.SendMessage(
                chatId,
                textMessages[i],
                replyParameters: i == 0 ? replyParameters : null,
                replyMarkup: i == 0 ? replyMarkup : null,
                cancellationToken: cancellationToken);
        }
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

    private static InlineKeyboardMarkup? CreateReplyMarkup(IEnumerable<ResponseButton> buttons)
    {
        var rows = buttons
            .Where(button => !string.IsNullOrWhiteSpace(button.Text) && !string.IsNullOrWhiteSpace(button.Payload))
            .Select(button => new[] { InlineKeyboardButton.WithCallbackData(button.Text, button.Payload) })
            .ToArray();

        return rows.Length == 0 ? null : new InlineKeyboardMarkup(rows);
    }
}
