using Telegram.Bot.Types.Enums;

namespace OhMyBot.TelegramGateway.Rendering;

public sealed record TelegramTextMessage(
    string Text,
    ParseMode ParseMode = default) : TelegramOutgoingMessage
{
    public static TelegramTextMessage PlainText(string text)
    {
        return new TelegramTextMessage(text);
    }

    public static TelegramTextMessage Markdown(string text)
    {
        return new TelegramTextMessage(text, ParseMode.MarkdownV2);
    }

    public static TelegramTextMessage Html(string text)
    {
        return new TelegramTextMessage(text, ParseMode.Html);
    }
}
