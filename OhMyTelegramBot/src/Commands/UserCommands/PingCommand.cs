using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using OhMyLib.Attributes;
using OhMyTelegramBot.Interfaces;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace OhMyTelegramBot.Commands.UserCommands;

[Component(Key = "cmd__ping")]
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class PingCommand : ICommand
{
    public async Task OnReceiveCommand(ITelegramBotClient botClient, Message message, long chatId, long senderId, string[] args)
    {
        var stopwatch = Stopwatch.StartNew();
        var m = await botClient.SendMessage(chatId, "Pong!", replyParameters: message);
        stopwatch.Stop();
        await botClient.EditMessageText(chatId, m.MessageId, $"Pong! | {stopwatch.ElapsedMilliseconds} ms");
    }
}