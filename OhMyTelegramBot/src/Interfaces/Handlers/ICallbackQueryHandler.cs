using Telegram.Bot.Types;

namespace OhMyTelegramBot.Interfaces.Handlers;

public interface ICallbackQueryHandler
{
    public Task OnReceiveCallbackQuery(CallbackQuery query);
}