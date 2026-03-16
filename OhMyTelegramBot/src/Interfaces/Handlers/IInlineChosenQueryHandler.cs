using Telegram.Bot.Types;

namespace OhMyTelegramBot.Interfaces.Handlers;

public interface IInlineChosenQueryHandler
{
    public Task OnReceiveChosenInlineQuery(ChosenInlineResult chosenInlineResult);
}