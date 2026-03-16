using Telegram.Bot.Types.ReplyMarkups;

namespace OhMyTelegramBot.Interfaces.Inline;

public interface IInlineQuery
{
    public string[] QueryKeys { get; }
    public string Id { get; }
    public InlineKeyboardMarkup? ReplyMarkup => null;
}