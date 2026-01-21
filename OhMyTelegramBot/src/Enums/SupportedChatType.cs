using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace OhMyTelegramBot.Enums;

[Flags]
public enum SupportedChatType
{
    None = 0,
    Private = 1 << 0,
    Group = 1 << 1,

    All = Group | Private
}

public static class SupportedChatTypeExtensions
{
    extension(SupportedChatType type)
    {
        public bool CanHandle(ChatType chatType)
        {
            return chatType switch
            {
                ChatType.Private => (type & SupportedChatType.Private) != 0,
                ChatType.Group or ChatType.Supergroup => (type & SupportedChatType.Group) != 0,
                _ => false,
            };
        }

        public bool CanHandle(Chat chat)
        {
            return type.CanHandle(chat.Type);
        }

        public bool CanHandle(Message msg)
        {
            return type.CanHandle(msg.Chat);
        }
    }
}