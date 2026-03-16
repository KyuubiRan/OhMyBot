using Telegram.Bot.Types;

namespace OhMyTelegramBot.Interfaces.Handlers;

public interface IInlineQueryHandler
{
    public Task OnReceiveInlineQuery(InlineQuery query);

    public Task OnReceiveChosenInlineQuery(ChosenInlineResult query);
}