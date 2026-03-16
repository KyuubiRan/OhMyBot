using System.Diagnostics.CodeAnalysis;
using FoxTail.Extensions;
using OhMyLib.Attributes;
using OhMyLib.Enums;
using OhMyTelegramBot.Enums;
using OhMyTelegramBot.Interfaces;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace OhMyTelegramBot.Commands.OwnerCommands;

[Component(Key = "cmd__shutdown")]
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class ShutdownCommand : ICommand
{
    public UserPrivilege RequirePrivilege => UserPrivilege.Owner;
    public SupportedChatType SupportChatTypes => SupportedChatType.Private;

    public async Task OnReceiveCommand(ITelegramBotClient botClient, Message message, long chatId, long senderId, string[] args)
    {
        if (args.IsEmpty || !args[0].Equals("confirm", StringComparison.CurrentCultureIgnoreCase))
        {
            await botClient.SendMessage(
                chatId,
                "输入`/shutdown confirm`来确认关闭Bot",
                ParseMode.MarkdownV2
            );
        }
        else
        {
            await botClient.SendMessage(chatId, "正在关闭Bot...");
            await Task.Delay(2000);
            Environment.Exit(0);
        }
    }
}