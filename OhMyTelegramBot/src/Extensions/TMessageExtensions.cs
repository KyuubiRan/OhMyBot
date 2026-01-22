using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace OhMyTelegramBot.Extensions;

// ReSharper disable once InconsistentNaming
public static class TMessageExtensions
{
    extension(Message msg)
    {
        public List<User> GetAllTextMentionedUsers()
        {
            var users = new List<User>();
            if (msg.Entities is null)
                return users;

            foreach (var entity in msg.Entities)
            {
                if (entity is { Type: MessageEntityType.TextMention, User: not null })
                {
                    users.Add(entity.User);
                }
            }

            return users;
        }

        public User? GetTextMentionedUser(int index = 0) =>
            msg.Entities?.Where(e => e.Type is MessageEntityType.TextMention)
                .ElementAtOrDefault(index)
                ?.User;

        public bool TryGetTextMentionedUser(out User? user, int index = 0)
        {
            user = msg.GetTextMentionedUser(index);
            return user is not null;
        }

        public User? GetReplyUser() => msg.ReplyToMessage?.From;

        public bool TryGetReplyUser(out User? user)
        {
            user = msg.GetReplyUser();
            return user is not null;
        }
    }
}