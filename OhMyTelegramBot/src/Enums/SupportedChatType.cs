using FoxTail.Extensions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace OhMyTelegramBot.Enums;

[Flags]
public enum SupportedChatType
{
    None = 0,
    Private = 1 << 0,
    Group = 1 << 1,
    Channel = 1 << 2,

    All = Group | Private | Channel
}

public static class SupportedChatTypeExtensions
{
    private static readonly Dictionary<SupportedChatType, string> TypeStringDict = new()
    {
        [SupportedChatType.Private] = "私聊",
        [SupportedChatType.Group] = "群组",
        [SupportedChatType.Channel] = "频道",
    };
    
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
        
        public string TypeStrings()
        {
            if (type == SupportedChatType.None)
                return "";

            return TypeStringDict.Where(kv => (type & kv.Key) != 0)
                                 .Select(kv => kv.Value)
                                 .JoinToString("或");
        }
    }
}