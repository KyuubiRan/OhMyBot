using OhMyLib.Attributes;
using OhMyTelegramBot.Enums;
using OhMyTelegramBot.Interfaces;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace OhMyTelegramBot.Commands.UserCommands;

[Component(Key = "cmd__start")]
public sealed class StartCommand : ICommand
{
    public SupportedChatType SupportChatTypes => SupportedChatType.Private;

    public async Task OnReceiveCommand(ITelegramBotClient botClient, Message message, long chatId, long senderId, string[] args)
    {
        await botClient.SendMessage(chatId, "喵喵喵？！");
    }
}