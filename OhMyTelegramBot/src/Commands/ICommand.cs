using Telegram.Bot;
using Telegram.Bot.Types;

namespace OhMyTelegramBot.Commands;

public interface ICommand
{
    public Task OnReceiveCommand(ITelegramBotClient botClient, Message message, long chatId, long senderId, string[] args);
}