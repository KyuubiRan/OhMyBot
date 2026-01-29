using Telegram.Bot.Types;

namespace OhMyTelegramBot.Interfaces;

public interface ICallbackQueryHandler
{
    public Task OnReceiveCallback(CallbackQuery query);
}