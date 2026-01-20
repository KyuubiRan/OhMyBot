using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OhMyLib.Attributes;
using OhMyTelegramBot.Commands;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace OhMyTelegramBot.MessageHandlers;

[Component]
public partial class CommandHandler(
    ILogger<CommandHandler> logger,
    ITelegramBotClient botClient,
    IServiceProvider serviceProvider
)
{
    public async Task HandleCommand(Message message, string command, params string[] args)
    {
        var chatId = message.Chat.Id;
        var senderId = message.From?.Id ?? 0;

        LogHandleCommand(chatId, senderId, command, args);

        // Example command handling logic
        var commandLower = command.ToLowerInvariant();
        var cmd = serviceProvider.GetKeyedService<ICommand>("cmd__" + commandLower);
        if (cmd != null) await cmd.OnReceiveCommand(botClient, message, chatId, senderId, args);
    }

    [LoggerMessage(LogLevel.Information, "Handling command '{command}' with args {args} from CID={chatId} (SID={senderId})")]
    private partial void LogHandleCommand(long chatId, long senderId, string command, string[] args);
}