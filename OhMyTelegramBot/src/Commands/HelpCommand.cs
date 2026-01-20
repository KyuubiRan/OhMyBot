using OhMyLib.Attributes;
using OhMyLib.Services;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace OhMyTelegramBot.Commands;

[Component(Key = "cmd__help")]
public class HelpCommand(BotUserService userService) : ICommand
{
    
    
    public async Task OnReceiveCommand(ITelegramBotClient botClient, Message message, long chatId, long senderId, string[] args)
    {
        await botClient.SendMessage(chatId, "邦邦！");
    }
}