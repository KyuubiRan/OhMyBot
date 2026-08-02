using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace OhMyBot.TelegramGateway;

internal static class TelegramMessageEntityExtensions
{
    public static MessageEntity? GetMessageEntityByType(this Message message, MessageEntityType type, int n = 0)
    {
        return message.Entities.GetMessageEntityByType(type, n);
    }

    public static MessageEntity? GetMessageEntityByType(this IEnumerable<MessageEntity>? entities, MessageEntityType type, int n = 0)
    {
        if (n < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(n), n, "n must be zero or greater.");
        }

        return entities?
            .Where(entity => entity.Type == type)
            .Skip(n)
            .FirstOrDefault();
    }

    public static MessageEntity? GetFirstCommandArgumentEntityByType(this Message message, MessageEntityType type)
    {
        var text = message.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var commandEnd = text.IndexOf(' ', StringComparison.Ordinal);
        if (commandEnd < 0)
        {
            return null;
        }

        return message.Entities?
            .Where(entity => entity.Type == type && entity.Offset > commandEnd)
            .OrderBy(entity => entity.Offset)
            .FirstOrDefault();
    }

    public static string? GetFirstCommandArgumentTextMentionUserId(this Message message)
    {
        return message.GetFirstCommandArgumentEntityByType(MessageEntityType.TextMention)?.User?.Id.ToString();
    }

    public static User? GetFirstCommandArgumentTextMentionUser(this Message message)
    {
        return message.GetFirstCommandArgumentEntityByType(MessageEntityType.TextMention)?.User;
    }

    public static string? GetEntityText(this Message message, MessageEntity entity)
    {
        var text = message.Text;
        if (string.IsNullOrEmpty(text)
            || entity.Offset < 0
            || entity.Length < 0
            || entity.Offset + entity.Length > text.Length)
        {
            return null;
        }

        return text.Substring(entity.Offset, entity.Length);
    }
}
