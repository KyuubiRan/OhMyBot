using OhMyTelegramBot.Enums;
using Telegram.Bot.Types;

namespace OhMyTelegramBot.Interfaces.Handlers;

public interface IMessageHandler
{
    public SupportedChatType SupportChatTypes => SupportedChatType.All;

    public Task OnReceiveMessage(Message message);
}