using OhMyLib.Enums;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace OhMyTelegramBot.Interfaces;

public interface ICommand
{
    public virtual UserPrivilege RequirePrivilege => UserPrivilege.User;

    public Task OnReceiveCommand(ITelegramBotClient botClient, Message message, long chatId, long senderId, string[] args);
}