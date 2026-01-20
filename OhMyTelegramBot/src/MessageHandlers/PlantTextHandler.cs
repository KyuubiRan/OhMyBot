using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OhMyLib.Attributes;
using OhMyTelegramBot.Configs;
using Telegram.Bot.Types;

namespace OhMyTelegramBot.MessageHandlers;

[Component]
public sealed partial class PlantTextHandler(
    ILogger<PlantTextHandler> logger,
    IOptionsMonitor<BotConfig> config,
    CommandHandler commandHandler
)
{
    public async Task OnReceiveTextMessage(Message message)
    {
        var chatId = message.Chat.Id;
        var senderId = message.From?.Id ?? 0;
        var text = message.Text ?? string.Empty;

        #region Command Handling

        var commandPrefix = config.CurrentValue.CommandPrefixes;
        var commandPrefixMatch = commandPrefix.FirstOrDefault(prefix => text.StartsWith(prefix));
        if (commandPrefixMatch is not null)
        {
            var commandAndArgs = text[commandPrefixMatch.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var command = commandAndArgs.FirstOrDefault() ?? string.Empty;
            var args = commandAndArgs.Skip(1).ToArray();
            try
            {
                await commandHandler.HandleCommand(message, command, args);
            }
            catch (Exception e)
            {
                LogUnhandledCommandException(e, chatId, senderId, command, args);
            }

            return;
        }

        #endregion

        LogReceivedMessage(chatId, senderId, text);
    }

    [LoggerMessage(LogLevel.Information, "Received message from CID={chatId} (SID={senderId}): {text}")]
    private partial void LogReceivedMessage(long chatId, long senderId, string text);

    [LoggerMessage(LogLevel.Warning, "Unhandled command exception occurred! CID={chatId} (SID={senderId}), Command='{command}', Args={args}")]
    private partial void LogUnhandledCommandException(Exception e, long chatId, long senderId, string command, string[] args);
}