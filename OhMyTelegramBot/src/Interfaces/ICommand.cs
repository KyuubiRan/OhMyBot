using OhMyLib.Enums;
using OhMyTelegramBot.Enums;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace OhMyTelegramBot.Interfaces;

public interface ICommand
{
    public SupportedChatType SupportChatTypes => SupportedChatType.All;
    public UserPrivilege RequirePrivilege => UserPrivilege.User;

    public Task OnReceiveCommand(ITelegramBotClient botClient, Message message, long chatId, long senderId, string[] args);
}