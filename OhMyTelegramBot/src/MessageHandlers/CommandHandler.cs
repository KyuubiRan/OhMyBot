using Microsoft.Extensions.Logging;
using OhMyLib.Attributes;
using OhMyLib.Enums.Kuro;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace OhMyTelegramBot.MessageHandlers;

[Component]
public partial class CommandHandler(
    ILogger<CommandHandler> logger,
    ITelegramBotClient botClient
)
{
    public async Task HandleCommand(Message message, string command, params string[] args)
    {
        var chatId = message.Chat.Id;
        var senderId = message.From?.Id ?? 0;

        LogHandleCommand(chatId, senderId, command, args);

        // Example command handling logic
        switch (command.ToLower())
        {
            case "start":
                await botClient.SendMessage(chatId, "喵喵喵？！");
                break;
            case "help":
                await botClient.SendMessage(chatId, "邦邦！");
                break;
            default:
                await botClient.SendMessage(chatId, $"Unknown command: {command}");
                break;
        }
    }

    [LoggerMessage(LogLevel.Information, "Handling command '{command}' with args {args} from CID={chatId} (SID={senderId})")]
    private partial void LogHandleCommand(long chatId, long senderId, string command, string[] args);
}