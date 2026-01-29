using OhMyTelegramBot.Models;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace OhMyTelegramBot.Interfaces;

public interface IBotActionHandler
{
    public bool OnlyForOwner => true;

    public Task OnReceiveAction(ITelegramBotClient botClient, CallbackQuery query, BotAction action);
}