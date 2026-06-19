using OhMyBot.Contracts.Grpc;
using OhMyBot.TelegramGateway.Rendering;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace OhMyBot.TelegramGateway;

public sealed class TelegramResponseRenderer(
    ITelegramBotClient botClient,
    IEnumerable<ITelegramCommandResultRenderer> renderers)
{
    public async Task RenderAsync(
        ChatId chatId,
        CommandResponse response,
        int? fallbackReplyToMessageId,
        CancellationToken cancellationToken = default)
    {
        var outgoingMessages = renderers
            .First(renderer => renderer.CanRender(response))
            .Render(response)
            .Where(message => message is not TelegramTextMessage textMessage || !string.IsNullOrWhiteSpace(textMessage.Text))
            .ToArray();

        if (outgoingMessages.Length == 0)
        {
            return;
        }

        var replyParameters = CreateReplyParameters(response.ReplyToMessageId, fallbackReplyToMessageId);
        var replyMarkup = CreateReplyMarkup(response.Buttons);

        for (var i = 0; i < outgoingMessages.Length; i++)
        {
            await RenderMessageAsync(
                botClient,
                chatId,
                outgoingMessages[i],
                i == 0 ? replyParameters : null,
                i == 0 ? replyMarkup : null,
                cancellationToken);
        }
    }

    private static async Task RenderMessageAsync(
        ITelegramBotClient botClient,
        ChatId chatId,
        TelegramOutgoingMessage message,
        ReplyParameters? replyParameters,
        InlineKeyboardMarkup? replyMarkup,
        CancellationToken cancellationToken)
    {
        switch (message)
        {
            case TelegramTextMessage textMessage:
                await botClient.SendMessage(
                    chatId,
                    textMessage.Text,
                    parseMode: textMessage.ParseMode,
                    replyParameters: replyParameters,
                    replyMarkup: replyMarkup,
                    cancellationToken: cancellationToken);
                return;
            default:
                throw new NotSupportedException($"Telegram outgoing message type is not supported yet: {message.GetType().Name}.");
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
