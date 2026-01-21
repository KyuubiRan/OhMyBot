using OhMyLib.Enums;
using Telegram.Bot.Types;

namespace OhMyTelegramBot.Interfaces;

public interface IMessageHandler
{
    public virtual ChatType SupportChatTypes => ChatType.All;
    
    public Task OnReceiveMessage(Message message);
}