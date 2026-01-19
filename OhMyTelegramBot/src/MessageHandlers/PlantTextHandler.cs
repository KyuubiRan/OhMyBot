using Microsoft.Extensions.Logging;
using OhMyBot.Attributes;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace OhMyTelegramBot.MessageHandlers;

[Component]
public sealed partial class PlantTextHandler(ILogger<PlantTextHandler> logger, ITelegramBotClient botClient)
{
    public void OnReceiveTextMessage(Message message)
    {
        var chatId = message.Chat.Id;
        var senderId = message.From?.Id ?? 0;
        var text = message.Text ?? string.Empty;

        LogReceivedMessage(chatId, senderId, text);
// #if DEBUG
//         botClient.SendMessage(chatId, text);
// #endif
    }

    [LoggerMessage(LogLevel.Information, "Received message from CID={chatId} (SID={senderId}): {text}")]
    private partial void LogReceivedMessage(long chatId, long senderId, string text);
}