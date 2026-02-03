using OhMyLib.Attributes;
using OhMyLib.Enums;
using OhMyLib.Utils;
using OhMyTelegramBot.Interfaces;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace OhMyTelegramBot.Commands.SuperAdminCommands;

[Component(Key = "cmd__status")]
public class SystemStatusCommand : ICommand
{
    public UserPrivilege RequirePrivilege => UserPrivilege.SuperAdmin;

    public async Task OnReceiveCommand(ITelegramBotClient botClient, Message message, long chatId, long senderId, string[] args)
    {
        await botClient.SendMessage(chatId, SystemUtils.GenSystemInfo());
    }
}