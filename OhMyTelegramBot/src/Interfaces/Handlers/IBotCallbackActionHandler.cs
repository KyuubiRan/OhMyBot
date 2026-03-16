using OhMyTelegramBot.Models;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace OhMyTelegramBot.Interfaces.Handlers;

public interface IBotCallbackActionHandler
{
    public bool OnlyForOwner => true;

    public Task OnReceiveAction(ITelegramBotClient botClient, CallbackQuery query, BotAction action);
}