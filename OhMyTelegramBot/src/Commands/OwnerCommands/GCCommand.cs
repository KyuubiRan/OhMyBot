using OhMyLib.Attributes;
using OhMyLib.Enums;
using OhMyTelegramBot.Interfaces;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace OhMyTelegramBot.Commands.OwnerCommands;

[Component(Key = "cmd__gc")]
// ReSharper disable once InconsistentNaming
public class GCCommand : ICommand
{
    public UserPrivilege RequirePrivilege => UserPrivilege.Owner;

    public async Task OnReceiveCommand(ITelegramBotClient botClient, Message message, long chatId, long senderId, string[] args)
    {
        GC.Collect(2, GCCollectionMode.Aggressive, true);
        await botClient.SendMessage(chatId, "垃圾回收完成", replyParameters: message);
    }
}