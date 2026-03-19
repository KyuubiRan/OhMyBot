using Telegram.Bot.Types.ReplyMarkups;

namespace OhMyTelegramBot.Interfaces.Inline;

public interface IInlineQuery
{
    public string[] QueryKeys { get; }
    public int Priority => 0;
    public string Id { get; }
    public InlineKeyboardMarkup? ReplyMarkup => null;
}